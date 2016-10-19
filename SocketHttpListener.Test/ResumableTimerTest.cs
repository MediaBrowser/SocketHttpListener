using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ResumableTimer = SocketHttpListener.Net.ResumableTimer;

namespace SocketHttpListener.Test
{
    [TestClass]
    public class ResumableTimerTest
    {
        object timeoutData;

        [TestInitialize]
        public void TestInit()
        {
            this.timeoutData = 1;
        }

        [TestCleanup]
        public void TestCleanup()
        {
        }

        [TestMethod]
        public async Task TestGenericTimeout()
        {
            ResumableTimer rt = new ResumableTimer(TimeoutCallback, 2);

            rt.Start(TimeSpan.FromMilliseconds(100));
            Assert.AreEqual(this.timeoutData, 1);
            await Task.Delay(250);
            Assert.AreEqual(this.timeoutData, 2);
        }

        [TestMethod]
        public async Task TestNoTimeoutAndReset()
        {
            ResumableTimer rt = new ResumableTimer(TimeoutCallback, 2);

            rt.Start(TimeSpan.FromHours(500));
            await Task.Delay(250);
            Assert.AreEqual(this.timeoutData, 1);

            rt.Start(TimeSpan.FromMilliseconds(100));
            await Task.Delay(250);
            Assert.AreEqual(this.timeoutData, 2);
        }

        [TestMethod]
        public async Task TestStopAndResume()
        {
            ResumableTimer rt = new ResumableTimer(TimeoutCallback, 2);

            rt.Start(TimeSpan.FromMilliseconds(150));
            await Task.Delay(10);
            Assert.AreEqual(this.timeoutData, 1);

            rt.Stop();
            rt.Resume();
            await Task.Delay(10);
            Assert.AreEqual(this.timeoutData, 1, "How long did stop/resume take?");

            rt.Stop();
            await Task.Delay(250);
            Assert.AreEqual(this.timeoutData, 1, "Ensure that Stop/Resume work without timeout");

            rt.Resume();
            await Task.Delay(10);
            Assert.AreEqual(this.timeoutData, 2, "Ensure that Resume worked");
        }

        void TimeoutCallback(object data)
        {
            timeoutData = data;
        }
    }
}
