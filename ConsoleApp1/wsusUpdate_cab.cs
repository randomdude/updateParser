using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace ConsoleApp1
{
    public class wsusUpdate_cab : updateInfo
    {
        public bool offlineCapable;
        public string description;
        public string productName;
        public string supportInfo;

//        public wsusUpdate parent { get; set; }
        
        public wsusUpdate_cab(wsusUpdate parent, string absoluteLocation) : base()
        {
            this.parent = parent;
        /*    this.offlineCapable = false;
            this.description = "(none)";
            this.productName = "(none)";
            this.supportInfo = "(none)";
            try
            {
                string dismInfo = wsusUpdate.runAndGetStdout("dism", $"/online /get-packageinfo /packagepath:{absoluteLocation}");
                string[] dismInfoLines = dismInfo.Split('\n');
                foreach (string dismInfoLine in dismInfoLines)
                {
                    Regex re = new Regex("^(?<name>.*?) : (?<value>.*)$");
                    Match m = re.Match(dismInfoLine);
                    if (m.Success == false)
                        continue;
                    string val = m.Groups["value"].ToString().Trim().ToLower();
                    string name = m.Groups["name"].ToString().Trim().ToLower();
                    switch (name)
                    {
                        case "completely offline capable":
                            offlineCapable = parseBool(val, name);
                            break;
                        case "description":
                            description = val;
                            break;
                        case "product name":
                            productName = val;
                            break;
                        case "support information":
                            supportInfo = val;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                // oh well
            }*/

        }

        public override IEnumerable<updateFile> GetFiles(string fileAbsPath)
        {
            string outputdir = Path.Combine(Program.tempdir, "expand_" + Path.GetFileName(parent.filename));
            if (Directory.Exists(outputdir))
                deleteWithRetry(outputdir);
            Directory.CreateDirectory(outputdir);

            try
            {
                // redirect to nul since expand has no quiet switch and otherwise conhost will take like 10% cpu
                wsusUpdate.run("cmd", $"/c expand.exe -R \"{fileAbsPath}\" -F:* \"{outputdir}\" >nul");

                var enumerator = enumFilesOnDisk(outputdir, (abspath, relpath) => 
                    new updateFile(abspath, relpath));
                foreach (var a in enumerator)
                    yield return a;
            }
            finally
            {
                deleteWithRetry(outputdir);
            }
        }

        private bool parseBool(string val, string name)
        {
            if (val == "no")
                return false;
            else if (val == "yes")
                return true;
            else
                throw new ArgumentException($"Unknown value '{val}' for property '{name}'");
        }

        public override void writeToDB(asyncsqlthread db, string absoluteFilename)
        {
            int n = 0;
            using (bulkDBInsert_file bulkxfer = new bulkDBInsert_file(parent, db))
            {
                foreach (updateFile updateFile in GetFiles(absoluteFilename))
                {
                    bulkxfer.add(new file(parent, updateFile));
                    n++;
                }
            }
            Debug.WriteLine($"found {n} files");
        }
    }
}
