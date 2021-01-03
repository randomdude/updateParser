using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ConsoleApp1;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using updatequery;

namespace tests
{
    [TestClass]
    public class deltaTests
    {
        [TestMethod]
        public void testCanGetPatchInfo()
        {
            string patchFile = "..\\..\\..\\..\\testdata\\rev.exe";
            byte[] headerbytes = File.ReadAllBytes(patchFile);
            interop.DELTA_HEADER_INFO res = updatequery.Program.findHeaderForDelta(headerbytes);
            Assert.AreEqual((UInt64)9697080, res.TargetSize);
        }
    }

    [TestClass]
    public class miscTests
    {
        [TestMethod]
        public void testHashing()
        {
            string testfile = "..\\..\\..\\..\\testdata\\test.txt";

            string[] stdout = wsusUpdate.runAndGetStdout("certutil", $"-hashfile \"{testfile}\" sha256").Split('\n');
            stdout = stdout.Where(x => x.StartsWith("SHA256 hash of") == false && x.StartsWith("CertUtil") == false && x.Length > 0).ToArray();
            string hash_sha256_string = stdout.SingleOrDefault().Trim();
            byte[] hash_sha256 = new byte[hash_sha256_string.Length / 2];
            for (int i = 0; i < hash_sha256.Length; ++i)
                hash_sha256[i] = (byte)((GetHexVal(hash_sha256_string[i << 1]) << 4) + (GetHexVal(hash_sha256_string[(i << 1) + 1])));


            using (SHA256 hasher = SHA256.Create())
            {
                using (FileStream stream = File.OpenRead(testfile))
                {
                    byte[] hashOnDisk = hasher.ComputeHash(stream);
                    Assert.IsTrue(hashOnDisk.SequenceEqual(hash_sha256));
                }
            }
        }

        // see https://stackoverflow.com/questions/321370/how-can-i-convert-a-hex-string-to-a-byte-array
        public static int GetHexVal(char hex)
        {
            int val = (int)hex;
            //For uppercase A-F letters:
            //return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }
    }

    [TestClass]
    public class databaseTests
    {
        public const string connstr = "Server=localhost;Integrated Security=true";
        public const string testDBName = "updateDB_tests";

        [TestInitialize]
        public void dropOldDBs()
        {
            updateDB.dropDBIfExists(connstr, testDBName);
        }

        [TestMethod]
        public void canCreateDB()
        {
            using (updateDB uut = new updateDB(connstr, testDBName, true))
            {

            }
        }

        [TestMethod]
        public void canStoreAndRetrieveWsusUpdates()
        {
            using (updateDB uut = new updateDB(connstr, testDBName, true))
            {
                wsusUpdate testSourcefile = new wsusUpdate("http://foo.cab", new byte[] { 0x11, 0x22, 0x33 }, 112233);
                testSourcefile.dbID = 0xacab;

                uut.insertOrCreateWsusFile(testSourcefile);
                Assert.AreNotEqual(0xacab, testSourcefile);
                int? origID = testSourcefile.dbID;

                uut.insertOrCreateWsusFile(testSourcefile);
                Assert.AreNotEqual(0xacab, testSourcefile);
                Assert.AreEqual(origID, testSourcefile.dbID);
            }

        }

        [TestMethod]
        public void canStoreAndRetrieveWIMEntries()
        {
            using (updateDB uut = new updateDB(connstr, testDBName, true))
            {
                // Make files in two WIM images, coming from a single wsusupdat (ie, wim file).
                wsusUpdate testSourcefile = new wsusUpdate("http://foo.cab", new byte[] { 0xaa}, 1);
                uut.insert_noconcurrency(testSourcefile);
                testSourcefile = uut.getWSUSFileByFileHash(testSourcefile.fileHashFromWSUS);

                file_wimInfo fileA = new file_wimInfo("fileA", new byte[] { 0x01 }, new byte[] { 0xaa },
                    new fileSource_wim("test", 2, "descA", 10), "locA");
                // These two files are both in the third imageindex.
                file_wimInfo fileB = new file_wimInfo("fileB", new byte[] { 0x02 }, new byte[] { 0xbb },
                    new fileSource_wim("foo", 3, "descB", 10), "locA");
                file_wimInfo fileC = new file_wimInfo("fileC", new byte[] { 0x03 }, new byte[] { 0xcc },
                    new fileSource_wim("foo", 3, "descB", 10), "locA");

                uut.bulkInsertFiles(testSourcefile, new file_wimInfo[] { fileA });
                uut.bulkInsertFiles(testSourcefile, new file_wimInfo[] { fileB, fileC });

                List<file_wimInfo> inDB = uut.getWimInfos();

                Assert.AreEqual(3, inDB.Count);
                Assert.IsTrue(inDB.All(x => x.dbID.HasValue));
                Assert.IsTrue(inDB.All(x => x.fileInfo.wsusFileID.HasValue));
                Assert.IsTrue(inDB.All(x => x.fileInfo.wsusFileID.Value == testSourcefile.dbID));
                file_wimInfo fromDB_A = inDB.FirstOrDefault(x => x.fileInfo.filename == "fileA");
                file_wimInfo fromDB_B = inDB.FirstOrDefault(x => x.fileInfo.filename == "fileB");
                file_wimInfo fromDB_C = inDB.FirstOrDefault(x => x.fileInfo.filename == "fileC");
                Assert.IsNotNull(fromDB_A);
                Assert.IsNotNull(fromDB_B);
                Assert.IsNotNull(fromDB_C);
                Assert.AreEqual("descA", fromDB_A.parent.wimImageDescription);
                Assert.AreEqual("descB", fromDB_B.parent.wimImageDescription);
                Assert.AreEqual("descB", fromDB_C.parent.wimImageDescription);
            }
        }

        [TestMethod]
        public void canStoreAndRetrieveFilesInWIM()
        {
            using (updateDB uut = new updateDB(connstr, testDBName, true))
            {
                wsusUpdate sourcefile = new wsusUpdate("http://foo.cab", new byte[]{ 0x11, 0x22, 0x33 }, 123);
                uut.insert_noconcurrency(sourcefile);
                sourcefile = uut.getWSUSFileByFileHash(new byte[] {0x11, 0x22, 0x33});

                fileSource_wim imageEntry = new fileSource_wim("test", 2, "abc", 10);

                file_wimInfo testFileA = new file_wimInfo("test file A", new byte[] { 0x11, 0x22, 0x33 }, new byte[] { 0xaa, 0xbb }, imageEntry, "loc1");
                file_wimInfo testFileB = new file_wimInfo("test file B", new byte[] { 0x44, 0x55, 0x66 }, new byte[] { 0xcc, 0xdd }, imageEntry, "loc1");

                uut.bulkInsertFiles(sourcefile, new file_wimInfo[] { testFileA, testFileB });

                // After insertion, the files should be correct
                List<file_wimInfo> A = uut.getWimInfos();
                Assert.AreEqual(2, A.Count);
                Assert.AreEqual(imageEntry.wimImageName, A[0].parent.wimImageName);
                Assert.AreEqual(imageEntry.wimImageName, A[1].parent.wimImageName);
                Assert.AreEqual("test file A", A[0].fileInfo.filename);
                Assert.AreEqual("test file B", A[1].fileInfo.filename);
                Assert.AreEqual(sourcefile.dbID.Value, A[0].fileInfo.wsusFileID);
                Assert.AreEqual(sourcefile.dbID.Value, A[1].fileInfo.wsusFileID);

            }
        }

        [TestMethod]
        public void canDedupeFilesInWIM()
        {
            using (updateDB uut = new updateDB(connstr, testDBName, true))
            {
                wsusUpdate sourcefile = new wsusUpdate("http://foo.cab", new byte[] { 0x11, 0x22, 0x33 }, 123);
                uut.insert_noconcurrency(sourcefile);
                sourcefile = uut.getWSUSFileByFileHash(new byte[] { 0x11, 0x22, 0x33 });

                fileSource_wim imageEntry = new fileSource_wim("test", 2, "abc", 10);

                file_wimInfo testFileA = new file_wimInfo("test file A", new byte[] { 0x11, 0x22, 0x33 }, new byte[] { 0xaa, 0xbb }, imageEntry, "loc1");
                file_wimInfo testFileB = new file_wimInfo("test file B", new byte[] { 0x44, 0x55, 0x66 }, new byte[] { 0xcc, 0xdd }, imageEntry, "loc1");

                uut.bulkInsertFiles(sourcefile, new file_wimInfo[] { testFileA });
                uut.bulkInsertFiles(sourcefile, new file_wimInfo[] { testFileB });

                // After a second insertion, no new rows should have been added.
                List<file_wimInfo> A = uut.getWimInfos();
                uut.bulkInsertFiles(sourcefile, new file_wimInfo[] { testFileA });
                uut.bulkInsertFiles(sourcefile, new file_wimInfo[] { testFileB });
                List<file_wimInfo> B = uut.getWimInfos();
                Assert.AreEqual(A.Count, B.Count);
            }
        }
    }
}