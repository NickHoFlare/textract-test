using Amazon.Runtime;
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
        private Dictionary<string, string> _keyValuePairs;
        private List<Block> _tableBlockList;
        private List<Dictionary<string, List<string>>> _tableList;

        public TextractService(IOptions<AwsSettingsOptions> awsSettings, ISqsService sqsService)
        {
            _awsSettings = awsSettings.Value;
            _sqsService = sqsService;
            _keyDict = new Dictionary<string, Block>();
            _valueDict = new Dictionary<string, Block>();
            _blockDict = new Dictionary<string, Block>();
            _keyValuePairs = new Dictionary<string, string>();
            _tableBlockList = new List<Block>();
            _tableList = new List<Dictionary<string, List<string>>>();
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

                var lines = new List<string>();
                var words = new List<string>();

                // Trigger a Textract Document Analysis job, completion status gets published to SQS.
                //var startAnalysisRequest = BuildStartDocumentAnalysisRequest(bucket, filename);
                //var textractJob = await client.StartDocumentAnalysisAsync(startAnalysisRequest);

                // Wait for Textract to finish processing - poll SQS for success status
                var completedJobId = "c1c1774ebaacd42549b176609544678539addba46e69735a503935018538c9df"; // await _sqsService.ProcessTextractJob(textractJob.JobId);

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
                    lines.AddRange(GetDocumentLinesOrWords(completedAnalysis, BlockType.LINE));
                    words.AddRange(GetDocumentLinesOrWords(completedAnalysis, BlockType.WORD));

                    // Digest Blocks
                    DigestBlocks(completedAnalysis.Blocks);

                    // Key-value pairs
                    GetKeyValueRelationship();

                    // Tables
                    GetTables();

                    // Get next page
                    paginationToken = completedAnalysis.NextToken;

                    done = paginationToken == null ? true : false;
                }

                textractDoc.Lines = lines;
                textractDoc.Words = words;
                textractDoc.KeyValuePairs = _keyValuePairs;
                textractDoc.Tables = _tableList;
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

        private void DigestBlocks(IEnumerable<Block> blocks)
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
                else if (block.BlockType == Constants.KeyValueSet.BlockTypes.Table)
                {
                    _tableBlockList.Add(block);
                }
            }
        }

        private void GetKeyValueRelationship()
        {
            foreach (var key in _keyDict.Keys)
            {
                var keyBlock = _keyDict[key];
                var valueBlock = GetValueBlock(keyBlock);

                var keyText = $"page{keyBlock.Page}-{GetKeyValueText(keyBlock)}";
                var valueText = GetKeyValueText(valueBlock);

                if (keyText != null)
                {
                    _keyValuePairs[keyText] = valueText;
                }
            }
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

        private void GetTables()
        { 
            foreach (var tableBlock in _tableBlockList)
            {
                var cellIds = tableBlock.Relationships.FirstOrDefault().Ids;
                // After this runs, we have a matrix of Blocks that are in order and representative of the table rows and columns
                var cellBlocks = InitializeTable(cellIds);
                var tableDict = GetTableFromBlocks(cellBlocks);
                _tableList.Add(tableDict);
            }
        }

        // This feels really space inefficient
        private List<List<Block>> InitializeTable(List<string> cellIds)
        {
            // cellBlocks keeps track of where each cell belongs to in the table, in a 2D list.
            var cellBlocks = new Dictionary<int, List<Block>>();

            foreach (var id in cellIds)
            {
                List<Block> columnList = null;
                var cellBlock = _blockDict[id];

                // Find the column that the cell belongs to, and slot it there, so that the columns are in order.
                if (cellBlocks.TryGetValue(cellBlock.ColumnIndex - 1, out var value))
                {
                    columnList = cellBlocks[cellBlock.ColumnIndex - 1];
                }
                else
                {
                    columnList = new List<Block>();
                    cellBlocks[cellBlock.ColumnIndex - 1] = columnList;
                }

                columnList.Add(cellBlock);
            }

            // Now that we have slotted all cells in their correct columns, we have to sort them by row.
            for (var i = 0; i < cellBlocks.Count; i++)
            {
                var column = cellBlocks[i];
                cellBlocks[i] = column.OrderBy(cell => cell.RowIndex).ToList();
            }

            var list = cellBlocks.Values.ToList();
            return list;
        }

        private Dictionary<string, List<string>> GetTableFromBlocks(List<List<Block>> cellBlocks)
        {
            var table = new Dictionary<string, List<string>>();

            foreach (var column in cellBlocks)
            {
                string header = null;
                for (var i = 0; i < column.Count; i++)
                {
                    var cell = column[i];
                    var cellText = GetCellText(cell);
                    if (i == 0)
                    {
                        header = cellText ?? "?Empty?";
                        table.Add(header, new List<string>());
                    } 
                    else
                    {
                        table[header].Add(cellText);
                    }
                }
            }

            return table;
        }

        private string GetCellText(Block cell)
        {
            var wordIds = cell.Relationships?.FirstOrDefault()?.Ids;
            string cellText = null;
            
            if (wordIds != null)
            {
                foreach (var id in wordIds)
                {
                    var word = _blockDict[id].Text;
                    cellText += $"{word} ";
                }
            }

            return cellText;
        }
    }
}
