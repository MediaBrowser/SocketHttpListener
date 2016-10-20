using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Patterns.Logging;

namespace SocketHttpListener.Net
{
    sealed class HttpConnection
    {
        static AsyncCallback onread_cb = new AsyncCallback(OnRead);
        const int BufferSize = 8192;
        Socket sock;
        Stream stream;
        EndPointListener epl;
        MemoryStream ms;
        byte[] buffer;
        HttpListenerContext context;
        StringBuilder current_line;
        ListenerPrefix prefix;
        RequestStream i_stream;
        ResponseStream o_stream;
        bool chunked;
        int reuses;
        bool context_bound;
        bool secure;
        Timer timer;
        IPEndPoint local_ep;
        HttpListener last_listener;
        int[] client_cert_errors;
        X509Certificate cert;
        SslStream ssl_stream;

        private ILogger _logger;
        private readonly string _connectionId;

        public HttpConnection(ILogger logger, Socket sock, EndPointListener epl, bool secure, X509Certificate cert, string connectionId)
        {
            _connectionId = connectionId;
            _logger = logger;
            this.sock = sock;
            this.epl = epl;
            this.secure = secure;
            this.cert = cert;
            this.SetSocketTimeout(sock);
            if (secure == false)
            {
                stream = new NetworkStream(sock, false);
            }
            else
            {
                //ssl_stream = epl.Listener.CreateSslStream(new NetworkStream(sock, false), false, (t, c, ch, e) =>
                //{
                //    if (c == null)
                //        return true;
                //    var c2 = c as X509Certificate2;
                //    if (c2 == null)
                //        c2 = new X509Certificate2(c.GetRawCertData());
                //    client_cert = c2;
                //    client_cert_errors = new int[] { (int)e };
                //    return true;
                //});
                //stream = ssl_stream.AuthenticatedStream;

                ssl_stream = new SslStream(new NetworkStream(sock, false), false);
                ssl_stream.AuthenticateAsServer(cert);
                stream = ssl_stream;
            }
            timer = new Timer(OnTimeout, null, Timeout.Infinite, Timeout.Infinite);
            Init();
        }

        void SetSocketTimeout(Socket sock)
        {
            // Socket timeout should be >= the largest applicable http listener timeout, and 
            // Certainly > 0.
            HttpListenerTimeoutManager mgr = this.epl.Listener.TimeoutManager;

            TimeSpan readTimeout = TimeSpan.FromMilliseconds(50);
            if (mgr.EntityBody > readTimeout)
                readTimeout = mgr.EntityBody;
            if (mgr.HeaderWait > readTimeout)
                readTimeout = mgr.HeaderWait;

            TimeSpan writeTimeout = TimeSpan.FromMilliseconds(50);
            if (mgr.DrainEntityBody > writeTimeout)
                writeTimeout = mgr.DrainEntityBody;

            sock.ReceiveTimeout = (int)readTimeout.TotalMilliseconds;
            sock.SendTimeout = (int)writeTimeout.TotalMilliseconds;
        }

        public Stream Stream
        {
            get
            {
                return stream;
            }
        }

        internal int[] ClientCertificateErrors
        {
            get { return client_cert_errors; }
        }

        void Init()
        {
            if (ssl_stream != null)
            {
                //ssl_stream.AuthenticateAsServer(client_cert, true, (SslProtocols)ServicePointManager.SecurityProtocol, false);
            }

            context_bound = false;
            i_stream = null;
            o_stream = null;
            prefix = null;
            chunked = false;
            ms = new MemoryStream();
            position = 0;
            input_state = InputState.RequestLine;
            line_state = LineState.None;
            context = new HttpListenerContext(this, _logger);
        }

        public bool IsClosed
        {
            get { return (sock == null); }
        }

        public int Reuses
        {
            get { return reuses; }
        }

        public IPEndPoint LocalEndPoint
        {
            get
            {
                if (local_ep != null)
                    return local_ep;

                local_ep = (IPEndPoint)sock.LocalEndPoint;
                return local_ep;
            }
        }

        public IPEndPoint RemoteEndPoint
        {
            get { return (IPEndPoint)sock.RemoteEndPoint; }
        }

        public bool IsSecure
        {
            get { return secure; }
        }

        public ListenerPrefix Prefix
        {
            get { return prefix; }
            set { prefix = value; }
        }

        void OnTimeout(object unused)
        {
            //_logger.Debug("HttpConnection keep alive timer fired. ConnectionId: {0}.", _connectionId);
            CloseSocket();
            Unbind();
        }

        public void BeginReadRequest()
        {
            if (buffer == null)
                buffer = new byte[BufferSize];
            try
            {
                timer.Change(this.epl.Listener.TimeoutManager.IdleConnection, Timeout.InfiniteTimeSpan);
                stream.BeginRead(buffer, 0, BufferSize, onread_cb, this);
            }
            catch
            {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                CloseSocket();
                Unbind();
            }
        }

        public RequestStream GetRequestStream(bool chunked, long contentlength)
        {
            if (i_stream == null)
            {
                byte[] buffer = ms.GetBuffer();
                int length = (int)ms.Length;
                ms = null;
                if (chunked)
                {
                    this.chunked = true;
                    //context.Response.SendChunked = true;
                    i_stream = new ChunkedInputStream(context, stream, buffer, position, length - position);
                }
                else {
                    i_stream = new RequestStream(stream, buffer, position, length - position, contentlength);
                }
            }
            return i_stream;
        }

        public ResponseStream GetResponseStream()
        {
            // TODO: can we get this stream before reading the input?
            if (o_stream == null)
            {
                HttpListener listener = context.Listener;

                if (listener == null)
                    return new ResponseStream(stream, context.Response, true);

                o_stream = new ResponseStream(stream, context.Response, listener.IgnoreWriteExceptions);
            }
            return o_stream;
        }

        static void OnRead(IAsyncResult ares)
        {
            HttpConnection cnc = (HttpConnection)ares.AsyncState;
            cnc.OnReadInternal(ares);
        }

        void OnReadInternal(IAsyncResult ares)
        {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            int nread = -1;
            try
            {
                nread = stream.EndRead(ares);
                ms.Write(buffer, 0, nread);
                if (ms.Length > 32768)
                {
                    SendError("Bad request", 400);
                    Close(true);
                    return;
                }
            }
            catch (Exception ex)
            {
                OnReadInternalException(ms, ex);
                return;
            }

            if (nread == 0)
            {
                //if (ms.Length > 0)
                //	SendError (); // Why bother?
                CloseSocket();
                Unbind();
                return;
            }

            if (ProcessInput(ms))
            {
                if (!context.HaveError)
                    context.Request.FinishInitialization();

                if (context.HaveError)
                {
                    SendError();
                    Close(true);
                    return;
                }

                if (!epl.BindContext(context))
                {
                    SendError("Invalid host", 400);
                    Close(true);
                    return;
                }
                HttpListener listener = context.Listener;
                if (last_listener != listener)
                {
                    RemoveConnection();
                    listener.AddConnection(this);
                    last_listener = listener;
                }

                context_bound = true;
                listener.RegisterContext(context);
                return;
            }
            try
            {
                timer.Change(this.epl.Listener.TimeoutManager.HeaderWait, Timeout.InfiniteTimeSpan);
                stream.BeginRead(buffer, 0, BufferSize, onread_cb, this);
            }
            catch (IOException ex)
            {
                OnReadInternalException(ms, ex);
            }
        }

        private void OnReadInternalException(MemoryStream ms, Exception ex)
        {
            //_logger.ErrorException("Error in HttpConnection.OnReadInternal", ex);

            if (ms != null && ms.Length > 0)
                SendError();
            if (sock != null)
            {
                CloseSocket();
                Unbind();
            }
        }

        void RemoveConnection()
        {
            if (last_listener == null)
                epl.RemoveConnection(this);
            else
                last_listener.RemoveConnection(this);
        }

        enum InputState
        {
            RequestLine,
            Headers
        }

        enum LineState
        {
            None,
            CR,
            LF
        }

        InputState input_state = InputState.RequestLine;
        LineState line_state = LineState.None;
        int position;

        // true -> done processing
        // false -> need more input
        bool ProcessInput(MemoryStream ms)
        {
            byte[] buffer = ms.GetBuffer();
            int len = (int)ms.Length;
            int used = 0;
            string line;

            while (true)
            {
                if (context.HaveError)
                    return true;

                if (position >= len)
                    break;

                try
                {
                    line = ReadLine(buffer, position, len - position, ref used);
                    position += used;
                }
                catch
                {
                    context.ErrorMessage = "Bad request";
                    context.ErrorStatus = 400;
                    return true;
                }

                if (line == null)
                    break;

                if (line == "")
                {
                    if (input_state == InputState.RequestLine)
                        continue;
                    current_line = null;
                    ms = null;
                    return true;
                }

                if (input_state == InputState.RequestLine)
                {
                    context.Request.SetRequestLine(line);
                    input_state = InputState.Headers;
                }
                else
                {
                    try
                    {
                        context.Request.AddHeader(line);
                    }
                    catch (Exception e)
                    {
                        context.ErrorMessage = e.Message;
                        context.ErrorStatus = 400;
                        return true;
                    }
                }
            }

            if (used == len)
            {
                ms.SetLength(0);
                position = 0;
            }
            return false;
        }

        string ReadLine(byte[] buffer, int offset, int len, ref int used)
        {
            if (current_line == null)
                current_line = new StringBuilder(128);
            int last = offset + len;
            used = 0;
            for (int i = offset; i < last && line_state != LineState.LF; i++)
            {
                used++;
                byte b = buffer[i];
                if (b == 13)
                {
                    line_state = LineState.CR;
                }
                else if (b == 10)
                {
                    line_state = LineState.LF;
                }
                else {
                    current_line.Append((char)b);
                }
            }

            string result = null;
            if (line_state == LineState.LF)
            {
                line_state = LineState.None;
                result = current_line.ToString();
                current_line.Length = 0;
            }

            return result;
        }

        public void SendError(string msg, int status)
        {
            try
            {
                HttpListenerResponse response = context.Response;
                response.StatusCode = status;
                response.ContentType = "text/html";
                string description = HttpListenerResponse.GetStatusDescription(status);
                string str;
                if (msg != null)
                    str = String.Format("<h1>{0} ({1})</h1>", description, msg);
                else
                    str = String.Format("<h1>{0}</h1>", description);

                byte[] error = context.Response.ContentEncoding.GetBytes(str);
                response.Close(error, false);
            }
            catch
            {
                // response was already closed
            }
        }

        public void SendError()
        {
            SendError(context.ErrorMessage, context.ErrorStatus);
        }

        void Unbind()
        {
            if (context_bound)
            {
                epl.UnbindContext(context);
                context_bound = false;
            }
        }

        public void Close()
        {
            Close(false);
        }

        private void CloseSocket()
        {
            if (sock == null)
                return;

            try
            {
                sock.Close();
            }
            catch
            {
            }
            finally
            {
                sock = null;
            }
            RemoveConnection();
        }

        internal void Close(bool force_close)
        {
            if (sock != null)
            {
                if (!context.Request.IsWebSocketRequest || force_close)
                {
                    Stream st = GetResponseStream();
                    if (st != null)
                        st.Close();

                    o_stream = null;
                }
            }

            if (sock != null)
            {
                force_close |= !context.Request.KeepAlive;
                if (!force_close)
                    force_close = (context.Response.Headers["connection"] == "close");
                /*
				if (!force_close) {
//					bool conn_close = (status_code == 400 || status_code == 408 || status_code == 411 ||
//							status_code == 413 || status_code == 414 || status_code == 500 ||
//							status_code == 503);
					force_close |= (context.Request.ProtocolVersion <= HttpVersion.Version10);
				}
				*/

                if (!force_close && context.Request.FlushInput(this.epl.Listener.TimeoutManager.EntityBody))
                {
                    if (chunked && context.Response.ForceCloseChunked == false)
                    {
                        // Don't close. Keep working.
                        reuses++;
                        Unbind();
                        Init();
                        BeginReadRequest();
                        return;
                    }

                    // BUG: isn't this exactly the same code that is in the if() block above?
                    reuses++;
                    Unbind();
                    Init();
                    BeginReadRequest();
                    return;
                }

                Socket s = sock;
                sock = null;
                try
                {
                    if (s != null)
                        s.Shutdown(SocketShutdown.Both);
                }
                catch
                {
                }
                finally
                {
                    if (s != null)
                        s.Close();
                }
                Unbind();
                RemoveConnection();
                return;
            }
        }
    }
}