using System;
using System.Threading;

namespace SocketHttpListener.Net
{
    /// <summary>
    /// A Timer that can be stopped and restarted, for timing the total duration
    /// of async operations.
    /// </summary>
    public class ResumableTimer
    {
        readonly Timer timer;
        DateTime endTime = DateTime.Now;

        public ResumableTimer(TimerCallback timeoutCallback)
            : this(timeoutCallback, null)
        {
        }

        public ResumableTimer(TimerCallback timeoutCallback, object data)
        {
            timer = new Timer(timeoutCallback, data, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start(TimeSpan newTimeout)
        {
            this.endTime = DateTime.Now + newTimeout;
            this.timer.Change(newTimeout, Timeout.InfiniteTimeSpan);
        }

        public void Stop()
        {
            this.timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Continue running a paused timer.
        /// </summary>
        public void Resume()
        {
            TimeSpan timeLeft = endTime - DateTime.Now;
            if (timeLeft < TimeSpan.Zero)
                timeLeft = TimeSpan.Zero;

            this.Start(timeLeft);
        }
    }
}
