using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SocketHttpListener.Test
{
    internal static class Utility
    {        
        internal const string SITE_URL = "localhost:12345/Testing/";
        internal const string TEXT_TO_WRITE = "TESTING12345";

        private const string CERTIFICATE_RESOURCE_NAME = "SocketHttpListener.Test.localhost.pfx";

        internal static string GetCertificateFilePath()
        {
            string pfxLocation = Path.GetTempFileName();
            using (var fileStream = File.OpenWrite(pfxLocation))
            {
                Assembly.GetExecutingAssembly().GetManifestResourceStream(CERTIFICATE_RESOURCE_NAME).CopyTo(fileStream);
            }

            return pfxLocation;
        }
    }
}
