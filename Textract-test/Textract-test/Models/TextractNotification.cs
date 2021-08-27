namespace Textract_test.Models
{
    public class TextractSnsMessage
    {
        public string Type { get; set; }
        public string MessageId { get; set; }
        public string TopicArn { get; set; }
        public string Message { get; set; }
        public string Timestamp { get; set; }
        public string SignatureVersion { get; set; }
        public string Signature { get; set; }
        public string SigningCertURL { get; set; }
        public string UnsubscribeURL { get; set; }
    }
    
    public class TextractNotification
    {
        public string JobId { get; set; }
        public string Status { get; set; }
        public string API { get; set; }
        public string JobTag { get; set; }
        public long Timestamp { get; set; }
        public Location DocumentLocation { get;set; }
    }

    public class Location
    {
        public string S3ObjectName { get; set; }
        public string S3Bucket { get; set; }
    }
}
