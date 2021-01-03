using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ConsoleApp1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests
{
    [TestClass]
    public class endToEndTests
    {
        [TestInitialize]
        public void initDBParams()
        {
            Program.connstr = databaseTests.connstr;
            Program.dbName = databaseTests.testDBName;
            updateDB.dropDBIfExists(Program.connstr, Program.dbName);
        }

        [TestMethod]
        public void testLoggingOfCab()
        {
            using (updateDB uut = new updateDB(Program.connstr, Program.dbName, true))
            {
                byte[] filehash = new byte[]
                {
                    0x3f, 0x06, 0x30, 0x72, 0x58, 0x2b, 0xd4, 0xae, 0xef, 0x41, 0xae, 0x19, 0xb0, 0x5d, 0xe5, 0x62, 0x43, 0x9c, 0x90, 0xeb
                };
                wsusUpdate sourcefile = new wsusUpdate(
                    "http://download.windowsupdate.com/d/msdownload/update/software/updt/2016/07/windows10.0-kb3161102-x86_3f063072582bd4aeef41ae19b05de562439c90eb.cab",
                    filehash, 8406);
                uut.insert_noconcurrency(sourcefile);

                Program.Main(null);

                List<file> result = uut.getAllFiles();
                List<string> errors = uut.getErrorStrings();
                Assert.AreEqual(0, errors.Count);
                Assert.AreEqual(322, result.Count);
                // TODO: more tests!
            }
        }


        [TestMethod]
        public void testLoggingOfWIM()
        {
            using (updateDB uut = new updateDB(Program.connstr, Program.dbName, true))
            {
                byte[] filehash = new byte[]
                {
                    0xe4, 0x13, 0x81, 0xb5, 0x3b, 0x64, 0x8f, 0x62, 0xc9, 0x1b, 0x13, 0xbd, 0xe5, 0x74, 0xc5, 0x03, 0x31, 0xf2, 0x20, 0x96
                };
                wsusUpdate sourcefile = new wsusUpdate(
                    "http://b1.download.windowsupdate.com/c/upgr/2016/05/10586.0.160426-1409.th2_refresh_clienteducationn_vol_x86fre_lv-lv_793d7991cca6a99000f938b3743712f756af57b2.esd",
                    filehash, 2059062476);
                uut.insert_noconcurrency(sourcefile);

                Program.Main(null);

                List<file> result = uut.getAllFiles();
                List<string> errors = uut.getErrorStrings();
                foreach (string error in errors)
                    Debug.WriteLine("Error: " + error);
//                Assert.AreEqual(0, errors.Count);
                Assert.AreEqual(44199, result.Count);
                // TODO: more tests!
            }
        }
    }
}