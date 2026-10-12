using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RdpMonitor.Shared;

namespace RdpMonitor.Server.Services;

/// <summary>
/// Ensures a TLS certificate exists for Kestrel to use. If the admin hasn't supplied a real
/// certificate (Kestrel:Endpoints:Https:Certificate in appsettings.json), a self-signed one is
/// generated once and reused on every startup, so the server works out of the box.
/// Agents should PIN this certificate (Agent:ServerCertificateSha256, the value is written to
/// server-cert-sha256.txt) instead of switching certificate validation off - see INSTALL.md.
/// </summary>
public static class CertificateHelper
{
    public static X509Certificate2 EnsureSelfSignedCertificate()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RdpMonitor");
        Directory.CreateDirectory(dir);
        var pfxPath = Path.Combine(dir, "server-cert.pfx");
        var passwordPath = Path.Combine(dir, "server-cert.pwd");
        var publicCerPath = Path.Combine(dir, "server-cert-public.cer");
        var thumbprintPath = Path.Combine(dir, "server-cert-sha256.txt");

        X509Certificate2 cert;
        if (File.Exists(pfxPath) && File.Exists(passwordPath))
        {
            var existingPassword = File.ReadAllText(passwordPath);
            cert = new X509Certificate2(pfxPath, existingPassword, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        }
        else
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // Server Authentication

            var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            File.WriteAllBytes(pfxPath, created.Export(X509ContentType.Pfx, password));
            File.WriteAllText(passwordPath, password);
            File.WriteAllBytes(publicCerPath, created.Export(X509ContentType.Cert));

            cert = new X509Certificate2(pfxPath, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        }

        // The private key and its password must not be readable by ordinary local users (C:\ProgramData is).
        FileProtection.Restrict(pfxPath);
        FileProtection.Restrict(passwordPath);

        // Public information: what agents need to pin this certificate.
        File.WriteAllText(thumbprintPath, cert.GetCertHashString(HashAlgorithmName.SHA256));
        return cert;
    }
}
