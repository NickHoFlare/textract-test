using Amazon.SQS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Textract_test.Services
{
    public interface ISqsService
    {
        Task<string> ProcessTextractJob(); 
    }

    public class SqsService : ISqsService
    {
        public async Task<string> ProcessTextractJob()
        {
            bool jobFound = false;

            using (var client = new AmazonSQSClient())
            {
                var sqsUrlResponse = await client.GetQueueUrlAsync("textract-sqs");
                var sqsUrl = sqsUrlResponse.QueueUrl;

                while (!jobFound)
                {
                    var messageResponse = await client.ReceiveMessageAsync(sqsUrl);
                    var messages = messageResponse.Messages;

                    if (messages != null && messages.Count > 0)
                    {
                        // TODO
                    }
                    else
                    {
                        await Task.Delay(2000);
                    }
                }
            }

            return "TODO!";
        }
    }
}
