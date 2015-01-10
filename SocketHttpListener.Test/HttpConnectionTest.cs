using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SocketHttpListener.Logging;
using HttpListener = SocketHttpListener.Net.HttpListener;

namespace SocketHttpListener.Test
{
    [TestClass]
    public class HttpConnectionTest
    {
        private const string SITE_URL = "localhost:12345/Testing/";
        private const string TEXT_TO_WRITE = "TESTING12345";
        private readonly byte[] BYTES_TO_WRITE = Encoding.UTF8.GetBytes(TEXT_TO_WRITE);
        
        private static string pfxLocation;

        private Mock<ILogger> logger;
        private HttpListener listener;
        private HttpClient httpClient;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            pfxLocation = Path.GetTempFileName();
            using (var fileStream = File.OpenWrite(pfxLocation))
            {
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("SocketHttpListener.Test.localhost.pfx")
                    .CopyTo(fileStream);
            }
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
            this.logger = null;
            ((IDisposable)this.listener).Dispose();
            this.httpClient.Dispose();

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => false;
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

        public async Task TestListenAndConnect(string prefix)
        {
            string url = string.Format("{0}://{1}", prefix, SITE_URL);
            listener.Prefixes.Add(url);
            listener.Start();
            listener.OnContext = x =>
            {
                Console.WriteLine(x.Request.HttpMethod);
                x.Response.OutputStream.Write(BYTES_TO_WRITE, 0, BYTES_TO_WRITE.Length);
                x.Response.Close();
            };

            string result = await this.httpClient.GetStringAsync(url);
            Assert.AreEqual(TEXT_TO_WRITE, result);
        }
    }
}
