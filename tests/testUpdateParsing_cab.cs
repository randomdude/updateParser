using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ConsoleApp1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests
{
    [TestClass]
    public class testUpdateParsing_cab
    {
        [TestMethod]
        public void testIndexing()
        {
            string testfilename = "..\\..\\..\\..\\testdata\\foo.cab";
            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_cab uut = new wsusUpdate_cab(parent, testfilename);

            List<updateFile> files = uut.GetFiles(testfilename).ToList();

            Assert.AreEqual(7, files.Count);
            Assert.IsTrue(files.Exists(x => x.filename == "qcgnss.cat"));
            Assert.IsTrue(files.Exists(x => x.filename == "qcgnss.dll"));
            Assert.IsTrue(files.Exists(x => x.filename == "qcgnss.inf"));
            Assert.IsTrue(files.Exists(x => x.filename == "qcqmux.sys"));
            Assert.IsTrue(files.Exists(x => x.filename == "qcqmuxusb.sys"));
            Assert.IsTrue(files.Exists(x => x.filename == "qmuxmdm.cat"));
            Assert.IsTrue(files.Exists(x => x.filename == "QmuxMdm.inf"));
        }

        [TestMethod]
        public void testNondismUpdate()
        {
            string testfilename = "..\\..\\..\\..\\testdata\\windows10.0-kb3161102-x86_ece5b9848dd46edfa823ca8e189f1298f36c4110.cab";
            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_cab uut = new wsusUpdate_cab(parent, testfilename);

            List<updateFile> files = uut.GetFiles(testfilename).ToList();
            Assert.AreEqual(322, files.Count);
        }

        [TestMethod]
        public void testTempFilesAreRemoved()
        {
            string[] preexistingFiles = Directory.GetFileSystemEntries(ConsoleApp1.Program.tempdir);

            string testfilename = "..\\..\\..\\..\\testdata\\windows10.0-kb3161102-x86_ece5b9848dd46edfa823ca8e189f1298f36c4110.cab";

            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_cab uut = new wsusUpdate_cab(parent, testfilename);

            uut.GetFiles(testfilename);

            string[] filesAfterOperation = Directory.GetFileSystemEntries(ConsoleApp1.Program.tempdir);

            string[] newfiles = filesAfterOperation.Where(x => preexistingFiles.Contains(x) == false).ToArray();
            Debug.WriteLine(newfiles);

            Assert.AreEqual(0, newfiles.Length);
        }

        [TestMethod]
        public void foo()
        {
            string testfilename = "..\\..\\..\\..\\testdata\\windows10.0-kb3161102-x86_3f063072582bd4aeef41ae19b05de562439c90eb.cab";
            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_cab uut = new wsusUpdate_cab(parent, testfilename);

            List<updateFile> files = uut.GetFiles(testfilename).ToList();
//            files = files.GroupBy(x => x.hash_sha256_string).Select(x => x.First()).ToList();
            Assert.AreEqual(322, files.Count);
        }
    }
}
