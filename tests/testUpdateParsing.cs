using Microsoft.VisualStudio.TestTools.UnitTesting;
using ConsoleApp1;

namespace tests
{
    [TestClass]
    public class testUpdateParsing
    {
        [TestMethod]
        [Ignore]
        public void testGnuFile_cab()
        {
            string sig = wsusUpdate.doGnuFileCheck("C:\\code\\updateparser\\testdata\\foo.cab");

            Assert.AreEqual("Microsoft Cabinet archive data", sig);
        }
    }
}