using Amazon.Runtime;
using Amazon.Textract;
using Amazon.Textract.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Textract_test.Common;

namespace Textract_test.Services
{
    public interface ITextractService
    {
        Task<AnalyzeDocumentResponse> AnalyzeDocumentAsync(string bucket, string filename);
    }

    public class TextractService: ITextractService
    {
        public async Task<AnalyzeDocumentResponse> AnalyzeDocumentAsync(string bucket, string filename)
        {
            var featureTypes = new List<string> { Constants.FeatureTypes.Forms, Constants.FeatureTypes.Tables };
            
            // Automatically gets AWS creds from default config/credential files on system
            using (var client = new AmazonTextractClient())
            {
                var request = new AnalyzeDocumentRequest
                {
                    Document = new Document
                    {
                        S3Object = new S3Object
                        {
                            Bucket = bucket,
                            Name = filename
                        }
                    },
                    FeatureTypes = featureTypes
                };

                var results = await client.AnalyzeDocumentAsync(request);

                // lines and words
                var lines = results.Blocks
                    .Select(block => block.BlockType == BlockType.LINE)
                    .ToList();
                var words = results.Blocks
                    .Select(block => block.BlockType == BlockType.WORD)
                    .ToList();

                // Key-value pairs
                var keyDict = new Dictionary<string, Block>();
                var valueDict = new Dictionary<string, Block>();
                var blockDict = new Dictionary<string, Block>();
                var keyValueSets = results.Blocks
                    .Where(block => block.BlockType == BlockType.KEY_VALUE_SET);

                PopulateKeyValueDicts(keyDict, valueDict, blockDict, keyValueSets);

                var keyValuePairs = GetKeyValueRelationship(keyDict, valueDict, blockDict);
                
                // Tables
                // TODO: Handle tables
            }

            throw new NotImplementedException("Not complete");
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
