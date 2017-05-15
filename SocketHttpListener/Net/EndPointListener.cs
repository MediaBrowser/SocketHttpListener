using Patterns.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace SocketHttpListener.Net
{
    sealed class EndPointListener
    {
        HttpListener listener;
        IPEndPoint endpoint;
        Socket sock;
        Dictionary<ListenerPrefix,HttpListener> prefixes;  // Dictionary <ListenerPrefix, HttpListener>
        List<ListenerPrefix> unhandled; // List<ListenerPrefix> unhandled; host = '*'
        List<ListenerPrefix> all;       // List<ListenerPrefix> all;  host = '+'
        X509Certificate cert;
        bool secure;
        Dictionary<HttpConnection, HttpConnection> unregistered;
        private readonly ILogger _logger;
        private bool _closed;
        private readonly bool _enableDualMode;

        public EndPointListener(HttpListener listener, IPAddress addr, int port, bool secure, X509Certificate cert, ILogger logger)
        {
            this.listener = listener;
            _logger = logger;

            this.secure = secure;
            this.cert = cert;

            _enableDualMode = Equals(addr, IPAddress.IPv6Any);
            endpoint = new IPEndPoint(addr, port);

            prefixes = new Dictionary<ListenerPrefix, HttpListener>();
            unregistered = new Dictionary<HttpConnection, HttpConnection>();

            CreateSocket();
        }

        internal HttpListener Listener
        {
            get
            {
                return listener;
            }
        }

        private void CreateSocket()
        {
            if (_enableDualMode)
            {
                _logger.Info("Enabling DualMode socket");

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    sock = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    EnableDualMode(sock);
                }
                else
                {
                    sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
                }
            }
            else
            {
                _logger.Info("Enabling non-DualMode socket");
                sock = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            }

            sock.Bind(endpoint);

            // This is the number TcpListener uses.
            sock.Listen(2147483647);

            Socket dummy = null;
            StartAccept(null, ref dummy);
            _closed = false;
        }

        private void EnableDualMode(Socket socket)
        {
            try
            {
                //sock.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);

                socket.DualMode = true;
            }
            catch (MissingMemberException)
            {
            }
        }

        public void StartAccept(SocketAsyncEventArgs acceptEventArg, ref Socket accepted)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
            }
            else
            {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            try
            {
                bool willRaiseEvent = sock.AcceptAsync(acceptEventArg);

                if (!willRaiseEvent)
                {
                    ProcessAccept(acceptEventArg);
                }
            }
            catch
            {
                if (accepted != null)
                {
                    try
                    {
                        accepted.Close();
                    }
                    catch
                    {
                    }
                    accepted = null;
                }
            }
        }

        // This method is the callback method associated with Socket.AcceptAsync  
        // operations and is invoked when an accept operation is complete 
        // 
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (_closed)
            {
                return;
            }

            // http://msdn.microsoft.com/en-us/library/system.net.sockets.socket.acceptasync%28v=vs.110%29.aspx
            // Under certain conditions ConnectionReset can occur
            // Need to attept to re-accept
            if (e.SocketError == SocketError.ConnectionReset)
            {
                _logger.Error("SocketError.ConnectionReset reported. Attempting to re-accept.");
                Socket dummy = null;
                StartAccept(e, ref dummy);
                return;
            }

            var acceptSocket = e.AcceptSocket;
            if (acceptSocket != null)
            {
                ProcessAccept(acceptSocket);
            }

            if (sock != null)
            {
                // Accept the next connection request
                StartAccept(e, ref acceptSocket);
            }
        }

        private void ProcessAccept(Socket accepted)
        {
            try
            {
                var listener = this;

                if (listener.secure && listener.cert == null)
                {
                    accepted.Close();
                    return;
                }

                HttpConnection conn = new HttpConnection(_logger, accepted, listener, listener.secure, listener.cert);
                //_logger.Debug("Adding unregistered connection to {0}. Id: {1}", accepted.RemoteEndPoint, connectionId);
                lock (listener.unregistered)
                {
                    listener.unregistered[conn] = conn;
                }
                conn.BeginReadRequest();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error in ProcessAccept", ex);
            }
        }

        internal void RemoveConnection(HttpConnection conn)
        {
            lock (unregistered)
            {
                unregistered.Remove(conn);
            }
        }

        public bool BindContext(HttpListenerContext context)
        {
            HttpListenerRequest req = context.Request;
            ListenerPrefix prefix;
            HttpListener listener = SearchListener(req.Url, out prefix);
            if (listener == null)
                return false;

            context.Listener = listener;
            context.Connection.Prefix = prefix;
            return true;
        }

        public void UnbindContext(HttpListenerContext context)
        {
            if (context == null || context.Request == null)
                return;

            context.Listener.UnregisterContext(context);
        }

        HttpListener SearchListener(Uri uri, out ListenerPrefix prefix)
        {
            prefix = null;
            if (uri == null)
                return null;

            string host = uri.Host;
            int port = uri.Port;
            string path = WebUtility.UrlDecode(uri.AbsolutePath);
            string path_slash = path[path.Length - 1] == '/' ? path : path + "/";

            HttpListener best_match = null;
            int best_length = -1;

            if (host != null && host != "")
            {
                var p_ro = prefixes;
                foreach (ListenerPrefix p in p_ro.Keys)
                {
                    string ppath = p.Path;
                    if (ppath.Length < best_length)
                        continue;

                    if (p.Host != host || p.Port != port)
                        continue;

                    if (path.StartsWith(ppath) || path_slash.StartsWith(ppath))
                    {
                        best_length = ppath.Length;
                        best_match = (HttpListener)p_ro[p];
                        prefix = p;
                    }
                }
                if (best_length != -1)
                    return best_match;
            }

            List<ListenerPrefix> list = unhandled;
            best_match = MatchFromList(host, path, list, out prefix);
            if (path != path_slash && best_match == null)
                best_match = MatchFromList(host, path_slash, list, out prefix);
            if (best_match != null)
                return best_match;

            list = all;
            best_match = MatchFromList(host, path, list, out prefix);
            if (path != path_slash && best_match == null)
                best_match = MatchFromList(host, path_slash, list, out prefix);
            if (best_match != null)
                return best_match;

            return null;
        }

        HttpListener MatchFromList(string host, string path, List<ListenerPrefix> list, out ListenerPrefix prefix)
        {
            prefix = null;
            if (list == null)
                return null;

            HttpListener best_match = null;
            int best_length = -1;

            foreach (ListenerPrefix p in list)
            {
                string ppath = p.Path;
                if (ppath.Length < best_length)
                    continue;

                if (path.StartsWith(ppath))
                {
                    best_length = ppath.Length;
                    best_match = p.Listener;
                    prefix = p;
                }
            }

            return best_match;
        }

        void AddSpecial(List<ListenerPrefix> coll, ListenerPrefix prefix)
        {
            if (coll == null)
                return;

            foreach (ListenerPrefix p in coll)
            {
                if (p.Path == prefix.Path) //TODO: code
                    throw new System.Net.HttpListenerException(400, "Prefix already in use.");
            }
            coll.Add(prefix);
        }

        bool RemoveSpecial(List<ListenerPrefix> coll, ListenerPrefix prefix)
        {
            if (coll == null)
                return false;

            int c = coll.Count;
            for (int i = 0; i < c; i++)
            {
                ListenerPrefix p = (ListenerPrefix)coll[i];
                if (p.Path == prefix.Path)
                {
                    coll.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        void CheckIfRemove()
        {
            if (prefixes.Count > 0)
                return;

            List<ListenerPrefix> list = unhandled;
            if (list != null && list.Count > 0)
                return;

            list = all;
            if (list != null && list.Count > 0)
                return;

            EndPointManager.RemoveEndPoint(this, endpoint);
        }

        public void Close()
        {
            _closed = true;
            sock.Close();
            lock (unregistered)
            {
                //
                // Clone the list because RemoveConnection can be called from Close
                //
                var connections = new List<HttpConnection>(unregistered.Keys);

                foreach (HttpConnection c in connections)
                    c.Close(true);
                unregistered.Clear();
            }
        }

        public void AddPrefix(ListenerPrefix prefix, HttpListener listener)
        {
            List<ListenerPrefix> current;
            List<ListenerPrefix> future;
            if (prefix.Host == "*")
            {
                do
                {
                    current = unhandled;
                    future = (current != null) ? current.ToList() : new List<ListenerPrefix>();
                    prefix.Listener = listener;
                    AddSpecial(future, prefix);
                } while (Interlocked.CompareExchange(ref unhandled, future, current) != current);
                return;
            }

            if (prefix.Host == "+")
            {
                do
                {
                    current = all;
                    future = (current != null) ? current.ToList() : new List<ListenerPrefix>();
                    prefix.Listener = listener;
                    AddSpecial(future, prefix);
                } while (Interlocked.CompareExchange(ref all, future, current) != current);
                return;
            }

            Dictionary<ListenerPrefix, HttpListener> prefs;
            Dictionary<ListenerPrefix, HttpListener> p2;
            do
            {
                prefs = prefixes;
                if (prefs.ContainsKey(prefix))
                {
                    HttpListener other = (HttpListener)prefs[prefix];
                    if (other != listener) // TODO: code.
                        throw new System.Net.HttpListenerException(400, "There's another listener for " + prefix);
                    return;
                }
                p2 = new Dictionary<ListenerPrefix, HttpListener>(prefs);
                p2[prefix] = listener;
            } while (Interlocked.CompareExchange(ref prefixes, p2, prefs) != prefs);
        }

        public void RemovePrefix(ListenerPrefix prefix, HttpListener listener)
        {
            List<ListenerPrefix> current;
            List<ListenerPrefix> future;
            if (prefix.Host == "*")
            {
                do
                {
                    current = unhandled;
                    future = (current != null) ? current.ToList() : new List<ListenerPrefix>();
                    if (!RemoveSpecial(future, prefix))
                        break; // Prefix not found
                } while (Interlocked.CompareExchange(ref unhandled, future, current) != current);
                CheckIfRemove();
                return;
            }

            if (prefix.Host == "+")
            {
                do
                {
                    current = all;
                    future = (current != null) ? current.ToList() : new List<ListenerPrefix>();
                    if (!RemoveSpecial(future, prefix))
                        break; // Prefix not found
                } while (Interlocked.CompareExchange(ref all, future, current) != current);
                CheckIfRemove();
                return;
            }

            Dictionary<ListenerPrefix, HttpListener> prefs;
            Dictionary<ListenerPrefix, HttpListener> p2;
            do
            {
                prefs = prefixes;
                if (!prefs.ContainsKey(prefix))
                    break;

                p2 = new Dictionary<ListenerPrefix, HttpListener>(prefs);
                p2.Remove(prefix);
            } while (Interlocked.CompareExchange(ref prefixes, p2, prefs) != prefs);
            CheckIfRemove();
        }
    }
}
