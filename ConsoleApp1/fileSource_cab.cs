using System.Collections.Generic;

namespace ConsoleApp1
{
    public class fileSource_cab : thingInDB
    {
        public int? wsusFileID;
        public bool offlineCapable;
        public string description;
        public string productName;
        public string supportInfo;

        public override Dictionary<string, object> columnNames
        {
            get
            {
                return new Dictionary<string, object>()
                {
                    {"wsusFileID", wsusFileID},
                    {"description", description},
                    {"productName", productName},
                    {"supportInfo", supportInfo},
                    {"offlineCapable", offlineCapable}
                };
            }
        }

        public fileSource_cab(wsusUpdate parent, wsusUpdate_cab src)
        {
            this.wsusFileID = parent.dbID.Value;
            this.offlineCapable = src.offlineCapable;
            this.description = src.description;
            this.productName= src.productName;
            this.supportInfo= src.supportInfo;
        }
    }
}