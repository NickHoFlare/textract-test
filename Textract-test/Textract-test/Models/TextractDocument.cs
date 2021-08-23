using System.Collections.Generic;

namespace Textract_test.Models
{
    public class TextractDocument
    {
        public List<string> Lines { get; set; }
        public List<string> Words { get; set; }
        public Dictionary<string, string> KeyValuePairs { get; set; }
    }
}
