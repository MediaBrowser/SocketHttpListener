using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Patterns.Logging;
using HttpListener = SocketHttpListener.Net.HttpListener;

namespace SocketHttpListener.Test
{
    [TestClass]
    public class HttpConnectionTest
    {        
        private static readonly byte[] BYTES_TO_WRITE = Encoding.UTF8.GetBytes(Utility.TEXT_TO_WRITE);
        
        private static string pfxLocation;

        private Mock<ILogger> logger;
        private HttpListener listener;
        private HttpClient httpClient;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            pfxLocation = Utility.GetCertificateFilePath();
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            if (File.Exists(pfxLocation))
            {
                File.Delete(pfxLocation);
            }
        }

        [TestInitialize]
        public void TestInit()
        {
            this.logger = LoggerFactory.CreateLogger();
            this.listener = new HttpListener(this.logger.Object, pfxLocation);
            this.httpClient = new HttpClient();

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        }

        [TestCleanup]
        public void TestCleanup()
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => false;

            this.logger = null;
            ((IDisposable)this.listener).Dispose();
            this.httpClient.Dispose();            
        }

        [TestMethod]
        public async Task TestHttpsListenAndConnect()
        {
            await TestListenAndConnect("https");
        }

        [TestMethod]
        public async Task TestHttpListenAndConnect()
        {
            await TestListenAndConnect("http");
        }

        private async Task TestListenAndConnect(string prefix)
        {
            string url = string.Format("{0}://{1}", prefix, Utility.SITE_URL);
            this.listener.Prefixes.Add(url);
            this.listener.Start();
            this.listener.OnContext = async x =>
            {
                await x.Response.OutputStream.WriteAsync(BYTES_TO_WRITE, 0, BYTES_TO_WRITE.Length);
                x.Response.Close();
            };

            string result = await this.httpClient.GetStringAsync(url);
            Assert.AreEqual(Utility.TEXT_TO_WRITE, result);
        }
    }
}
