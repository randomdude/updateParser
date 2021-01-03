using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Reflection;

namespace ConsoleApp1
{
    public abstract class thingInDB
    {
        public int? dbID;
        public abstract Dictionary<string, object> columnNames { get; }

        public thingInDB()
        {
        }

        // TODO: codegen this at runtime, this much reflection will be slowww
        public thingInDB(SqlDataReader res)
        {
            foreach (string keyName in columnNames.Keys)
            {
                FieldInfo field = this.GetType().GetField(keyName,
                    BindingFlags.FlattenHierarchy | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic);

                int ord = res.GetOrdinal(keyName);
                if (ord == -1 || field == null)
                    throw new Exception($"Field {keyName} not found in type {this.GetType().ToString()}");

                if (res.IsDBNull(ord))
                    field.SetValue(this, null);
                else
                    field.SetValue(this, res[ord]);
            }
            this.dbID = (int) res[res.GetOrdinal("id")];
        }

    }
}