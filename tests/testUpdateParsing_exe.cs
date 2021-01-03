using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ConsoleApp1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests
{
    [TestClass]
    public class testUpdateParsing_exe
    {
        [TestMethod]
        public void testIndexing_sfxDeltas()
        {
            string testfilename = "..\\..\\..\\..\\testdata\\foo.exe";
            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_exe uut = new wsusUpdate_exe(parent);

            List<updateFile> files = uut.GetFiles(testfilename).ToList();

            // We should get 18 'real' files plus 15 we recreated from deltas.
            Assert.AreEqual(18 + 15, files.Count);

            Assert.AreEqual(1, files.Count(x => x.locationAndFilename == "SP3QFE\\sprv041f.dll"));
            Assert.AreEqual(1, files.Count(x => x.locationAndFilename == "update\\update.exe"));
            Assert.AreEqual("fab244a421d424a0efecf682e019b40df2ba9e072595d6a0b4e193a92c07a220",
                files.Single(x => x.locationAndFilename == "update\\update.exe").hash_sha256_string);
        }

        [TestMethod]
        public void testIndexing_ARM()
        {
            string testfilename = "..\\..\\..\\..\\testdata\\am_slim_delta_patch_1.315.1036.0_040a6602eba4bfa65550a94396aa2fb1187b0814.exe";
            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_exe uut = new wsusUpdate_exe(parent);

            List<updateFile> files = uut.GetFiles(testfilename).ToList();
            foreach (var updateFile in files)
            {
                Debug.WriteLine(updateFile.filename);
            }
        }


        [TestMethod]
        public void testTempFilesAreRemoved()
        {
            string[] preexistingFiles = Directory.GetFileSystemEntries(ConsoleApp1.Program.tempdir);

            string testfilename = "..\\..\\..\\..\\testdata\\am_slim_delta_patch_1.315.1036.0_040a6602eba4bfa65550a94396aa2fb1187b0814.exe";

            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_exe uut = new wsusUpdate_exe(parent);

            uut.GetFiles(testfilename);

            string[] filesAfterOperation = Directory.GetFileSystemEntries(ConsoleApp1.Program.tempdir);

            string[] newfiles = filesAfterOperation.Where(x => preexistingFiles.Contains(x) == false).ToArray();
            Debug.WriteLine(newfiles);

            Assert.AreEqual(0, newfiles.Length);
        }
    }
}