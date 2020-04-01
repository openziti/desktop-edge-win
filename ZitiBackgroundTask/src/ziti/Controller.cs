using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


using Newtonsoft.Json;


using bc = Org.BouncyCastle;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Pkcs;

namespace NetFoundry.VPN.Ziti
{
    internal class Controller
    {
        private static HttpClient httpClient = null;
        static string URL = "https://demo.ziti.netfoundry.io:1080/";

        internal static void GetServices()
        {
            string json = @"";
            bc.X509.X509Certificate x509Cert = null;
            bc.X509.X509Certificate x509CaCert = null;
            AsymmetricCipherKeyPair ackp = null;

            dynamic thing = JsonConvert.DeserializeObject(json);
            string key = CleanPkiNode(thing.id.key?.ToString());
            string cert = CleanPkiNode(thing.id.cert?.ToString());
            string ca = CleanPkiNode(thing.id.ca?.ToString());

            if (!string.IsNullOrEmpty(key))
            {
                using (var keyReader = new StringReader(key))
                {
                    ackp = (AsymmetricCipherKeyPair)new PemReader(keyReader).ReadObject();
                }
            }

            if (!string.IsNullOrEmpty(cert))
            {
                using (var certReader = new StringReader(cert))
                {
                    x509Cert = (bc.X509.X509Certificate)new PemReader(certReader).ReadObject();
                }
            }

            if (!string.IsNullOrEmpty(ca))
            {
                using (var caReader = new StringReader(ca))
                {
                    x509CaCert = (bc.X509.X509Certificate)new PemReader(caReader).ReadObject();
                }
            }

            var csharpCert = CreateDotNetCertificate(x509Cert, ackp);

            var cch = new HttpClientHandler();

            cch.ClientCertificates.Add(csharpCert);
            cch.ClientCertificateOptions = ClientCertificateOption.Manual;
            httpClient = new HttpClient(new LoggingHandler(cch))
            {
                BaseAddress = new Uri($"{URL}")
            };
        }
        internal static X509Certificate createCertificate()
        {
            return null;
        }


        static string CleanPkiNode(string source)
        {
            if (source == null) return null;
            if (source.StartsWith("pem:"))
            {
                return source.Substring("pem:".Length);
            }
            else if (source.StartsWith("file://"))
            {
                throw new Exception("File pki not supported currently");
            }
            else
            {
                throw new Exception("Unexpected pki?");
            }
        }
        private static X509Certificate CreateDotNetCertificate(Org.BouncyCastle.X509.X509Certificate certificate,
            AsymmetricCipherKeyPair keyPair)
        {
            Pkcs12Store store = new Pkcs12Store();
            string friendlyName = new Guid().ToString();
            X509CertificateEntry certificateEntry = new X509CertificateEntry(certificate);
            store.SetCertificateEntry(friendlyName, certificateEntry);
            store.SetKeyEntry(friendlyName, new AsymmetricKeyEntry(keyPair.Private), new[] { certificateEntry });

            MemoryStream stream = new MemoryStream();
            store.Save(stream, null, new SecureRandom());

            return new X509Certificate2(stream.ToArray());
        }
    }

    internal class LoggingHandler : DelegatingHandler
    {
        public LoggingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.WriteLine($"Request: {request}");
            try
            {
                // base.SendAsync calls the inner handler
                var response = await base.SendAsync(request, cancellationToken);
                System.Diagnostics.Debug.WriteLine($"Response: {response}");
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get response: {ex}");
                throw;
            }
        }
    }

}
