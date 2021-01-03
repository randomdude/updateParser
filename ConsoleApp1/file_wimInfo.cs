using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace ConsoleApp1
{
    public class file_wimInfo : thingInDB
    {
        public fileSource_wim parent;
        public file fileInfo;

        public override Dictionary<string, object> columnNames
        {
            get
            {
                return new Dictionary<string, object>()
                {
                    { "sourceID_wim", parent?.dbID },
                    { "fileID", fileInfo?.dbID}, 
                    { "id", this.dbID }
                };
            }
        }

        /// <summary>
        /// This constructor is used for injecting test data.
        /// </summary>
        public file_wimInfo(string filename, Byte[] hash_sha256, byte[] contents, fileSource_wim parent, string location)
        {
            fileInfo = new file(parent.wimFileID, filename, hash_sha256, contents, location);
            this.parent = parent;
        }

        public file_wimInfo(wsusUpdate grandparent, updateFile source, fileSource_wim parent)
        {
            fileInfo = new file(grandparent, source);

            this.parent = parent;
        }

        public file_wimInfo(fileSource_wim parent, file fileInfo)
        {
            this.parent = parent;
            this.fileInfo = fileInfo;
        }

        public file_wimInfo(SqlDataReader reader) : base(reader)
        {
            
        }
    }
}