using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Patterns.Logging;
using SocketHttpListener.Net.WebSockets;
using HttpListener = SocketHttpListener.Net.HttpListener;

namespace SocketHttpListener.Test
{
    [TestClass]
    public class WebsocketTest
    {                
        private static readonly TimeSpan WaitOneTimeout = TimeSpan.FromSeconds(10);        
        
        private static string pfxLocation;

        private Mock<ILogger> logger;
        private HttpListener listener;
        private WebSocket4Net.WebSocket socket;
        private AutoResetEvent serverResetEvent;
        private AutoResetEvent clientResetEvent;
        private WebSocketContext webSocketContextServer;
        private bool areEqual;
        private bool sent;

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
            this.areEqual = false;
            this.sent = false;

            this.logger = LoggerFactory.CreateLogger();
            this.listener = new HttpListener(this.logger.Object, pfxLocation);            
            
            this.serverResetEvent = new AutoResetEvent(false);
            this.clientResetEvent = new AutoResetEvent(false);

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        }

        [TestCleanup]
        public void TestCleanup()
        {
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => false;

            this.serverResetEvent.Dispose();
            this.clientResetEvent.Dispose();

            this.socket.Dispose(); // 0.10 doesn't have this. Comment out to show error.
            ((IDisposable)this.listener).Dispose();
            this.logger = null;
            this.webSocketContextServer = null;
            this.areEqual = false;
            this.sent = false;
        }

        [TestMethod]
        public void LargeWebSocketMessageTest()
        {
            RunWebSocketMessageTest("http", "ws", string.Concat(Enumerable.Range(0, 3000000).Select(x => Utility.TEXT_TO_WRITE)));            
        }

        /// <summary>
        /// This test fails because the largest binary message that can be received correctly by WebSocket4Net is 1016 bytes.
        /// The bug in in is: https://github.com/kerryjiang/WebSocket4Net/blob/master/WebSocket4Net/WebSocketCommandInfo.cs#L123
        /// copied is never incremented causing binary messages over 1016 bytes to overwrite one another. Once this is fixed binary should work.
        /// </summary>
        [TestMethod]
        public void LargeWebSocketDataTest()
        {
            RunWebSocketDataTest("http", "ws", string.Concat(Enumerable.Range(0, 3000000).Select(x => Utility.TEXT_TO_WRITE)));
        }

        
        [TestMethod]
        public void LargeWebSocketLargestValidDataTest()
        {
            RunWebSocketDataTest("http", "ws", string.Concat(Enumerable.Range(0, 1016).Select(x => "A")));
        }

        [TestMethod]
        public void TestWebSocketHttpListenAndConnect()
        {
            RunWebSocketMessageTest("http", "ws", Utility.TEXT_TO_WRITE);
        }

        [TestMethod]
        public void TestWebSocketHttpsListenAndConnect()
        {
            RunWebSocketMessageTest("https", "wss", Utility.TEXT_TO_WRITE);
        }

        private void RunWebSocketDataTest(string httpPrefix, string wsPrefix, string messageToSend)
        {
            SetupListener(httpPrefix);

            SetupClient(wsPrefix, messageToSend);

            SendAndWaitForResults(Encoding.UTF8.GetBytes(messageToSend));
        }

        private void RunWebSocketMessageTest(string httpPrefix, string wsPrefix, string messageToSend)
        {
            SetupListener(httpPrefix);

            SetupClient(wsPrefix, messageToSend);

            SendAndWaitForResults(messageToSend);
        }

        private void SendAndWaitForResults(string valueToSend)
        {            
            webSocketContextServer.WebSocket.SendAsync(valueToSend, x =>
            {
                sent = x;
                this.serverResetEvent.Set();
            });       
     
            WaitForResults();
        }

        private void SendAndWaitForResults(byte[] valueToSend)
        {
            webSocketContextServer.WebSocket.SendAsync(valueToSend, x =>
            {
                sent = x;
                this.serverResetEvent.Set();
            });

            WaitForResults();
        }

        private void WaitForResults()
        {
            Assert.IsTrue(this.serverResetEvent.WaitOne(WaitOneTimeout), "Timeout waiting for message to send");
            Assert.IsTrue(sent, "Message not sent");

            Assert.IsTrue(this.clientResetEvent.WaitOne(WaitOneTimeout), "Timeout receiving message from server socket");
            Assert.IsTrue(areEqual, "Value sent does not equal value recieved");

            this.socket.Close();

            Assert.IsTrue(this.clientResetEvent.WaitOne(WaitOneTimeout), "Timeout waiting for close"); // Wait for close
        }

        private void SetupClient(string prefix, string expectedResult)
        {
            string url = string.Format("{0}://{1}", prefix, Utility.SITE_URL);

            this.socket = new WebSocket4Net.WebSocket(url);

            this.socket.Closed += (sender, args) =>
            {
                this.logger.Object.Info("Socket Closed");
                this.clientResetEvent.Set();
            };

            this.socket.Opened += (sender, args) =>
            {
                this.logger.Object.Info("Socket Opened");
                this.clientResetEvent.Set();
            };

            this.socket.MessageReceived += (sender, args) =>
            {
                this.logger.Object.Info("Got Message");

                this.areEqual = string.Compare(expectedResult, args.Message, StringComparison.Ordinal) == 0;
                this.clientResetEvent.Set();
            };

            this.socket.DataReceived += (sender, args) =>
            {
                this.logger.Object.Info("Got Data");

                this.areEqual = string.Compare(expectedResult, Encoding.UTF8.GetString(args.Data), StringComparison.Ordinal) == 0;
                this.clientResetEvent.Set();
            };

            this.socket.Open();

            Assert.IsTrue(this.serverResetEvent.WaitOne(WaitOneTimeout), "Timeout waiting for server to accept connection"); // Wait for server to complete connection
            Assert.IsTrue(this.clientResetEvent.WaitOne(WaitOneTimeout), "Timeout waiting for client to open"); // Wait for client Open
        }

        private void SetupListener(string prefix)
        {
            string url = string.Format("{0}://{1}", prefix, Utility.SITE_URL);
            this.listener.Prefixes.Add(url);
            this.listener.Start();

            this.listener.OnContext += context =>
            {
                this.logger.Object.Info("Accepting connection.");

                this.webSocketContextServer = context.AcceptWebSocket(null);
                this.webSocketContextServer.WebSocket.ConnectAsServer();
                this.serverResetEvent.Set();
            };            
        }
    }
}
