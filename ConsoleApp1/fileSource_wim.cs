using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace ConsoleApp1
{
    public class fileSource_wim : thingInDB
    {
        public int? wimFileID;
        public int wimImageIndex;
        public string wimImageName;
        public Int64 wimImageSize;
        public string wimImageDescription;

        public override Dictionary<string, object> columnNames
        {
            get
            {
                return new Dictionary<string, object>()
                {
                    {"wimFileID", wimFileID},
                    {"wimImageIndex", wimImageIndex},
                    {"wimImageName", wimImageName},
                    {"wimImageSize", wimImageSize},
                    {"wimImageDescription", wimImageDescription}
                };
            }
        }

        public fileSource_wim(string imagename, int imageIndex, string description, long sizeBytes)
        {
            wimImageName = imagename;
            wimImageIndex = imageIndex;
            wimImageSize = sizeBytes;
            wimImageDescription = description;
        }

        public fileSource_wim(wimImage image)
        {
            wimImageName = image.name;
            wimImageIndex = image.index;
            wimImageSize = (long)image.sizeBytes;
            wimImageDescription = image.description;
        }

        public fileSource_wim(SqlDataReader reader) : base(reader)
        {
            
        }
    }
}