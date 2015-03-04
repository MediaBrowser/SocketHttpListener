using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
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
            CreateListener(pfxLocation);

            await TestListenAndConnect("https");
        }

        [TestMethod]
        public async Task TestHttpListenAndConnect()
        {
            CreateListener(pfxLocation);

            await TestListenAndConnect("http");
        }

        [TestMethod]
        public async Task TestHttpListenAndConnectMissingCert()
        {            
            CreateListener(@"C:\d.pfx");

            await TestListenAndConnect("http");
        }

        [TestMethod]
        public async Task TestHttpListenAndConnectNoPrivateKeyCert()
        {
            string certWithoutKey = Path.GetTempFileName();

            try
            {
                RemovePrivateKeyAndWrite(pfxLocation, certWithoutKey);

                CreateListener(certWithoutKey);

                await TestListenAndConnect("http");
            }
            finally
            {
                File.Delete(certWithoutKey);    
            }            
        }

        [TestMethod]
        public async Task TestHttpListenAndConnectCorruptedCert()
        {

            string corruptedCert = Path.GetTempFileName();

            try
            {
                File.WriteAllBytes(corruptedCert, new byte[] { 0x01, 0x02 });

                CreateListener(corruptedCert);

                await TestListenAndConnect("http");
            }
            finally
            {
                File.Delete(corruptedCert);
            }
        }

        private void RemovePrivateKeyAndWrite(string sourceCertFile, string destCertFile)
        {
            X509Certificate2 sourceCert = new X509Certificate2(sourceCertFile);

            sourceCert.PrivateKey = null;

            File.WriteAllBytes(destCertFile, sourceCert.Export(X509ContentType.Pfx, (string)null));
        }

        private void CreateListener(string pfxLocationLocal)
        {
            this.listener = new HttpListener(this.logger.Object, pfxLocationLocal);
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
