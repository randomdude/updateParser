using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.UpdateServices.Administration;

namespace ConsoleApp1
{
    public class wsusUpdate : thingInDB
    {
        /// <summary>
        /// The filename on the local system where we downloaded the update to.
        /// </summary>
        public string filename;

        /// <summary>
        /// The hash of the file, as reported by WSUS
        /// </summary>
        public Byte[] fileHashFromWSUS;

        /// <summary>
        /// Where the update can be downloaded from
        /// </summary>
        public string downloadURI;

        /// <summary>
        /// The size of this update file
        /// </summary>
        public Int64 sizeBytes;

        public DateTime startTime;
        public DateTime endTime;
        public TimeSpan sqltime;

        private const string gnuFileUtilPath = "C:\\tools\\cygwin\\bin\\file.exe";

        public virtual IEnumerable<updateFile> GetFiles()
        {
            throw new NotImplementedException();
        }

        public static updateInfo parse(wsusUpdate src, string absoluteLocation)
        {
            /*
            // Do an initial pass using the unix 'file' tool.
            string filesig = doGnuFileCheck(filename);

            switch (filesig)
            {
                case "Microsoft Cabinet archive data":
                    return new wsusUpdate_cab(filename, deleteOnDestruction);
//                    return new wsusUpdate_exe(filename, deleteOnDestruction);
                case "Windows imaging (WIM) image":
                    return new wsusUpdate_wim(filename, deleteOnDestruction);
            }

            if (filesig.StartsWith("PE32"))
                return new wsusUpdate_exe(filename, deleteOnDestruction);
                */

            string ext = Path.GetExtension(src.downloadURI.ToString()).ToLower();
            switch (ext)
            {
                case ".cab":
                    return new wsusUpdate_cab(new wsusUpdate(src), absoluteLocation);
                case ".exe":
                    return new wsusUpdate_exe(new wsusUpdate(src));
                case ".esd":
                case ".wim":
                    return new wsusUpdate_wim(new wsusUpdate(src));
                default:
                    throw new ArgumentException($"unknown file extension '{ext}'");
            }

        }

        public UInt32 sizeMB
        {
            get
            {
                if (sizeBytes < 1024 * 1024)
                    return 0;
                return (uint)(sizeBytes / 1024 / 1024);
            }
        }

        public override Dictionary<string, Object> columnNames
        {
            get
            {
                return new Dictionary<string, Object>
                {
                    {"filename", filename},
                    {"fileHashFromWSUS", fileHashFromWSUS},
                    {"downloadURI", downloadURI},
                    {"sizeBytes", sizeBytes}

                };
            }
        }


        public wsusUpdate(wsusUpdate src)
        {
            this.fileHashFromWSUS = src.fileHashFromWSUS;
            this.downloadURI = src.downloadURI;
            this.sizeBytes = src.sizeBytes;
            this.filename = getTemporaryFilename();
        }

        public wsusUpdate(UpdateFile src)
        {
            this.fileHashFromWSUS = src.Hash;
            this.downloadURI = src.OriginUri.ToString();
            this.sizeBytes = src.TotalBytes;
            this.filename = getTemporaryFilename();
        }

        public wsusUpdate(SqlDataReader res) : base(res)
        {

        }

        /// <summary>
        /// Just for testing!
        /// </summary>
        /// <param name="upstreamURL"></param>
        /// <param name="src"></param>
        public wsusUpdate(string upstreamURL, Byte[] hash, int fileSize)
        {
            this.downloadURI = upstreamURL;
            this.fileHashFromWSUS = hash;
            this.sizeBytes = fileSize;
            this.filename = wsusUpdate.getTemporaryFilename(hash, Path.GetExtension(this.downloadURI));
        }

        /// <summary>
        /// Just for testing!
        /// </summary>
        /// <param name="upstreamURL"></param>
        /// <param name="src"></param>
        public wsusUpdate(string upstreamURL, string src)
        {
            this.downloadURI = upstreamURL;
            using (SHA256 hasher = SHA256.Create())
            {
                using (FileStream stream = File.OpenRead(src))
                {
                    this.fileHashFromWSUS = hasher.ComputeHash(stream);
                }
            }

            this.sizeBytes = new FileInfo(src).Length;
            this.filename = src;
        }

        public static string getFilenameForUpdate(UpdateFile toDownload)
        {
            return getTemporaryFilename(toDownload.Hash, Path.GetExtension(toDownload.OriginUri.ToString()));
        }

        public string getTemporaryFilename()
        {
            return getTemporaryFilename(fileHashFromWSUS, Path.GetExtension(downloadURI));
        }

        public static string getTemporaryFilename(string hash, string extension)
        {
            return $"update_{hash.ToString()}{extension}";
        }

        public static string getTemporaryFilename(Byte[] fileHash, string extension)
        {
            StringBuilder hashString = new StringBuilder();

            foreach (byte b in fileHash)
                hashString.Append(b.ToString("X2"));

            return getTemporaryFilename(hashString.ToString(), extension);
        }

        public static void run(string cmd, string args)
        {
            int retries = 10;
            while (true)
            {
                ProcessStartInfo psi = new ProcessStartInfo(cmd, args);
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
//                psi.RedirectStandardError = true;
//                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                using (Process gnuFile = Process.Start(psi))
                {
                    gnuFile.WaitForExit();
                    if (gnuFile.ExitCode == 0)
                    {
                        if (retries != 10)
                        {
                            Debug.WriteLine("retried OK");
                        }
                        break;
                    }

                    retries--;
                    if (retries == 0)
                    {
                        throw new Exception($"command '{cmd}' with args '{args}' failed"); // : stdout is '{gnuFile.StandardOutput.ReadToEnd()}' stderr is '{gnuFile.StandardError.ReadToEnd()}'");
                    }
                    Thread.Sleep(TimeSpan.FromSeconds(3));
                }
            }
        }

        public static string runAndGetStdout(string cmd, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(cmd, args);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            using (Process gnuFile = Process.Start(psi))
            {
                gnuFile.WaitForExit();
                string stdout = gnuFile.StandardOutput.ReadToEnd().Trim();

                if (gnuFile.ExitCode != 0)
                    throw new Exception("command '" + cmd + "' with args '" + args + "' failed: " + stdout);

                return stdout;
            }
        }

        public static string doGnuFileCheck(string filename)
        {
            string toRet = runAndGetStdout(gnuFileUtilPath, " --brief " + filename);

            // GNU file's output for cabinet files will unclude some information we don't want.
            // Trim out this data if this is a cabinet file.
            if (toRet.StartsWith("Microsoft Cabinet archive data"))
                toRet = "Microsoft Cabinet archive data";

            return toRet;
        }
        
    }
}


