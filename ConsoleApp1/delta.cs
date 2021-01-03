using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace ConsoleApp1
{
    public class delta : thingInDB
    {
        public int? sourceFileID;
        public long sourceFileSize;
        public int? outputFileID;
        public long outputFileSize;
        public int deltaFileID;
        public long deltaFileSize;

        public delta(SqlDataReader reader) : base(reader)
        {

        }

        public delta(file delta)
        {
            deltaFileID = delta.dbID.Value;
            deltaFileSize = delta.size;
        }

        public override Dictionary<string, object> columnNames
        {
            get
            {
                return new Dictionary<string, object>()
                {
                    {"sourceFileID", sourceFileID},
                    {"sourceFileSize", sourceFileSize},
                    {"outputFileID", outputFileID},
                    {"outputFileSize", outputFileSize},
                    {"deltaFileID", deltaFileID},
                    {"deltaFileSize", deltaFileSize}
                };
            }
        }

    }
}