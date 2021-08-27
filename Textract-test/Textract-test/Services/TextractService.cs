﻿using Amazon.Runtime;
using Amazon.Textract;
using Amazon.Textract.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Textract_test.Common;
using Textract_test.Models;

namespace Textract_test.Services
{
    public interface ITextractService
    {
        Task<TextractDocument> AnalyzeDocument(string bucket, string filename);
    }

    public class TextractService: ITextractService
    {
        private readonly AwsSettingsOptions _awsSettings;
        private readonly ISqsService _sqsService;
        private Dictionary<string, Block> _keyDict;
        private Dictionary<string, Block> _valueDict;
        private Dictionary<string, Block> _blockDict;

        public TextractService(IOptions<AwsSettingsOptions> awsSettings, ISqsService sqsService)
        {
            _awsSettings = awsSettings.Value;
            _sqsService = sqsService;
            _keyDict = new Dictionary<string, Block>();
            _valueDict = new Dictionary<string, Block>();
            _blockDict = new Dictionary<string, Block>();
        }
        
        // AnalyzeDocumentAsync only handles single-page jpg or pngs.
        // To analyze multi-page PDFs, use StartDocumentAnalysis to trigger the Textract job
        // poll the Amazon SQS queue to retrieve the completion status published by Amazon Textract when a text detection request completes.
        // Obtain the result using GetDocumentAnalysisRequest to get a GetDocumentAnalysisResponse, then execute the below code. 
        public async Task<TextractDocument> AnalyzeDocument(string bucket, string filename)
        {
            var textractDoc = new TextractDocument();
            
            // Automatically gets AWS creds from default config/credential files on system
            using (var client = new AmazonTextractClient())
            {
                var done = false;
                string paginationToken = null;

                // Trigger a Textract Document Analysis job, completion status gets published to SQS.
                var startAnalysisRequest = BuildStartDocumentAnalysisRequest(bucket, filename);
                var textractJob = await client.StartDocumentAnalysisAsync(startAnalysisRequest);

                // Wait for Textract to finish processing - poll SQS for success status
                var completedJobId = await _sqsService.ProcessTextractJob(textractJob.JobId);

                // Get Textract analysis after job completed
                while (!done)
                {
                    var getAnalysisRequest = new GetDocumentAnalysisRequest
                    {
                        JobId = completedJobId,
                        NextToken = paginationToken
                    };
                    var completedAnalysis = await client.GetDocumentAnalysisAsync(getAnalysisRequest);

                    // lines and words
                    var lines = GetDocumentLinesOrWords(completedAnalysis, BlockType.LINE);
                    var words = GetDocumentLinesOrWords(completedAnalysis, BlockType.WORD);
                    textractDoc.Lines = lines;
                    textractDoc.Words = words;

                    // Key-value pairs
                    var keyValuePairs = GetKeyValuePairs(completedAnalysis);
                    textractDoc.KeyValuePairs = keyValuePairs;

                    // Get next page
                    paginationToken = completedAnalysis.NextToken;
                    done = paginationToken == null ? true : false;
                }

                // Tables
                // TODO: Handle tables
            }

            return textractDoc;
        }

        // https://docs.aws.amazon.com/textract/latest/dg/api-async.html
        private StartDocumentAnalysisRequest BuildStartDocumentAnalysisRequest(string bucket, string filename)
        {
            return new StartDocumentAnalysisRequest
            {
                DocumentLocation = new DocumentLocation
                {
                    S3Object = new S3Object
                    {
                        Bucket = bucket,
                        Name = filename
                    }
                },
                FeatureTypes = new List<string> { Constants.FeatureTypes.Forms, Constants.FeatureTypes.Tables },
            // https://docs.aws.amazon.com/textract/latest/dg/api-async-roles.html
            NotificationChannel = new NotificationChannel
                {
                    RoleArn = _awsSettings.TextractIamRoleArn,
                    SNSTopicArn = _awsSettings.SnsArn
            },
                JobTag = "BankStatement", // JobTags help identify the job/groups of jobs

            };
        }

        private List<string> GetDocumentLinesOrWords(GetDocumentAnalysisResponse analysis, string blockType)
        {
            return analysis.Blocks
                .Where(block => block.BlockType == blockType)
                .Select(block => block.Text)
                .ToList();
        }

        private Dictionary<string,string> GetKeyValuePairs(
            GetDocumentAnalysisResponse analysis)
        {

            PopulateKeyValueDicts(analysis.Blocks);

            var keyValuePairs = GetKeyValueRelationship();

            return keyValuePairs;
        }

        private void PopulateKeyValueDicts(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                _blockDict.Add(block.Id, block);

                if (block.BlockType == Constants.KeyValueSet.BlockTypes.KeyValueSet)
                {
                    if (block.EntityTypes.Contains(Constants.KeyValueSet.EntityTypes.Key))
                    {
                        _keyDict.Add(block.Id, block);
                    }
                    else
                    {
                        _valueDict.Add(block.Id, block);
                    }
                }
            }
        }

        private Dictionary<string,string> GetKeyValueRelationship()
        {
            var keyValuePairs = new Dictionary<string, string>();

            foreach (var key in _keyDict.Keys)
            {
                var keyBlock = _keyDict[key];
                var valueBlock = GetValueBlock(keyBlock);

                var keyText = GetKeyValueText(keyBlock);
                var valueText = GetKeyValueText(valueBlock);

                if (keyText != null)
                {
                    keyValuePairs[keyText] = valueText;
                }
            }

            return keyValuePairs;
        }

        private Block GetValueBlock(Block keyBlock)
        {
            var valueBlockId = keyBlock.Relationships
                .Where(rel => rel.Type == Constants.KeyValueSet.RelationshipTypes.Value)
                .FirstOrDefault().Ids.FirstOrDefault();

            return _valueDict[valueBlockId];
        }

        private string GetKeyValueText(Block keyValueBlock)
        {
            string keyValueText = null;
            foreach (var relationship in keyValueBlock.Relationships)
            {
                if (relationship.Type == Constants.KeyValueSet.RelationshipTypes.Child)
                {
                    foreach (var id in relationship.Ids)
                    {
                        var word = _blockDict[id];
                        if (word.BlockType == Constants.KeyValueSet.BlockTypes.Word)
                        {
                            keyValueText += word.Text;
                        }
                        else if (word.BlockType == Constants.KeyValueSet.BlockTypes.SelectionElement)
                        {
                            if (word.SelectionStatus == Constants.KeyValueSet.BlockTypes.SelectionStatus)
                            {
                                keyValueText += "X ";
                            }
                        }
                    }
                }
            }

            return keyValueText;
        }
    }
}
