using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using HttpListener = SocketHttpListener.Net.HttpListener;


namespace SocketHttpListener.Test
{
    /// <summary>
    /// Tests for the various network timeouts.
    /// </summary>
    [TestClass]
    public class HttpListenerTimeoutManagerTest
    {
        /// <summary>
        /// the shortest timeout we can reasonably expect to still involve a delay.
        /// </summary>
        const int ShortestTime = 100;

        /// <summary>
        /// The timeout tests will reach if ShortestTime does not trigger.
        /// </summary>
        const int FailTestTimeout = 1000;

        /// <summary>
        /// A "very long timeout" (ms) for the HttpListener, for timeouts that we are not 
        /// exercising, and do not expect to reach even on a failing test.
        /// </summary>
        const int ALongTime = 5000; 

        const int BufferSize = 256;

        HttpListener listener;

        [TestInitialize]
        public void TestInit()
        {
            this.listener = new HttpListener();

            string url = string.Format("http://{0}", Utility.SITE_URL);
            this.listener.Prefixes.Add(url);

            this.listener.OnContext += (ctx) => {
                Assert.Fail("Not reached");
            };

            this.listener.TimeoutManager.DrainEntityBody = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.TimeoutManager.EntityBody = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.TimeoutManager.HeaderWait = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.TimeoutManager.IdleConnection = TimeSpan.FromMilliseconds(ALongTime);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (null != this.listener)
            {
                this.listener.Close();
                this.listener = null;
            }
        }


        [TestMethod]
        public async Task TestHttpKeepAliveTimeout()
        {
            // Exercise the IdleConnection timeout. (keepalive)

            this.listener.TimeoutManager.IdleConnection = TimeSpan.FromMilliseconds(100);
            this.listener.Start();

            using (Socket socket = this.GetConnectedSocket())
            {
                Assert.IsTrue(this.IsSocketConnected(socket), "Server should be waiting for us to send a header.");
                await Task.Delay(FailTestTimeout);
                Assert.IsFalse(this.IsSocketConnected(socket), "Server should have given up by now");
            }

            // Set longer timeout
            this.listener.Stop();
            this.listener.TimeoutManager.IdleConnection = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.Start();

            using (Socket socket = this.GetConnectedSocket())
            {
                Assert.IsTrue(this.IsSocketConnected(socket), "Server should be waiting for us to send a header.");
                await Task.Delay(FailTestTimeout);
                Assert.IsTrue(this.IsSocketConnected(socket), "Server should still be waiting");
            }
        }

        const string IncompleteHttpHeader =
            "GET /something HTTP/1.1\n" +
            "Host: 127.0.0.1\n" +
            "Accept: tex"
            ;


        const string SimpleHttpGet =
            "GET " + Utility.SITE_PREFIX + " HTTP/1.1\n" +
            "Host: " + Utility.SITE_HOSTNAME + "\n" +
            "Accept: text/xml\n" +
            "Connection: keep-alive\n" +
            "Keep-Alive: 300000\n" +
            "Content-Type: text/xml\n" +
            "Content-Length: 0\n" +
            "\n"
            ;
        const string IncompleteHttpPost =
            "POST " + Utility.SITE_PREFIX + " HTTP/1.1\n" +
            "Host: " + Utility.SITE_HOSTNAME + "\n" +
            "Accept: text/xml\n" +
            "Connection: keep-alive\n" +
            "Keep-Alive: 300000\n" +
            "Content-Type: text/xml\n" +
            "Content-Length: 9995\n" +
            "\n" +
            "<xml version="
            ;

        [TestMethod]
        public async Task TestHttpReadHeadersTimeout()
        {
            this.listener.TimeoutManager.HeaderWait = TimeSpan.FromMilliseconds(100);
            this.listener.Start();

            byte [] buffer = Encoding.UTF8.GetBytes(IncompleteHttpHeader);
            using (Socket socket = this.GetConnectedSocket())
            {
                // Send an incomplete header.
                this.AwaitWithTimeout(() => { socket.Send(buffer); });
                Assert.IsTrue(this.IsSocketConnected(socket), "Server should be waiting for the rest");
                await Task.Delay(FailTestTimeout);
                Assert.IsFalse(this.IsSocketConnected(socket), "Server should have given up.");
            }

            // * Fail
            this.listener.Stop();
            this.listener.TimeoutManager.HeaderWait = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.Start();
            using (Socket socket = this.GetConnectedSocket())
            {
                this.AwaitWithTimeout(() => { socket.Send(buffer); });
                Assert.IsTrue(this.IsSocketConnected(socket), "Server should be waiting for the rest");
                await Task.Delay(FailTestTimeout);
                Assert.IsTrue(this.IsSocketConnected(socket), "Server should still be waiting.");
            }
        }

        [TestMethod]
        public async Task TestHttpReadBodySyncTimeout()
        {
            this.listener.TimeoutManager.HeaderWait = TimeSpan.FromMilliseconds(100);
            this.listener.TimeoutManager.EntityBody = TimeSpan.FromMilliseconds(100);

            bool gotReadTimeout = false;
            this.listener.OnContext = (ctx) => {
                using (TextReader reader = new StreamReader(ctx.Request.InputStream))
                {
                    Task readToEnd = Task.Run( () => {
                        reader.ReadToEnd(); // Will block until the socket times out.
                    });

                    try
                    {
                        Assert.IsFalse(readToEnd.Wait(FailTestTimeout), "gave up waiting for the socket timeout");
                    }
                    catch (AggregateException aex)
                    {
                        Assert.IsTrue(aex.InnerException is IOException);
                        gotReadTimeout = true;
                    }
                }
            };

            this.listener.Start();

            byte [] buffer = Encoding.UTF8.GetBytes(IncompleteHttpPost);
            using (Socket socket = this.GetConnectedSocket())
            {
                // Send an incomplete header.
                this.AwaitWithTimeout(() => { socket.Send(buffer); });
                Assert.IsTrue(this.IsSocketConnected(socket), "Server should be waiting for the rest");
                await Task.Delay(FailTestTimeout);         // Wait for socket to time out and the handler to fire
                Assert.IsTrue(gotReadTimeout, "The context handler should have timed out");
            }

            this.listener.Stop();
            this.listener.TimeoutManager.EntityBody = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.Start();

            gotReadTimeout = false;
            using (Socket socket = this.GetConnectedSocket())
            {
                // Send an incomplete header.
                this.AwaitWithTimeout(() => { socket.Send(buffer); });
                await Task.Delay(FailTestTimeout);         // Wait for socket to time out and the handler to fire
                Assert.IsFalse(gotReadTimeout, "The context handler should still be waiting");
            }
        }

        const string ResponseBody = 
            "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            + "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
            ;

        /// <summary>
        /// Exercise the Write function's synchronous timeout. ResponseStream.Close()
        /// also makes internal calls to synchronous Write() via InternalWrite, exercised
        /// thusly.
        /// </summary>
        [TestMethod]
        public async Task Test_ResponseStream_WriteSynchronous()
        {
            // Notes:
            // SendHeaders() is triggered by ResponseStream.GetHeaders(), which can be triggered
            // by Write, BeginWrite, and Close. It may not be possible to exercise the timeout 
            // in SendHeaders, because of output buffering.

            this.listener.TimeoutManager.DrainEntityBody = TimeSpan.FromMilliseconds(100);

            bool gotTimeout = false;
            this.listener.OnContext = (ctx) => {
                using (TextWriter writer = new StreamWriter(ctx.Response.OutputStream, Encoding.UTF8, BufferSize))
                {
                    Task writerTask = Task.Run( () => {
                        // Fill the buffers with junk!
                        while (true)
                            writer.Write(ResponseBody);
                    });

                    try
                    {
                        writerTask.Wait(FailTestTimeout); // will throw on socket timeout
                    }
                    catch (AggregateException aex)
                    {
                        Assert.IsTrue(aex.InnerException is IOException);
                        gotTimeout = true;
                    }
                }
                ctx.Response.Close();
            };

            this.listener.Start();

            byte [] buffer = Encoding.UTF8.GetBytes(IncompleteHttpPost);
            gotTimeout = false; // not  yet
            using (Socket socket = GetConnectedSocket())
            {
                this.AwaitWithTimeout(() => { socket.Send(buffer); });
                await Task.Delay(FailTestTimeout);         // Wait for socket to time out and the handler to fire
                Assert.IsTrue(gotTimeout);
                // Can't use this.IsSocketConnected(), because the test needs the reader to stall.
            }

            // * It won't time out, when the timeout is "long"
            this.listener.Stop();
            this.listener.TimeoutManager.DrainEntityBody = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.Start();

            gotTimeout = false;
            using (Socket socket = GetConnectedSocket())
            {
                this.AwaitWithTimeout(() => { socket.Send(buffer); });
                await Task.Delay(FailTestTimeout);
                Assert.IsFalse(gotTimeout);
            }
        }

        /// <summary>
        /// Make sure async writes time out properly.
        /// </summary>
        [TestMethod]
        public async Task Test_ResponseStream_WriteAsync()
        {
            this.listener.TimeoutManager.DrainEntityBody = TimeSpan.FromMilliseconds(100);

            bool gotTimeout = false;
            this.listener.OnContext = (ctx) => {
                using (TextWriter writer = new StreamWriter(ctx.Response.OutputStream))
                {
                    Task writerTask = Task.Run(async () => {
                        // Fill the buffers with junk...asynchronously!
                        while (true) {
                            await writer.WriteAsync("12345678901234567890123456789012345678901234567890123456789012345678901234567890\n");
                        }
                    });

                    try
                    {
                        writerTask.Wait(FailTestTimeout);
                        Assert.Fail("Not reached: should have thrown exception."); 
                    }
                    catch (AggregateException aex)
                    {
                        Assert.IsTrue(aex.InnerException is IOException);
                        gotTimeout = true;
                    }
                }
                ctx.Response.Close();
            };

            this.listener.Start();

            byte [] buffer = Encoding.UTF8.GetBytes(IncompleteHttpPost);
            byte [] readBuffer = new byte[1000];
            using (Socket socket = GetConnectedSocket())
            {
                this.AwaitWithTimeout(() => { socket.Send(buffer); });
                await Task.Delay(FailTestTimeout);         // Wait for socket to time out and the handler to fire

                // Get the first few bytes to get the flow started, then stall.
                this.AwaitWithTimeout(() => { socket.Receive(readBuffer); }, FailTestTimeout);
                await Task.Delay(FailTestTimeout);         // Wait for socket to time out and the handler to fire
                Assert.IsTrue(gotTimeout);
            }

            // * Fail test

            this.listener.Stop();
            this.listener.TimeoutManager.DrainEntityBody = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.Start();

            gotTimeout = false;
            using (Socket socket = GetConnectedSocket())
            {
                this.AwaitWithTimeout(() => { socket.Send(buffer); });
                await Task.Delay(FailTestTimeout);         // Wait for socket to time out and the handler to fire

                // Get the first few bytes to get the flow started, then stall.
                this.AwaitWithTimeout(() => { socket.Receive(readBuffer); }, FailTestTimeout);
                await Task.Delay(FailTestTimeout);         // Wait for socket to time out and the handler to fire
                Assert.IsFalse(gotTimeout);
            }
        }

        /// <summary>
        /// test timeouts in HttpListenerRequest.FlushInput() triggered by HttpListenerResponse.Close().
        /// </summary>
        [TestMethod]
        public async Task Test_HttpListenerRequest_FlushInput()
        {
            this.listener.TimeoutManager.EntityBody = TimeSpan.FromMilliseconds(100); // The relevant timeout

            this.listener.OnContext = (ctx) => {

                // Do not close ctx.Request.InputStream.
                // If we close the input stream, FlushInput will not be able
                // to read from the input, and we will not be testing its timeouts.

                ctx.Response.Close();  // Closing the response trigers FlushInput()
            };
            this.listener.Start();

            // With a 100ms timeout, the socket should time out when it can not read the rest of the request body

            byte [] buffer = Encoding.UTF8.GetBytes(IncompleteHttpPost);
            using (Socket socket = GetConnectedSocket())
            {
                this.AwaitWithTimeout(() => { 
                    socket.Send(buffer);
                });

                // Give the OnContext handler time to trigger the network flush and time out. 
                await(Task.Delay(FailTestTimeout));
                Assert.IsFalse(this.IsSocketConnected(socket), "Server should have closed the connection by now");
            }


            // Test failure: "I want to see a negative before I provide you with a positive."
            // With a LONG timeout, the listener should hang.

            this.listener.Stop();
            this.listener.TimeoutManager.EntityBody = TimeSpan.FromMilliseconds(ALongTime);
            this.listener.Start();

            using (Socket socket = GetConnectedSocket())
            {
                this.AwaitWithTimeout(() => { 
                    socket.Send(buffer);
                });

                // Give the OnContext handler time to trigger the network flush and time out. 
                await(Task.Delay(FailTestTimeout));
                Assert.IsTrue(this.IsSocketConnected(socket), "Server should still be stuck in FlushInput()");
            }
        }
        
        bool IsSocketConnected(Socket socket)
        {
            // This will detect if the socket was closed gracefully, but not a 
            // hard network fault/pulled cable. Should work for testing.

            // The test only works if the read buffer has been drained.
            // In this test, this side effect is acceptable, but beware if copying
            // this code for use elsewhere.

            byte [] buffer = new byte[1024];
            while (socket.Available > 0) {
                socket.Receive(buffer);
            }

            bool part1 = socket.Poll(FailTestTimeout, SelectMode.SelectRead);
            bool part2 = (socket.Available == 0);
            if (part1 && part2)
                return false;
            else
                return true;
        }

        /// <summary>
        /// Return a socket connected to the test listener.
        /// </summary>
        /// <returns></returns>
        Socket GetConnectedSocket()
        {
            Socket socket = new Socket(SocketType.Stream, ProtocolType.IP);
            try
            {
                // Establish a no-linger, small-buffers socket connection for simulating/detecting
                // stalled network conditions.

                socket.LingerState = new LingerOption(true, 0); // disconnect immediately please
                socket.NoDelay = true;
                socket.ReceiveBufferSize = BufferSize;
                socket.SendBufferSize = BufferSize;
                this.AwaitWithTimeout(() => { 
                    socket.Connect(Utility.SITE_HOSTNAME, Utility.SITE_PORT);
                });

                Assert.IsTrue(this.IsSocketConnected(socket), "Test IsSocketConnected()");
                return socket;
            }
            catch (Exception)
            {
                socket.Dispose();
                throw;
            }
        }

        void AwaitWithTimeout(Action action) 
        {
            this.AwaitWithTimeout(Task.Run(action), ALongTime);
        }

        void AwaitWithTimeout(Action action, int timeoutMs) 
        {
            this.AwaitWithTimeout(Task.Run(action), timeoutMs);
        }

        /// <summary>
        /// Simulate "waiting forever", using a timeout that is longer than the 
        /// longest timeout in the HttpListener.
        /// </summary>
        void AwaitWithTimeout(Task task) 
        {
            this.AwaitWithTimeout(task, ALongTime);
        }

        /// <summary>
        /// Throws TimeoutException if timeout is reached
        /// </summary>
        void AwaitWithTimeout(Task task, int timeoutMs) 
        {
            try
            {
                if (!task.Wait(timeoutMs))
                    throw new TimeoutException();
            }
            catch (AggregateException aex)
            {
                if (null != aex.InnerException)
                    throw aex.InnerException;

                throw;
            }
        }
    }
}
