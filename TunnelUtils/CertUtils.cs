using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TunnelUtils
{
    public static class CertUtils
    {

        public static X509Certificate2 BuildSelfSignedServerCertificate(string certificateName)
        {
            X500DistinguishedName distinguishedName = new X500DistinguishedName($"CN={certificateName}, OU=R&D, O=OrgName, L=Locality, C=CountryName");
           
            using (RSA rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                byte[] keyBytes = new byte[20];
                using (var rng = RandomNumberGenerator.Create())
                {

                    rng.GetBytes(keyBytes);
                }

                var skiExtension = new X509SubjectKeyIdentifierExtension(keyBytes, false);


                request.CertificateExtensions.Add(skiExtension);

                var certificate = request.CreateSelfSigned(new DateTimeOffset(DateTime.UtcNow.AddDays(-1)), new DateTimeOffset(DateTime.UtcNow.AddDays(365)));
                certificate.FriendlyName = certificateName;

                return new X509Certificate2(certificate.Export(X509ContentType.Pfx, "GenNewSismaForMoreSecurity"), "GenNewSismaForMoreSecurity", X509KeyStorageFlags.MachineKeySet);
            }
        }
    }
}
