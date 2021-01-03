using System;
using System.Collections.Generic;
using System.Threading;
using ConsoleApp1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace tests
{
    [TestClass]
    public class testAsyncSqlThread
    {
        [TestMethod]
        public void testFailureInjection()
        {
            List<string> messages = new List<string>();

            asyncsqlthread uut = new asyncsqlthread(x => messages.Add(x));
            uut.start();

            Thread.Sleep(TimeSpan.FromSeconds(1));
            uut.injectFatalFailure();
            Thread.Sleep(TimeSpan.FromSeconds(5));

            // The injected error should have been fatal to the thread.
            Assert.IsFalse(uut.isAlive());

            // We should see exactly one failure message.
            Assert.AreEqual(1, messages.Count);

            uut.stop();
        }
    }
}