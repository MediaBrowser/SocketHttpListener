using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Patterns.Logging;

namespace SocketHttpListener.Net
{
    // FIXME: Does this buffer the response until Close?
    // Update: we send a single packet for the first non-chunked Write
    // What happens when we set content-length to X and write X-1 bytes then close?
    // what if we don't set content-length at all?
    class ResponseStream : Stream
    {
        HttpListenerResponse response;
        bool ignore_errors;
        bool disposed;
        bool trailer_sent;
        Stream stream;

        internal ResponseStream(Stream stream, HttpListenerResponse response, bool ignore_errors)
        {
            this.response = response;
            this.ignore_errors = ignore_errors;
            this.stream = stream;
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }


        protected override void Dispose(bool disposing)
        {
            if (disposed == false)
            {
                disposed = true;
                byte[] bytes = null;
                MemoryStream ms = GetHeaders(true);
                bool chunked = response.SendChunked;
                if (stream.CanWrite)
                {
                    try
                    {
                        if (ms != null)
                        {
                            long start = ms.Position;
                            if (chunked && !trailer_sent)
                            {
                                bytes = GetChunkSizeBytes(0, true);
                                ms.Position = ms.Length;
                                ms.Write(bytes, 0, bytes.Length);
                            }
                            InternalWrite(ms.ToArray(), (int)start, (int)(ms.Length - start));
                            trailer_sent = true;
                        }
                        else if (chunked && !trailer_sent)
                        {
                            bytes = GetChunkSizeBytes(0, true);
                            InternalWrite(bytes, 0, bytes.Length);
                            trailer_sent = true;
                        }
                    }
                    catch (IOException ex)
                    {
                        // Ignore error due to connection reset by peer
                    }
                }
                response.Close();
            }

            base.Dispose(disposing);
        }

        MemoryStream GetHeaders(bool closing)
        {
            // SendHeaders works on shared headers
            lock (response.headers_lock)
            {
                if (response.HeadersSent)
                    return null;
                MemoryStream ms = new MemoryStream();
                response.SendHeaders(closing, ms);
                return ms;
            }
        }

        public override void Flush()
        {
        }

        static byte[] crlf = new byte[] { 13, 10 };
        static byte[] GetChunkSizeBytes(int size, bool final)
        {
            string str = String.Format("{0:x}\r\n{1}", size, final ? "\r\n" : "");
            return Encoding.ASCII.GetBytes(str);
        }

        internal void InternalWrite(byte[] buffer, int offset, int count)
        {
            if (ignore_errors)
            {
                try
                {
                    stream.Write(buffer, offset, count);
                }
                catch { }
            }
            else {
                stream.Write(buffer, offset, count);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().ToString());

            byte[] bytes = null;
            MemoryStream ms = GetHeaders(false);
            bool chunked = response.SendChunked;
            if (ms != null)
            {
                long start = ms.Position; // After the possible preamble for the encoding
                ms.Position = ms.Length;
                if (chunked)
                {
                    bytes = GetChunkSizeBytes(count, false);
                    ms.Write(bytes, 0, bytes.Length);
                }

                int new_count = Math.Min(count, 16384 - (int)ms.Position + (int)start);
                ms.Write(buffer, offset, new_count);
                count -= new_count;
                offset += new_count;
                InternalWrite(ms.ToArray(), (int)start, (int)(ms.Length - start));
                ms.SetLength(0);
                ms.Capacity = 0; // 'dispose' the buffer in ms.
            }
            else if (chunked)
            {
                bytes = GetChunkSizeBytes(count, false);
                InternalWrite(bytes, 0, bytes.Length);
            }

            if (count > 0)
                InternalWrite(buffer, offset, count);
            if (chunked)
                InternalWrite(crlf, 0, 2);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().ToString());

            byte[] bytes = null;
            MemoryStream ms = GetHeaders(false);
            bool chunked = response.SendChunked;
            if (ms != null)
            {
                long start = ms.Position;
                ms.Position = ms.Length;
                if (chunked)
                {
                    bytes = GetChunkSizeBytes(count, false);
                    ms.Write(bytes, 0, bytes.Length);
                }
                ms.Write(buffer, offset, count);
                buffer = ms.ToArray();
                offset = (int)start;
                count = (int)(ms.Position - start);
            }
            else if (chunked)
            {
                bytes = GetChunkSizeBytes(count, false);
                InternalWrite(bytes, 0, bytes.Length);
            }

            try
            {
                if (count > 0)
                {
                    await stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                }

                if (response.SendChunked)
                    stream.Write(crlf, 0, 2);
            }
            catch
            {
                if (!ignore_errors)
                {
                    throw;
                }
            }
        }

        //public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count,
        //                    AsyncCallback cback, object state)
        //{
        //    if (disposed)
        //        throw new ObjectDisposedException(GetType().ToString());

        //    byte[] bytes = null;
        //    MemoryStream ms = GetHeaders(false);
        //    bool chunked = response.SendChunked;
        //    if (ms != null)
        //    {
        //        long start = ms.Position;
        //        ms.Position = ms.Length;
        //        if (chunked)
        //        {
        //            bytes = GetChunkSizeBytes(count, false);
        //            ms.Write(bytes, 0, bytes.Length);
        //        }
        //        ms.Write(buffer, offset, count);
        //        buffer = ms.ToArray();
        //        offset = (int)start;
        //        count = (int)(ms.Position - start);
        //    }
        //    else if (chunked)
        //    {
        //        bytes = GetChunkSizeBytes(count, false);
        //        InternalWrite(bytes, 0, bytes.Length);
        //    }

        //    return stream.BeginWrite(buffer, offset, count, cback, state);
        //}

        //public override void EndWrite(IAsyncResult ares)
        //{
        //    if (disposed)
        //        throw new ObjectDisposedException(GetType().ToString());

        //    if (ignore_errors)
        //    {
        //        try
        //        {
        //            stream.EndWrite(ares);
        //            if (response.SendChunked)
        //                stream.Write(crlf, 0, 2);
        //        }
        //        catch { }
        //    }
        //    else {
        //        stream.EndWrite(ares);
        //        if (response.SendChunked)
        //            stream.Write(crlf, 0, 2);
        //    }
        //}

        public override int Read([In, Out] byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        //public override IAsyncResult BeginRead(byte[] buffer, int offset, int count,
        //                    AsyncCallback cback, object state)
        //{
        //    throw new NotSupportedException();
        //}

        //public override int EndRead(IAsyncResult ares)
        //{
        //    throw new NotSupportedException();
        //}

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
