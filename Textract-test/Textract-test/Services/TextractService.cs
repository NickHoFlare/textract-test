using Amazon.Runtime;
using Amazon.Textract;
using Amazon.Textract.Model;
using Microsoft.Extensions.Configuration;
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
        Task<TextractDocument> AnalyzeDocumentAsync(string bucket, string filename);
    }

    public class TextractService: ITextractService
    {
        private readonly IConfiguration _config;
        private readonly ISqsService _sqsService;

        public TextractService(IConfiguration config, ISqsService sqsService)
        {
            _config = config;
            _sqsService = sqsService;
        }
        
        // TODO: AnalyzeDocumentAsync only handles single-page jpg or pngs.
        // To analyze multi-page PDFs, use StartDocumentAnalysis to trigger the Textract job
        // poll the Amazon SQS queue to retrieve the completion status published by Amazon Textract when a text detection request completes.
        // Obtain the result using GetDocumentAnalysisRequest to get a GetDocumentAnalysisResponse, then execute the below code. 
        public async Task<TextractDocument> AnalyzeDocumentAsync(string bucket, string filename)
        {
            var featureTypes = new List<string> { Constants.FeatureTypes.Forms, Constants.FeatureTypes.Tables };
            var textractDoc = new TextractDocument();
            
            // Automatically gets AWS creds from default config/credential files on system
            using (var client = new AmazonTextractClient())
            {
                // Trigger a Textract Document Analysis job, completion status gets published to SQS.
                var startAnalysisRequest = BuildStartDocumentAnalysisRequest(bucket, filename);
                var textractJob = await client.StartDocumentAnalysisAsync(startAnalysisRequest);

                // Wait for Textract to finish processing - poll SQS for success status
                // TODO
                var completedJobId = await _sqsService.ProcessTextractJob();

                // Get Textract analysis after job completed
                var getAnalysisRequest = new GetDocumentAnalysisRequest
                {
                    JobId = completedJobId
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
                // https://docs.aws.amazon.com/textract/latest/dg/api-async-roles.html
                NotificationChannel = new NotificationChannel
                {
                    RoleArn = _config["textractIamRoleArn"],
                    SNSTopicArn = _config["snsArn"]
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

        private Dictionary<string,string> GetKeyValuePairs(GetDocumentAnalysisResponse analysis)
        {
            var keyDict = new Dictionary<string, Block>();
            var valueDict = new Dictionary<string, Block>();
            var blockDict = new Dictionary<string, Block>();
            var keyValueSets = analysis.Blocks
                .Where(block => block.BlockType == BlockType.KEY_VALUE_SET);

            PopulateKeyValueDicts(keyDict, valueDict, blockDict, keyValueSets);

            var keyValuePairs = GetKeyValueRelationship(keyDict, valueDict, blockDict);

            return keyValuePairs;
        }

        private void PopulateKeyValueDicts(
            Dictionary<string, Block> keyDict, Dictionary<string, Block> valueDict, Dictionary<string, Block> blockDict, IEnumerable<Block> keyValueSets)
        {
            foreach (var keyValueSet in keyValueSets)
            {
                blockDict.Add(keyValueSet.Id, keyValueSet);

                if (keyValueSet.EntityTypes.Contains(Constants.KeyValueSet.EntityTypes.Key))
                {
                    keyDict.Add(keyValueSet.Id, keyValueSet);
                }
                else
                {
                    valueDict.Add(keyValueSet.Id, keyValueSet);
                }
            }
        }

        private Dictionary<string,string> GetKeyValueRelationship(
            Dictionary<string, Block> keyDict, Dictionary<string, Block> valueDict, Dictionary<string, Block> blockDict)
        {
            var keyValuePairs = new Dictionary<string, string>();

            foreach (var key in keyDict.Keys)
            {
                var keyBlock = keyDict[key];
                var keyText = GetKeyValueText(keyBlock, blockDict);

                var valueBlock = GetValueBlock(keyBlock, valueDict);
                var valueText = GetKeyValueText(valueBlock, blockDict);

                keyValuePairs[keyText] = valueText;
            }

            return keyValuePairs;
        }

        private string GetKeyValueText(Block keyValueBlock, Dictionary<string, Block> blockDict)
        {
            var keyValueTextBlockIds = keyValueBlock.Relationships
                .Where(rel => rel.Type == Constants.KeyValueSet.RelationshipTypes.Child)
                .FirstOrDefault().Ids;

            string keyValueText = null;
            foreach (var keyValueTextTokenBlockId in keyValueTextBlockIds)
            {
                var keyValueTextTokenBlock = blockDict[keyValueTextTokenBlockId];
                var keyValueTextToken = keyValueTextTokenBlock.Text;
                keyValueText += $"{keyValueTextToken} ";
            }

            return keyValueText.TrimEnd();
        }

        private Block GetValueBlock(Block keyBlock, Dictionary<string, Block> valueDict)
        {
            var valueBlockId = keyBlock.Relationships
                .Where(rel => rel.Type == Constants.KeyValueSet.RelationshipTypes.Value)
                .FirstOrDefault().Ids.FirstOrDefault();

            return valueDict[valueBlockId];
        }
    }
}
