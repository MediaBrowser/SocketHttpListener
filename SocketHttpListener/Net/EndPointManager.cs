using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Patterns.Logging;

namespace SocketHttpListener.Net
{
    sealed class EndPointManager
    {
        // Dictionary<IPAddress, Dictionary<int, EndPointListener>>
        static Hashtable ip_to_endpoints = new Hashtable();

        private EndPointManager()
        {
        }

        public static void AddListener(ILogger logger, HttpListener listener)
        {
            ArrayList added = new ArrayList();
            try
            {
                lock (ip_to_endpoints)
                {
                    foreach (string prefix in listener.Prefixes)
                    {
                        AddPrefixInternal(logger, prefix, listener);
                        added.Add(prefix);
                    }
                }
            }
            catch
            {
                foreach (string prefix in added)
                {
                    RemovePrefix(logger, prefix, listener);
                }
                throw;
            }
        }

        public static void AddPrefix(ILogger logger, string prefix, HttpListener listener)
        {
            lock (ip_to_endpoints)
            {
                AddPrefixInternal(logger, prefix, listener);
            }
        }

        static void AddPrefixInternal(ILogger logger, string p, HttpListener listener)
        {
            ListenerPrefix lp = new ListenerPrefix(p);
            if (lp.Path.IndexOf('%') != -1)
                throw new System.Net.HttpListenerException(400, "Invalid path.");

            if (lp.Path.IndexOf("//", StringComparison.Ordinal) != -1) // TODO: Code?
                throw new System.Net.HttpListenerException(400, "Invalid path.");

            // listens on all the interfaces if host name cannot be parsed by IPAddress.
            EndPointListener epl = GetEPListener(logger, lp.Host, lp.Port, listener, lp.Secure);
            epl.AddPrefix(lp, listener);
        }

        private static bool SupportsDualMode()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return true;
            }

            return false;
            //return GetMonoVersion() >= new Version(4, 4);
        }

        private static Version GetMonoVersion()
        {
            Type type = Type.GetType("Mono.Runtime");
            if (type != null)
            {
                MethodInfo displayName = type.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
                var displayNameValue = displayName.Invoke(null, null).ToString().Trim().Split(' ')[0];

                Version version;
                if (Version.TryParse(displayNameValue, out version))
                {
                    return version;
                }
            }

            return new Version(1, 0);
        }

        private static IPAddress GetIpAnyAddress()
        {
            return SupportsDualMode() ? IPAddress.IPv6Any : IPAddress.Any;
        }

        static EndPointListener GetEPListener(ILogger logger, string host, int port, HttpListener listener, bool secure)
        {
            IPAddress addr;
            if (host == "*" || host == "+")
                addr = GetIpAnyAddress();
            else if (IPAddress.TryParse(host, out addr) == false)
            {
                try
                {
                    IPHostEntry iphost = Dns.GetHostByName(host);
                    if (iphost != null)
                        addr = iphost.AddressList[0];
                    else
                        addr = GetIpAnyAddress();
                }
                catch
                {
                    addr = GetIpAnyAddress();
                }
            }
            Hashtable p = null;  // Dictionary<int, EndPointListener>
            if (ip_to_endpoints.ContainsKey(addr))
            {
                p = (Hashtable)ip_to_endpoints[addr];
            }
            else
            {
                p = new Hashtable();
                ip_to_endpoints[addr] = p;
            }

            EndPointListener epl = null;
            if (p.ContainsKey(port))
            {
                epl = (EndPointListener)p[port];
            }
            else
            {
                epl = new EndPointListener(listener, addr, port, secure, listener.Certificate, logger);
                p[port] = epl;
            }

            return epl;
        }

        public static void RemoveEndPoint(EndPointListener epl, IPEndPoint ep)
        {
            lock (ip_to_endpoints)
            {
                // Dictionary<int, EndPointListener> p
                Hashtable p = null;
                p = (Hashtable)ip_to_endpoints[ep.Address];
                p.Remove(ep.Port);
                if (p.Count == 0)
                {
                    ip_to_endpoints.Remove(ep.Address);
                }
                epl.Close();
            }
        }

        public static void RemoveListener(ILogger logger, HttpListener listener)
        {
            lock (ip_to_endpoints)
            {
                foreach (string prefix in listener.Prefixes)
                {
                    RemovePrefixInternal(logger, prefix, listener);
                }
            }
        }

        public static void RemovePrefix(ILogger logger, string prefix, HttpListener listener)
        {
            lock (ip_to_endpoints)
            {
                RemovePrefixInternal(logger, prefix, listener);
            }
        }

        static void RemovePrefixInternal(ILogger logger, string prefix, HttpListener listener)
        {
            ListenerPrefix lp = new ListenerPrefix(prefix);
            if (lp.Path.IndexOf('%') != -1)
                return;

            if (lp.Path.IndexOf("//", StringComparison.Ordinal) != -1)
                return;

            EndPointListener epl = GetEPListener(logger, lp.Host, lp.Port, listener, lp.Secure);
            epl.RemovePrefix(lp, listener);
        }
    }
}
