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
        var handler = new HttpClientHandler();
        if (options.AllowInsecureTls)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
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
