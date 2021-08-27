namespace Textract_test.Models
{
    public class AwsSettingsOptions
    {
        public const string AwsSettings = "AwsSettings";

        public string S3BucketName { get; set; }
        public string SqsQueueName { get; set; }
        public string SnsArn { get; set; }
        public string TextractIamRoleArn { get; set; }
    }
}
