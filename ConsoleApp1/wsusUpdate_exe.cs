using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Configuration;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConsoleApp1
{
    public class wsusUpdate_exe : updateInfo
    {
        [DllImport("mspatcha.dll", SetLastError = true)]
        static extern bool ApplyPatchToFile(string PatchFileName, string OldFileName, string NewFileName, UInt32 ApplyOptionFlags);

//        public wsusUpdate parent { get; set; }

        public wsusUpdate_exe(wsusUpdate parent) : base()
        {
            this.parent = parent;
        }

        public override IEnumerable<updateFile> GetFiles(string absoluteFilename)
        {
            string mountpoint = Path.Combine(Program.tempdir, "mount_" + Path.GetFileName(parent.filename));
            if (Directory.Exists(mountpoint))
                deleteWithRetry(mountpoint);
            Directory.CreateDirectory(mountpoint);

            try
            {
                // Do the extraction.
                wsusUpdate.runAndGetStdout("7z.exe", $"x {absoluteFilename} -y -bd -o{mountpoint}/");

                // Postprocess any delta updates.
                string sfxManifestFilename = Path.Combine(mountpoint, "_sfx_manifest_");
                if (File.Exists(sfxManifestFilename))
                {
                    sfxManifest manifest = new sfxManifest(sfxManifestFilename);
                    foreach (fileDelta delta in manifest.deltas)
                    {
                        string fulldest = Path.Combine(mountpoint, delta.dest);

                        // TODO: make output dir recursively
                        if (!Directory.Exists(Path.GetDirectoryName(fulldest)))
                            Directory.CreateDirectory(Path.GetDirectoryName(fulldest));

                        bool s = ApplyPatchToFile(
                            Path.Combine(mountpoint, delta.patch),
                            Path.Combine(mountpoint, delta.source),
                            fulldest,
                            0);

                        if (s == false)
                            throw new Win32Exception();
                    }
                }

                var enumerator = enumFilesOnDisk(mountpoint, (abspath, relpath) => new updateFile( abspath, relpath));
                foreach (var a in enumerator)
                    yield return a;
            }
            finally
            {
                deleteWithRetry(mountpoint);
            }
        }

        public override void writeToDB(asyncsqlthread db, string absoluteFilename)
        {
            using (bulkDBInsert_file bulkxfer = new bulkDBInsert_file(parent, db))
            {
                foreach (updateFile updateFile in GetFiles(absoluteFilename))
                    bulkxfer.add(new file(parent, updateFile));
                bulkxfer.finish();
            }
        }
    }
}
