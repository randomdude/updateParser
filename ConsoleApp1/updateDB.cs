using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ConsoleApp1.Properties;
using Microsoft.UpdateServices.Administration;

namespace ConsoleApp1
{
    public class updateDB : IDisposable
    {
        private SqlConnection _dbConn;

        public updateDB(string connStr, string dbname, bool offsetIdentityColumns = false)
        {
            _dbConn = new SqlConnection(connStr);
            _dbConn.Open();

            try
            {
                if (!DBExists(_dbConn, dbname))
                {
                    createDB(dbname, offsetIdentityColumns);
                }

                selectDB(dbname);
            }
            catch (Exception)
            {
                _dbConn.Dispose();
                throw;
            }
        }

        private void createDB(string dbName, bool offsetIdentityColumns)
        {
            // Fixme - the sql file should be a resource, compiled into the assembly
            string[] creationSQL = Resources.dbCreation.Split('\n');

            // If we have been instructed to set identity columns to start and increment by some value, apply that.
            // This is used during testing, to help find cases where we are using an id from one table with an unrelated table.
            if (offsetIdentityColumns)
            {
                int idStart = 100;
                int idInc = 7;
                for (int n = 0; n < creationSQL.Length; n++)
                {
                    // TODO: this is whitespace sensitive, so "identity(1, 1)" won't be picked up for now.
                    if (creationSQL[n].Contains("IDENTITY(1,1)"))
                    {
                        creationSQL[n] = creationSQL[n].Replace($"IDENTITY(1,1)", $"IDENTITY({idStart},{idInc})");
                        idStart = idStart * 3;
                        idInc = idInc + (idStart / 100);
                    }
                }
            }

            // Split the script by 'GO' commands, so we can run each chunk individually.
            List<string> creationSQLStatements = new List<string>();
            string thisChunk = "";
            foreach (string sqlLine in creationSQL)
            {
                if (sqlLine.Trim().ToLower() == "GO".ToLower())
                {
                    creationSQLStatements.Add(thisChunk);
                    thisChunk = "";
                }
                else
                {
                    thisChunk += "\n" + sqlLine;
                }
            }

            creationSQLStatements.Add(thisChunk);

            // Now execute each chunk.
            foreach (string sqlChunk in creationSQLStatements)
            {
                using (SqlCommand cmd = _dbConn.CreateCommand())
                {
                    string toExec = sqlChunk.Replace("__DBNAME__", dbName);
                    cmd.CommandText = toExec;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void selectDB(string dbname)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = $"use [{dbname}]";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Like cmd.Parameters.AddWithValue, but will add a DBNull.Value if appropriate.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cmd"></param>
        /// <param name="varName"></param>
        /// <param name="val"></param>
        private void addParamWithValue<T>(SqlCommand cmd, string varName, T val)
        {
            if (val == null)
            {
                // :^)
                // fixme/todo/wtf/etc
                if (varName == "@pe_sizeOfCode" || varName == "@pe_timestamp" || varName == "@pe_magicType" || 
                    varName == "@hash_sha256" || varName == "@contents128b" || varName == "@parentfileshash")
                {
                    cmd.Parameters.AddWithValue(varName, DBNull.Value).DbType = DbType.Binary;
                }
                else
                {
                    cmd.Parameters.AddWithValue(varName, DBNull.Value).DbType = DbType.String;
                }
            }
            else
            {
                cmd.Parameters.AddWithValue(varName, val);
            }

        }

        public void Dispose()
        {
            _dbConn.Dispose();
        }

        public static void dropDBIfExists(string connStr, string dbName)
        {
            using (var dbConn = new SqlConnection(connStr))
            {
                dbConn.Open();

                if (!DBExists(dbConn, dbName))
                    return;

                string[] commandTexts = new[]
                {
                    $"EXEC msdb.dbo.sp_delete_database_backuphistory @database_name = {dbName}",
                    $"ALTER DATABASE [{dbName}] SET  SINGLE_USER WITH ROLLBACK IMMEDIATE",
                    $"drop database [{dbName}]"
                };

                foreach (string commandText in commandTexts)
                {
                    using (SqlCommand cmd = dbConn.CreateCommand())
                    {
                        cmd.CommandText = commandText;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static bool DBExists(SqlConnection conn, string dbName)
        {
            using (SqlCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"select DB_ID('{dbName}')";
                Object toRet = cmd.ExecuteScalar();
                return toRet != DBNull.Value;
            }
        }

        public void insertOrCreateCabFileNoUpdate(wsusUpdate parent, wsusUpdate_cab metadata)
        {
            if (parent.dbID == null)
                insertOrCreateWsusFileNoUpdate(parent);
            insert_noconcurrency(new [] { new fileSource_cab(parent, metadata) }, "filesource_cab" );
        }

        public void insertOrCreateWsusFile(wsusUpdate toInsert)
        {
            insertOrCreate(toInsert, "wsusFile", "filename");
        }

        public void insertOrCreateWsusFileNoUpdate(wsusUpdate toInsert)
        {
            if (toInsert.dbID.HasValue)
                return;
            insertOrCreateNoUpdate(toInsert, "wsusFile", new []{"filename"} );
        }

        public void insert_noconcurrency(wsusUpdate toInsert)
        {
            insert_noconcurrency(new [] { toInsert }, "wsusFile");
        }

            public void insert_noconcurrency(wsusUpdate[] toInsert)
        {
            insert_noconcurrency(toInsert, "wsusFile");
        }
        
        public void bulkInsertFiles(wsusUpdate grandparent, file_wimInfo[] toInsertList)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                string initialsql = $"Declare @testdata as FileTableType; ";
                int thisbatchcnt = 0;

                void flushBatch()
                {
                    if (thisbatchcnt == 0)
                        return;

                    addParamWithValue(cmd, $"@parentFileHash", grandparent.fileHashFromWSUS);
                    addParamWithValue(cmd, $"@wimImageIndex", toInsertList[0].parent.wimImageIndex);
                    addParamWithValue(cmd, $"@wimImageSize", toInsertList[0].parent.wimImageSize);
                    addParamWithValue(cmd, $"@wimImageName", toInsertList[0].parent.wimImageName);
                    addParamWithValue(cmd, $"@wimImageDescription", toInsertList[0].parent.wimImageDescription);
                    cmd.CommandText +=
                        " exec insertfiles_wim @parentFileHash, @wimImageIndex, @wimImageSize, @wimImageName, @wimImageDescription, @testdata; ";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = initialsql;
                    thisbatchcnt = 0;
                    cmd.Parameters.Clear();
                }

                cmd.CommandText = initialsql;

                foreach (file_wimInfo toInsert in toInsertList)
                {
                    addFileToBatchCommand(toInsert.fileInfo, thisbatchcnt, cmd);
                    thisbatchcnt++;
                    if (thisbatchcnt == 80)
                        flushBatch();
                }
                flushBatch();
            }
        }

        public void bulkInsertFiles(wsusUpdate grandparent, IEnumerable<file> toInsertList)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                string initialsql = $"Declare @testdata as FileTableType; ";
                cmd.CommandText = initialsql;

                int thisbatchcnt = 0;

                void flushBatch()
                {
                    addParamWithValue(cmd, $"@parentFileHash", grandparent.fileHashFromWSUS);
                    cmd.CommandText += " exec insertfiles @parentFileHash,@testdata; ";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = $"Declare @testdata as FileTableType; ";
                    thisbatchcnt = 0;
                    cmd.Parameters.Clear();
                }

                foreach (file toInsert in toInsertList)
                {
                    addFileToBatchCommand(toInsert, thisbatchcnt, cmd);

                    thisbatchcnt++;
                    if (thisbatchcnt == 80)
                        flushBatch();
                }

                flushBatch();
            }
        }

        private void addFileToBatchCommand(file toInsert, int thisbatchcnt, SqlCommand cmd)
        {
            Dictionary<string, object>.KeyCollection columnNameList = toInsert.columnNames.Keys;
            StringBuilder columnNames = new StringBuilder();
            StringBuilder columnValues = new StringBuilder();
            string[] binarycols = {"hash_sha256", "pe_timestamp", "pe_sizeOfCode", "pe_magicType", "contents128b"};
            foreach (string colName in columnNameList)
            {
                if (colName == "wsusFileID")
                    continue;

                columnNames.Append(colName + ",");
                if (binarycols.Count(x => x == colName) > 0)
                {
                    Byte[] asbyte = toInsert.columnNames[colName] as byte[];
                    if (asbyte != null)
                    {
                        columnValues.Append($"CONVERT(varbinary({asbyte.Length}), @{colName}_{thisbatchcnt}, 1),");
                    }
                    else
                    {
                        columnValues.Append($"CONVERT(varbinary(max), @{colName}_{thisbatchcnt}, 1),");
                    }
                }
                else
                {
                    columnValues.Append($"@{colName}_{thisbatchcnt},");
                }
            }

            cmd.CommandText +=
                $" Insert Into @testdata  ({columnNames.ToString().TrimEnd(',')}) values ({columnValues.ToString().TrimEnd(',')}); ";

            foreach (KeyValuePair<string, object> col in toInsert.columnNames)
            {
                addParamWithValue(cmd, $"@{col.Key}_{thisbatchcnt}", col.Value);
            }
        }

        private void insertOrCreate(thingInDB toInsert, string tableName, string searchFields)
        {
            insertOrCreate(toInsert, tableName, new[] { searchFields });
        }

        private void insertOrCreate(thingInDB toInsert, string tableName, string[] searchFields)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                Dictionary<string, object>.KeyCollection columnNameList = toInsert.columnNames.Keys;
                string columnNames = string.Join(",", columnNameList);
                string columnValues = string.Join(",", columnNameList.Select(x => "@" + x));

                cmd.CommandText =
                    $"DECLARE @insertedID int " +
                    $" MERGE {tableName} WITH (SERIALIZABLE) AS T " +
                    $" USING(VALUES({columnValues})) AS U({columnNames}) " +
                    $" ON (" +
                    String.Join(" AND ", searchFields.Select(x => $"T.{x} = U.{x}")) +
                    $") WHEN MATCHED THEN " +
                    $"      UPDATE SET " +
                    String.Join(", ", columnNameList.Select(x => $"T.{x} = U.{x}")) +
                    $"      ,@insertedID = T.id" +
                    $" WHEN NOT MATCHED THEN " +
                    $" INSERT({columnNames}) " +
                    $" VALUES({columnValues});";

                cmd.CommandText += "; SELECT CAST(ISNULL(@insertedID, SCOPE_IDENTITY()) AS INT)";
                //Debug.WriteLine(cmd.CommandText);

                foreach (KeyValuePair<string, object> col in toInsert.columnNames)
                {
                    addParamWithValue(cmd, "@" + col.Key, col.Value);
                    //Debug.WriteLine(col.Key + " is " + col.Value);
                }

                toInsert.dbID = (int)cmd.ExecuteScalar();
            }
        }

        public void insertOrCreateNoUpdate(thingInDB toInsert, string tableName, string[] searchFields)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                Dictionary<string, object>.KeyCollection columnNameList = toInsert.columnNames.Keys;
                string columnNames = string.Join(",", columnNameList);
                string columnValues = string.Join(",", columnNameList.Select(x => "@" + x));

                cmd.CommandText =
                    $"DECLARE @insertedID int " +
                    $" MERGE {tableName} WITH (SERIALIZABLE) AS T " +
                    $" USING(VALUES({columnValues})) AS U({columnNames}) " +
                    $" ON (" +
                    String.Join(" AND ", searchFields.Select(x => $"T.{x} = U.{x}")) +
                    $") WHEN MATCHED THEN " +
                    $"      UPDATE SET " +
                   // String.Join(", ", columnNameList.Select(x => $"T.{x} = U.{x}")) +
                    $"      @insertedID = T.id" +
                    $" WHEN NOT MATCHED THEN " +
                    $"      INSERT({columnNames}) " +
                    $"      VALUES({columnValues});";

                cmd.CommandText += "; SELECT CAST(ISNULL(@insertedID, SCOPE_IDENTITY()) AS INT)";
                //Debug.WriteLine(cmd.CommandText);

                foreach (KeyValuePair<string, object> col in toInsert.columnNames)
                {
                    addParamWithValue(cmd, "@" + col.Key, col.Value);
                    //Debug.WriteLine(col.Key + " is " + col.Value);
                }

                toInsert.dbID = (int)cmd.ExecuteScalar();
            }
        }

        public void insert_noconcurrency(IEnumerable<thingInDB> toInsert, string tableName)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                uint n = 0;
                foreach (var element in toInsert)
                {
                    Dictionary<string, object>.KeyCollection columnNameList = element.columnNames.Keys;
                    string columnNames = string.Join(",", columnNameList);
                    cmd.CommandText = $"insert into {tableName} ({columnNames}) values ";

                    string columnValues = string.Join(",", columnNameList.Select(x => "@" + x + "_" + n));
                    cmd.CommandText += $"({columnValues}),";

                    foreach (KeyValuePair<string, object> col in element.columnNames)
                    {
                        addParamWithValue(cmd, "@" + col.Key + "_" + n, col.Value);
                        //Debug.WriteLine(col.Key + " is " + col.Value);
                    }

                    n++;
                }

                cmd.CommandText = cmd.CommandText.TrimEnd(',');

                cmd.ExecuteNonQuery();
            }
        }

        public void logError(UpdateFile src, Exception e)
        {
            //insertOrCreateWsusFileNoUpdate(src);

            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText =
                    "Insert into errors (srcurl, exceptiontype, exceptionstring) values (@srcurl, @exceptiontype, @exceptionstring)";

                if (src != null)
                    addParamWithValue(cmd, "@srcurl", src.OriginUri.ToString());
                else
                    addParamWithValue(cmd, "@srcurl", DBNull.Value);

                addParamWithValue(cmd, "@exceptiontype", e.GetType().ToString());
                addParamWithValue(cmd, "@exceptionstring", e.ToString());

                cmd.ExecuteNonQuery();
            }
        }

        public void logError(wsusUpdate src, Exception e)
        {
            insertOrCreateWsusFileNoUpdate(src);

            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText =
                    "Insert into errors (srcurl, exceptiontype, exceptionstring) values (@srcurl, @exceptiontype, @exceptionstring)";

                if (src != null)
                    addParamWithValue(cmd, "@srcurl", src.downloadURI.ToString());
                else
                    addParamWithValue(cmd, "@srcurl", DBNull.Value);

                addParamWithValue(cmd, "@exceptiontype", e.GetType().ToString());
                addParamWithValue(cmd, "@exceptionstring", e.ToString());

                cmd.ExecuteNonQuery();
            }
        }

        public void logError(Exception e)
        {
            //insertOrCreateWsusFileNoUpdate(src);

            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText =
                    "Insert into errors (exceptiontype, exceptionstring) values (@exceptiontype, @exceptionstring)";

                addParamWithValue(cmd, "@exceptiontype", e.GetType().ToString());
                addParamWithValue(cmd, "@exceptionstring", e.ToString());

                cmd.ExecuteNonQuery();
            }
        }

        public void logError(string msg)
        {
            //insertOrCreateWsusFileNoUpdate(src);

            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText =
                    "Insert into errors (exceptiontype, exceptionstring) values ('warning', @exceptionstring)";

                addParamWithValue(cmd, "@exceptionstring", getMachineHostname() + ": " + msg);

                cmd.ExecuteNonQuery();
            }
        }

        public bool containsWSUSFile(Byte[] hash, string URL)
        {
            string filename = wsusUpdate.getTemporaryFilename(hash, Path.GetExtension(URL));

            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText =
                    "select count(*) from wsusFile where filename = @filename";

                addParamWithValue(cmd, "@filename", filename);
                Object res = cmd.ExecuteScalar();
                if ((int)res == 0)
                    return false;
                return true;
            }
        }

        public wsusUpdate getWSUSFileByFilename(string filename)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText =
                    "select * from wsusFile where filename = @filename";

                addParamWithValue(cmd, "@filename", filename);
                using (SqlDataReader res = cmd.ExecuteReader())
                {
                    if (res.Read() == false)
                        throw new Exception($"Couldn't find file {filename}");
                    return new wsusUpdate(res);
                }
            }
        }

        public wsusUpdate getWSUSFileByFileHash(byte[] hashBytes)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = "select * from wsusFile where fileHashFromWSUS = @hashBytes";

                addParamWithValue(cmd, "@hashBytes", hashBytes);
                using (SqlDataReader res = cmd.ExecuteReader())
                {
                    if (res.Read() == false)
                        throw new Exception($"Couldn't find file by hash");
                    return new wsusUpdate(res);
                }
            }
        }

        public wsusUpdate getWSUSFileByID(int ID)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText =
                    "select * from wsusFile where id = @id";

                addParamWithValue(cmd, "@id", ID);
                using (SqlDataReader res = cmd.ExecuteReader())
                {
                    if (res.Read() == false)
                        throw new Exception($"Couldn't find wsus file with id {ID}");

                    wsusUpdate toRet = new wsusUpdate(res);

                    toRet.dbID = ID;
                    return toRet;
                }
            }
        }

        public wsusUpdate startNextUpdate()
        {
            int toRetID;
            using (SqlTransaction trans = _dbConn.BeginTransaction(IsolationLevel.Serializable))
            {
                using (SqlCommand cmd = _dbConn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = "select 1 from wsusFile with (holdlock, tablockx)";
                    cmd.ExecuteNonQuery();
                }

                using (SqlCommand cmd = _dbConn.CreateCommand())
                {
                    string orderstanza = "order by sizeBytes ";
                    if (Program.biggestfirst)
                        orderstanza += "desc";
                    else
                        orderstanza += "asc";

                    cmd.Transaction = trans;
                    cmd.CommandText = @"update E
                    set status = @hostname
                    output INSERTED.id
                        FROM
                        (
                            select top 1 id, status
                            from wsusFile
                            where status = 'QUEUED'
                            " + orderstanza + @"
                        ) E
                    ";
                    addParamWithValue(cmd, "hostname", getMachineHostname());
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            toRetID = (int) reader[0];
                        else
                            toRetID = int.MinValue;
                    }
                }

                trans.Commit();
            }

            if (toRetID == int.MinValue)
                return null;

            Debug.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId} started update {toRetID}");
            wsusUpdate toRet = getWSUSFileByID(toRetID);
            return toRet;
        }

        public void completeWsusUpdate(long parentID, DateTime starttime, DateTime endtime, TimeSpan sqltime, bool succeeded)
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = "insert into wsusfilestats (hostname, wsusfileid, starttime, endtime) values (@hostname, @parentid, @starttime, @endtime)";
                addParamWithValue(cmd, "@hostname", getMachineHostname());
                addParamWithValue(cmd, "@parentid", parentID);
                addParamWithValue(cmd, "@starttime", starttime);
                addParamWithValue(cmd, "@endtime", endtime);
              //  addParamWithValue(cmd, "@sqltime", sqltime);
                cmd.ExecuteNonQuery();
            }

            Debug.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId} finishing update {parentID}");
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                string status = succeeded ? "DONE" : "FAILED";
                cmd.CommandText = $"update wsusfile set status = '{status}' where id = {parentID}";
                cmd.ExecuteNonQuery();
            }
        }

        public void removeDuplicateWsusFiles()
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = @"delete  tbl
                                     from    (
                                            select  row_number() over (partition by fileHashFromWSUS 
                                                                      order by id desc) as rn
                                            ,       *
                                            from    wsusFile
                                            ) tbl
                                    where   rn > 1";
                cmd.ExecuteNonQuery();
            }
        }

        public IEnumerable<wsusUpdate> getClaimedUpdate()
        {
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = @"select * from wsusFile where status = @hostname";
                addParamWithValue(cmd, "hostname", getMachineHostname());
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    List<wsusUpdate> toRet = new List<wsusUpdate>();
                    while (reader.Read())
                        toRet.Add(new wsusUpdate(reader));
                    return toRet;
                }
            }
        }

        private string machineName = null;
        public string getMachineHostname()
        {
            if (machineName != null)
                return machineName;
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("172.31.29.68", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                machineName= endPoint.Address.ToString();
                return machineName;
            }
        }

        public List<file> getAllFiles()
        {
            List<file> toRet = new List<file>();
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = @"select * from files";
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        toRet.Add(new file(reader));
                }
            }

            return toRet;
        }

        public List<file_wimInfo> getWimInfos()
        {
            // Get all the filesource_wims (memory heavy I know, but we only call this in testing so w/e)
            Dictionary<int, fileSource_wim> filesource_wims = new Dictionary<int, fileSource_wim>();
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = @"select * from filesource_wim";
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        filesource_wims.Add((int) reader[reader.GetOrdinal("id")], new fileSource_wim(reader));
                }
            }
            // and a list of files
            Dictionary<int, file> files = new Dictionary<int, file>();
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = @"select * from files";
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        files.Add((int)reader[reader.GetOrdinal("id")], new file(reader));
                }
            }

            // And create for each.
            List<file_wimInfo> toRet = new List<file_wimInfo>();
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = @"select * from files_wim";
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int files_wim_id = (int)reader[reader.GetOrdinal("id")];
                        int fileID = (int)reader[reader.GetOrdinal("fileID")];
                        int sourceID = (int)reader[reader.GetOrdinal("sourceID_wim")];

                        file_wimInfo newItem = new file_wimInfo(filesource_wims[sourceID], files[fileID]);
                        newItem.dbID = files_wim_id;

                        toRet.Add(newItem);
                    }

                    return toRet;
                }
            }
        }

        public List<string> getErrorStrings()
        {
            List<string> toRet = new List<string>();
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandText = @"select exceptionstring from errors";
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string exceptionstring = (string) reader[reader.GetOrdinal("exceptionstring")];
                        Debug.WriteLine($"Error logged in DB: {exceptionstring}");
                        toRet.Add(exceptionstring);
                    }

                    return toRet;
                }
            }
        }

        public List<file> getDeltasFilesByPartialName(string filter)
        {
            List<file> toRet = new List<file>();
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandTimeout = 600;
                cmd.CommandText = $@"select * from files where filename like @filter and pe_timestamp is null";
                //cmd.CommandText += " and id in (6344568, 8396013, 9002503, 8997300, 8997298, 8802055, 8802046, 8769977, 8769975, 8396005, 7784218, 7784212, 6344512, 6344507, 6343710, 6343699, 6312392, 6312380, 10063047, 10063046, 9933049, 9933048, 9724018, 9724017, 9002506, 10075608, 10075607, 9934777, 9934776, 9730974, 9730973, 10411186, 10411185, 7021093, 7021090, 10391355, 10391354, 8184508, 8184503, 7930372, 7930367, 7780721, 7780718, 8067470, 8067466, 8056866, 9097037, 9097034, 8681086, 8681085, 8365615, 8365611, 8364121, 8364112, 8364109, 8364108, 8056860, 7646739, 7646736, 6824130, 6824087, 6604710, 6604707, 6589837, 6589830, 6576511, 6576499, 6180559, 6180554, 6168349, 6168337, 6150609, 6150591, 10125255, 10125254, 10019176, 10019175, 10014931, 10014929, 9099005, 9099004, 4689408, 4689401, 5815355, 7961964, 7497557, 7497553, 6647098, 6647091, 6506711, 6506706, 5854409, 5815352, 8749336, 8554703, 7961959, 5854403, 10050607, 10050606, 9909295, 9909294, 9907140, 9907139, 9847189, 9847188, 9711190, 9711189, 9707242, 9707241, 9040261, 9040259, 8749333, 8554697, 8545910, 8545907, 8455390, 8455387, 8447865, 8447861, 8216990, 8216982, 8199899, 8199890, 8198652, 8198634, 8135859, 8135853, 8008440, 8008436, 7823754, 7823749, 7814193, 7814189, 7712862, 7712854, 7711866, 7711861, 7698604, 7698600, 7698581, 7698574, 7587326, 7587324, 7566648, 7566645, 7496818, 7496816, 7487294, 7487290, 7386478, 7386474, 7374418, 7374413, 7077668, 7077664, 6736855, 6736847, 6646199, 6646192, 6506491, 6506482, 6406614, 6406604, 6404921, 6404916, 6290887, 6290877, 6032688, 6032679, 6032355, 6032349, 5854071, 5854067, 5814323, 5814315, 10332911, 10332910, 10229275, 10229274, 10221002, 10221001, 10085944, 10085943, 5730051, 5730046, 2817773, 2817772, 7522208, 7522206, 6202794, 10259966, 10259965, 9988605, 9988604, 9810433, 9810432, 9802921, 9802920, 9797879, 9797878, 8882441, 8882440, 8503636, 8503632, 8502367, 8502363, 8499946, 8499944, 7989157, 7989151, 7973505, 7973501, 7618022, 7618020, 7611025, 7611019, 7594223, 7594219, 9793919, 7593723, 7593717, 7324939, 7324931, 6453402, 6453394, 9793920, 6202787, 10296547, 10289439, 10114238, 9964411, 9881443, 9874436, 9751902, 9745552, 10296546, 10289438, 10114237, 9964410, 9881442, 9874435, 9751901, 9745551, 9109894, 9109893, 8672136, 8672132, 8669395, 8669391, 8667818, 8667812, 8657043, 8657039, 8481230, 8481226, 8464799, 8464795, 8258730, 8258725, 8092754, 8092748, 7854025, 7854018, 7536423, 7536416, 7522160, 7522159, 7264375, 7264370, 6973002, 6972997, 6888815, 6888809, 6477803, 6477797, 6211320, 6211316, 6202671, 6202669, 4999664, 4999655, 10240129, 10240128, 10141784, 10141783, 9952767, 9952766, 9947718, 9947717, 9778359, 9778358, 9771961, 9771960, 9769264, 9769263, 8310139, 6617213, 6617206, 5981482, 5981477, 8310133, 10367683, 10367682, 10206303, 10206301, 10165811, 10165810, 9886549, 9886548, 8926273, 8926268, 8924392, 8924390, 8274707, 8274703, 8162872, 8162868, 7765050, 7765043, 7455809, 7455804, 6804038, 6804027, 6686661, 6686656, 5956750, 5956745, 8613900, 8613895, 3910646, 3910641, 9817645, 9817644, 7742661, 7219135, 10039279, 10029165, 9824053, 7220117, 7220110, 10039278, 10029164, 9824051, 9814014, 9814013, 7923350, 7923346, 7746971, 7746967, 7744119, 7744112, 7742656, 7219130, 6094739, 6094731, 10425968, 10425967, 7217787, 7217781)";
                addParamWithValue(cmd, "filter", filter);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        file newfile = new file(reader);
                        toRet.Add(newfile);
                    }

                    return toRet;
                }
            }
        }

        public List<file> getFilesByPartialName(string filter)
        {
            List<file> toRet = new List<file>();
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                cmd.CommandTimeout = 600;
                cmd.CommandText = $@"select * from files where filename like @filter";
                addParamWithValue(cmd, "@filter", filter);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        file newfile = new file(reader);
                        toRet.Add(newfile);
                    }

                    return toRet;
                }
            }
        }

        public List<delta> getDeltasByFileIDs(int[] fileIDs)
        {
            if (fileIDs.Length == 0)
                return new List<delta>();

            List<delta> toRet = new List<delta>();
            using (SqlCommand cmd = _dbConn.CreateCommand())
            {
                int fileIDIndex = 0;
                StringBuilder sql = new StringBuilder($@"select * from deltas where deltaFileID in (");
                foreach (int fileID in fileIDs)
                {
                    sql.Append($"@id_{fileIDIndex},");
                    addParamWithValue(cmd, $"@id_{fileIDIndex}", fileID);
                    fileIDIndex++;
                }

                cmd.CommandText = sql.ToString().TrimEnd(',') + ")";
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        delta newfile = new delta(reader);
                        toRet.Add(newfile);
                    }

                    return toRet;
                }
            }
        }
    }
}