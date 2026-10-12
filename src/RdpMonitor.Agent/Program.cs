using System.Security.Cryptography;
using RdpMonitor.Agent;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

builder.Services.AddSingleton<AgentIdentityStore>();
builder.Services.AddSingleton<LocalEventQueue>();
builder.Services.AddSingleton<RdpSessionMonitor>();
builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection(AuditOptions.SectionName));
builder.Services.AddSingleton<AuditMonitor>();

builder.Services.AddHttpClient<ServerApiClient>()
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Tls");
        var handler = new HttpClientHandler();

        var pin = (options.ServerCertificateSha256 ?? "").Replace(":", "").Replace(" ", "").Trim();
        if (pin.Length > 0)
        {
            // Certificate pinning: only the certificate with this fingerprint is accepted (works for self-signed certificates).
            handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert is not null &&
                string.Equals(Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)), pin, StringComparison.OrdinalIgnoreCase);
            log.LogInformation("TLS: the server certificate is pinned (SHA-256 {Pin}...)", pin.Length > 12 ? pin[..12] : pin);
        }
        else if (options.AllowInsecureTls)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            log.LogWarning("SECURITY: Agent:AllowInsecureTls is on - the server certificate is NOT checked, so a man-in-the-middle could read the API key and inject events. " +
                           "Set Agent:ServerCertificateSha256 to pin the server certificate instead.");
        }
        return handler;
    });

builder.Services.AddHostedService<Worker>();

builder.Logging.AddEventLog(settings => settings.SourceName = "RdpMonitor Agent");

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "RdpMonitorAgent";
});

var host = builder.Build();
host.Run();
