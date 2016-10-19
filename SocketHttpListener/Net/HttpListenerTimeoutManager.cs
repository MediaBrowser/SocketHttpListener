using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SocketHttpListener.Net
{
    /// <summary>
    /// Timeouts.
    /// </summary>
    public class HttpListenerTimeoutManager
    {
        private TimeSpan idleConnection = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Time the listener will wait for the next HTTP request.
        /// 
        /// Defaults to 5 minutes for backward compatibility.
        /// 
        /// This is the default/maximum timeout that will be used while waiting for 
        /// a KeepAlive session.
        /// </summary>
        public TimeSpan IdleConnection 
        {
            get
            {
                return this.idleConnection;
            }
            set
            {
                this.idleConnection = value;
            }
        }

        private TimeSpan headerWait = TimeSpan.FromMinutes(5);
        ///// <summary>
        ///// Maximum time the listener will spend reading an HTTP request's headers.
        ///// 
        ///// The network read timeout used when http headers have started
        ///// to arrive but not finished.
        ///// </summary>
        public TimeSpan HeaderWait
        {
            get
            {
                return this.headerWait;
            }
            set
            {
                this.headerWait = value;
            }
        }

        private TimeSpan entityBody = TimeSpan.FromMinutes(5);
        /// <summary>
        /// The read timeout when reading the body. Set for synchronous IO.
        /// 
        /// Not enforced. If the OnContext handler uses async IO, it will need to 
        /// handle timeouts man ually.
        /// 
        /// Defaults to 5 minutes.
        /// </summary>
        public TimeSpan EntityBody
        {
            get
            {
                return this.entityBody;
            }
            set
            {
                this.entityBody = value;
            }
        }

        private TimeSpan drainEntityBody = TimeSpan.FromMinutes(5);
        /// <summary>
        /// The write timeout, used during the body write. Set for synchronous IO.
        /// 
        /// If the OnContext handler uses async IO, it will need to handle timeouts 
        /// manually.
        /// 
        /// This timeout is used for all HTTP writes, including headers, because the 
        /// HttpListenerTimeoutManager spec does not have a separate timeout field for
        /// this.
        /// </summary>
        public TimeSpan DrainEntityBody
        {
            get
            {
                return this.drainEntityBody;
            }
            set
            {
                this.drainEntityBody = value;
            }
        }
    }
}
