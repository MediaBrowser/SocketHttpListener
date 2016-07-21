using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SocketHttpListener
{
    internal abstract class HttpBase
    {
        #region Private Fields

        private NameValueCollection _headers;
        private const int _headersMaxLength = 8192;
        private Version _version;

        #endregion

        #region Internal Fields

        internal byte[] EntityBodyData;

        #endregion

        #region Protected Fields

        protected const string CrLf = "\r\n";

        #endregion

        #region Protected Constructors

        protected HttpBase(Version version, NameValueCollection headers)
        {
            _version = version;
            _headers = headers;
        }

        #endregion

        #region Public Properties

        public string EntityBody
        {
            get
            {
                return EntityBodyData != null && EntityBodyData.LongLength > 0
                       ? getEncoding(_headers["Content-Type"]).GetString(EntityBodyData)
                       : String.Empty;
            }
        }

        public NameValueCollection Headers
        {
            get
            {
                return _headers;
            }
        }

        public Version ProtocolVersion
        {
            get
            {
                return _version;
            }
        }

        #endregion

        #region Private Methods

        private static Encoding getEncoding(string contentType)
        {
            if (contentType == null || contentType.Length == 0)
                return Encoding.UTF8;

            var i = contentType.IndexOf("charset=", StringComparison.Ordinal);
            if (i == -1)
                return Encoding.UTF8;

            var charset = contentType.Substring(i + 8);
            i = charset.IndexOf(';');
            if (i != -1)
                charset = charset.Substring(0, i).TrimEnd();

            return Encoding.GetEncoding(charset.Trim('"'));
        }

        private static async Task<byte[]> ReadEntityBody(Stream stream, string length)
        {
            long len;
            if (!Int64.TryParse(length, out len))
                throw new ArgumentException("Cannot be parsed.", "length");

            if (len < 0)
                throw new ArgumentOutOfRangeException("length", "Less than zero.");

            if (len > 1024)
            {
                return await stream.ReadBytes(len, 1024).ConfigureAwait(false);
            }

            if (len > 0)
            {
                return await stream.ReadBytes((int)len).ConfigureAwait(false);
            }

            return null;
        }

        private static string[] readHeaders(Stream stream, int maxLength)
        {
            var buff = new List<byte>();
            var cnt = 0;
            Action<int> add = i =>
            {
                buff.Add((byte)i);
                cnt++;
            };

            var read = false;
            while (cnt < maxLength)
            {
                if (stream.ReadByte().EqualsWith('\r', add) &&
                    stream.ReadByte().EqualsWith('\n', add) &&
                    stream.ReadByte().EqualsWith('\r', add) &&
                    stream.ReadByte().EqualsWith('\n', add))
                {
                    read = true;
                    break;
                }
            }

            if (!read)
                throw new WebSocketException("The length of header part is greater than the max length.");

            return Encoding.UTF8.GetString(buff.ToArray())
                   .Replace(CrLf + " ", " ")
                   .Replace(CrLf + "\t", " ")
                   .Split(new[] { CrLf }, StringSplitOptions.RemoveEmptyEntries);
        }

        #endregion

        #region Public Methods

        public byte[] ToByteArray()
        {
            return Encoding.UTF8.GetBytes(ToString());
        }

        #endregion
    }
}