using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;

namespace ConsoleApp1
{
    public class file : thingInDB
    {
        public int? wsusFileID;
        public readonly string filelocation;
        public readonly string filename;
        public readonly string fileextension;
        public readonly byte[] hash_sha256;
        public long size;
        public byte[] pe_timestamp;
        public byte[] pe_sizeOfCode;
        public byte[] pe_magicType;
        public byte[] contents128b;
        public string rsds_GUID;
        public int rsds_age;
        public string rsds_filename;
        public string authenticode_certfriendly;
        public string authenticode_certsubj;
        public string sourceFilename;
        public string FileDescription;
        public string FileVersion;
        public string ProductName;
        public string ProductVersion;
        public string Comments;
        public string CompanyName;

        public string hash_sha256_string
        {
            get { return String.Join("", hash_sha256.Select(x => x.ToString("x2"))); }
        }

        public override Dictionary<string, object> columnNames
        {
            get
            {
                return new Dictionary<string, object>()
                {
                    {"wsusFileID", this.wsusFileID },
                    {"hash_sha256", this.hash_sha256},
                    {"filename", this.filename},
                    {"filelocation", this.filelocation},
                    {"fileextension", this.fileextension},
                    {"size", this.size},
                    {"pe_timestamp", this.pe_timestamp},
                    {"pe_sizeOfCode", this.pe_sizeOfCode},
                    {"contents128b", this.contents128b},
                    {"pe_magicType", this.pe_magicType},
                    {"FileDescription", this.FileDescription},
                    {"FileVersion", this.FileVersion},
                    {"ProductName", this.ProductName},
                    {"ProductVersion", this.ProductVersion},
                    {"Comments", this.Comments},
                    {"CompanyName", this.CompanyName}
                };
            }
        }

        public file(wsusUpdate parent, updateFile src)
        {
            this.sourceFilename = parent.filename;
            this.filename = src.filename;
            this.filelocation = src.filelocation;
            this.fileextension = src.fileextension;
            this.hash_sha256 = src.hash_sha256;
            this.size = (long)src.size;
            this.pe_sizeOfCode = toNullableBinary(src.pe_sizeOfCode);
            this.pe_timestamp = toNullableBinary(src.pe_timedatestamp);
            this.pe_magicType = toNullableBinary(src.pe_magicType);
            this.contents128b = src.contents128b;

            this.rsds_GUID = src.rsds_GUID;
            this.rsds_age = (int)src.rsds_age;
            this.rsds_filename = src.rsds_filename;
            this.authenticode_certfriendly = src.authenticode_certfriendly;
            this.authenticode_certsubj = src.authenticode_certsubj;
            this.FileDescription = src.FileDescription;
            this.FileVersion = src.FileVersion;
            this.ProductName = src.ProductName;
            this.ProductVersion = src.ProductVersion;
            this.Comments = src.Comments;
            this.CompanyName = src.CompanyName;
        }

        private byte[] toNullableBinary(int? input)
        {
            if (!input.HasValue)
                return null;

            return BitConverter.GetBytes(input.Value);
        }

        private byte[] toNullableBinary(short? input)
        {
            if (!input.HasValue)
                return null;

            return BitConverter.GetBytes(input.Value);
        }

        /// <summary>
        /// This constructor is used for injecting test data.
        /// </summary>
        public file(int? sourceFileID, string filename, byte[] hash_sha256, byte[] contents, string location)
        {
            this.wsusFileID = sourceFileID;
            this.filename = filename;
            this.contents128b = contents;
            this.hash_sha256 = hash_sha256;
            this.filelocation = location;
            this.fileextension = Path.GetExtension(location);
        }

        public file(SqlDataReader reader) : base(reader)
        {
            
        }
    }
}