using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Textract_test.Common
{
    public class Constants
    {
        public class FeatureTypes
        {
            public const string Forms = "FORMS";
            public const string Tables = "TABLES";
        }

        public class KeyValueSet
        {
            public class EntityTypes
            {
                public const string Key = "KEY";
            }

            public class RelationshipTypes
            {
                public const string Child = "CHILD";
                public const string Value = "VALUE";
            }
        }
    }
}
