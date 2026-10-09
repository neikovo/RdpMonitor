using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RdpMonitor.Server.Services;

/// <summary>
/// Ensures a TLS certificate exists for Kestrel to use. If the admin hasn't supplied a real
/// certificate (Kestrel:Endpoints:Https:Certificate in appsettings.json), a self-signed one is
/// generated once and reused on every startup, so the server works out of the box.
/// Agents talking to a self-signed server need Agent:AllowInsecureTls = true, or the exported
/// public certificate installed as trusted — see README.
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

        if (File.Exists(pfxPath) && File.Exists(passwordPath))
        {
            var existingPassword = File.ReadAllText(passwordPath);
            return new X509Certificate2(pfxPath, existingPassword, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={Environment.MachineName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // Server Authentication

        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfxBytes);
        File.WriteAllText(passwordPath, password);
        File.WriteAllBytes(publicCerPath, cert.Export(X509ContentType.Cert));

        return new X509Certificate2(pfxPath, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
    }
}
