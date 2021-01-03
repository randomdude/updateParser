using System.Diagnostics;
using System.Linq;
using ConsoleApp1;
using Microsoft.UpdateServices.Administration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests
{
    [TestClass]
    public class testUpdateParsing_wim
    {
        [TestMethod]
        public void testImageListing()
        {
            string testfilename = "..\\..\\..\\..\\testdata\\19041.84.200218-1143.vb_release_svc_refresh_clientconsumer_ret_x86fre_uk-ua_0535b069954cdcf41534af7bc8f0166b21c73469.esd";
            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_wim uut = new wsusUpdate_wim(parent);

            wimImage[] images = uut.getImages(testfilename);

            Assert.AreEqual(7, images.Length);
            Assert.AreEqual("Windows Setup Media", images[0].name);
            Assert.AreEqual("Windows Setup Media", images[0].description);
            Assert.AreEqual((ulong)223561359, images[0].sizeBytes);
        }

        [TestMethod]
        public void testGettingFilesForImage()
        {
            string testfilename = "..\\..\\..\\..\\testdata\\19041.84.200218-1143.vb_release_svc_refresh_clientconsumer_ret_x86fre_uk-ua_0535b069954cdcf41534af7bc8f0166b21c73469.esd";
            wsusUpdate parent = new wsusUpdate("idk", testfilename);
            wsusUpdate_wim uut = new wsusUpdate_wim(parent);

            var files = uut.getFilesForImage(testfilename,  1).ToArray();


            Assert.AreEqual(898, files.Length);
            Assert.AreEqual("autorun.inf", files[0].locationAndFilename);
            Assert.AreEqual("1b6a5b7444395bb1adaddca43adad2b800278099fbfe2c176d916df923f68d81", files[0].hash_sha256_string);
            Assert.AreEqual("boot\\memtest.exe", files[9].locationAndFilename);
        }
    }
}