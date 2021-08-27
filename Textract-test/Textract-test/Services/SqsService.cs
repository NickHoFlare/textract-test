using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Textract_test.Common;
using Textract_test.Models;

namespace Textract_test.Services
{
    public interface ISqsService
    {
        Task<string> ProcessTextractJob(string jobId); 
    }

    public class SqsService : ISqsService
    {
        private readonly AwsSettingsOptions _awsSettings;

        public SqsService(IOptions<AwsSettingsOptions> awsSettings)
        {
            _awsSettings = awsSettings.Value;
        }
        
        public async Task<string> ProcessTextractJob(string jobId)
        {
            bool jobFound = false;

            using (var client = new AmazonSQSClient())
            {
                var sqsUrlResponse = await client.GetQueueUrlAsync(_awsSettings.SqsQueueName);
                var sqsUrl = sqsUrlResponse.QueueUrl;

                while (!jobFound)
                {
                    var messageResponse = await client.ReceiveMessageAsync(sqsUrl);
                    var messages = messageResponse.Messages;

                    if (messages != null && messages.Count > 0)
                    {
                        foreach (var messageJson in messages)
                        {
                            var message = JsonSerializer.Deserialize<TextractSnsMessage>(messageJson.Body);
                            var notification = JsonSerializer.Deserialize<TextractNotification>(message.Message);

                            if (notification.JobId == jobId)
                            {
                                jobFound = true;

                                if (notification.Status == Constants.TextractJobSuccess)
                                {
                                    return jobId;
                                }
                                else
                                {
                                    throw new Exception("Failed to analyse document.");
                                }
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(5000);
                    }
                }
            }

            return null;
        }
    }
}
