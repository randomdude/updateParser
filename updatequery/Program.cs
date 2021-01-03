using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using ConsoleApp1;

namespace updatequery
{
    public class Program
    {
        class opts
        {
            [Option('d', "database", HelpText = "Name of database to use", Default = "updateinfo_copy")]
            public string dbname { get; set; }

            [Option('c', "connectionstring", HelpText = "Connection string used to connect to the database",
                Default = "Server=localhost;Integrated Security=true")]
            public string connstr { get; set; }

//            [Option('l', "list", HelpText = "Show information for matched files", Default = false)]
//            public bool op_list { get; set; }
//
//            [Option('f', "filename", HelpText = "File to find information about", Default = null)]
//            public string filename { get; set; }
        }

        static void Main(string[] args)
        {
            ParserResult<opts> f = Parser.Default.ParseArguments<opts>(args);
            f.WithParsed(o => Main(o));
        }

        private static void Main(opts args)
        {
            using (updateDB db = new updateDB(args.connstr, args.dbname))
            {
                // Make sure all delta files are represented in the deltafiles table, even if they are empty.
                List<file> deltaFiles = db.getDeltasFilesByPartialName("%ntoskrnl.exe");
                foreach (file deltaFile in deltaFiles)
                {
                    delta newdelta = new delta(deltaFile);
                    Byte[] deltaBytes = new byte[deltaFile.contents128b.Length - 4];
                    Array.Copy(deltaFile.contents128b, 4, deltaBytes, 0, deltaBytes.Length);
                    interop.DELTA_HEADER_INFO headerInfo = findHeaderForDelta(deltaBytes);
                    newdelta.outputFileSize = (long) headerInfo.TargetSize;
                    newdelta.deltaFileID = deltaFile.dbID.Value;

                    db.insertOrCreateNoUpdate(newdelta, "deltas", new []{ "sourcefileID" });
                }

                while (true)
                {
                    // Next, go through the delta table, filling in anything we already have rows for.
                    List<file> allFiles = db.getFilesByPartialName("%ntoskrnl.exe");
                    List<delta> deltas = db.getDeltasByFileIDs(deltaFiles.Select(x => x.dbID.Value).ToArray());
                    foreach (delta delta in deltas)
                    {
                        if (delta.sourceFileID == null)
                            applyDelta(db, allFiles, delta);
                    }
                }
            }
        }

        public static interop.DELTA_HEADER_INFO findHeaderForDelta(byte[] headerBytes)
        {
            interop.DELTA_INPUT input = new interop.DELTA_INPUT();
            input.Editable = true;
            input.uSize = 126;
            input.lpStart = Marshal.AllocHGlobal(headerBytes.Length);
            IntPtr headerInfo = IntPtr.Zero;
            
            try
            {
                headerInfo = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(interop.DELTA_HEADER_INFO)));
                Marshal.Copy(headerBytes, 0, input.lpStart, headerBytes.Length);
                UInt32 s = interop.GetDeltaInfoB(input, headerInfo);
                if (s == 0)
                    throw new Win32Exception();

                interop.DELTA_HEADER_INFO headerInfoParsed = new interop.DELTA_HEADER_INFO();
                Marshal.PtrToStructure<interop.DELTA_HEADER_INFO>(headerInfo, headerInfoParsed);

                return headerInfoParsed;
            }
            finally
            {
                if (headerInfo != IntPtr.Zero)
                    Marshal.FreeHGlobal(headerInfo);
                Marshal.FreeHGlobal(input.lpStart);
            }
        }

        private static void applyDelta(updateDB db, List<file> allFiles, delta toApply)
        {
            List<file> candidates = allFiles.Where(x => x.size == toApply.sourceFileSize).ToList();
            foreach (file candidate in candidates)
            {
                // todo
            }
        }
    }
}
