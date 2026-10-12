using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RdpMonitor.Server;
using RdpMonitor.Server.Data;
using RdpMonitor.Server.Endpoints;
using RdpMonitor.Server.Services;
using RdpMonitor.Shared;

var builder = WebApplication.CreateBuilder(args);

// If the admin hasn't pointed Kestrel at a real certificate, fall back to a self-signed one
// generated on first run, so HTTPS works without any manual setup.
if (string.IsNullOrEmpty(builder.Configuration["Kestrel:Endpoints:Https:Certificate:Path"]))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureHttpsDefaults(https =>
        {
            https.ServerCertificate = CertificateHelper.EnsureSelfSignedCertificate();
        });
    });
}

// Requests are small JSON/forms; refuse anything big before it is read into memory.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

builder.Services.Configure<AgentEnrollmentOptions>(builder.Configuration.GetSection(AgentEnrollmentOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<AlertOptions>(builder.Configuration.GetSection(AlertOptions.SectionName));

// Slow down password / token guessing. Limits are per client address.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = ctx.Request.Path.Value ?? "";
        var isPost = HttpMethods.IsPost(ctx.Request.Method);
        var handler = ctx.Request.Query["handler"].ToString();

        static FixedWindowRateLimiterOptions PerMinute(int permits) =>
            new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

        if (isPost && path.Equals("/Login", StringComparison.OrdinalIgnoreCase))
            return RateLimitPartition.GetFixedWindowLimiter("login:" + ip, _ => PerMinute(10));

        if (isPost && path.Equals("/api/agents/register", StringComparison.OrdinalIgnoreCase))
            return RateLimitPartition.GetFixedWindowLimiter("enroll:" + ip, _ => PerMinute(30));

        if (isPost && handler.Equals("ClearEvents", StringComparison.OrdinalIgnoreCase))
            return RateLimitPartition.GetFixedWindowLimiter("clear:" + ip, _ => PerMinute(10));

        // generous ceiling for everything else (many agents may share one NAT address)
        return RateLimitPartition.GetFixedWindowLimiter("all:" + ip, _ => PerMinute(6000));
    });
});

var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RdpMonitor");
Directory.CreateDirectory(dataDir);

// The database lives in its own folder that only SYSTEM, Administrators and the service account may open
// (C:\ProgramData\RdpMonitor itself is readable by every local user and shared with other tools).
// Files SQLite creates inside (-wal, -shm) inherit that restriction.
var dbDir = Path.Combine(dataDir, "server-data");
Directory.CreateDirectory(dbDir);
FileProtection.RestrictDirectory(dbDir);
var dbPath = Path.Combine(dbDir, "rdpmonitor.db");

// Upgrade from versions that kept the database directly in ProgramData\RdpMonitor.
var legacyDb = Path.Combine(dataDir, "rdpmonitor.db");
if (File.Exists(legacyDb) && !File.Exists(dbPath))
{
    foreach (var suffix in new[] { "", "-wal", "-shm" })
    {
        var from = legacyDb + suffix;
        if (File.Exists(from)) File.Move(from, dbPath + suffix);
    }
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<AlertService>();
builder.Services.AddSingleton<EmailService>();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
builder.Services.AddAuthorization();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "RdpMonitorServer";
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// The database holds every recorded event: keep it away from ordinary local users.
foreach (var file in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
    FileProtection.Restrict(file);

// Loud warnings (also in the Windows event log) when a shipped placeholder secret was never replaced.
{
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var admin = app.Configuration.GetSection(AdminOptions.SectionName).Get<AdminOptions>() ?? new AdminOptions();
    var enrollment = app.Configuration.GetSection(AgentEnrollmentOptions.SectionName).Get<AgentEnrollmentOptions>() ?? new AgentEnrollmentOptions();
    if (SecretPolicy.IsPlaceholder(admin.Password) || SecretPolicy.IsPlaceholder(admin.Username))
        startupLog.LogWarning("SECURITY: Admin:Username/Admin:Password still have the default value - dashboard sign-in is DISABLED until you set your own in appsettings.json.");
    if (SecretPolicy.IsPlaceholder(enrollment.EnrollmentToken))
        startupLog.LogWarning("SECURITY: Agent:EnrollmentToken still has the default value - NEW agents cannot register until you set your own in appsettings.json.");
    else if (enrollment.EnrollmentToken.Length < 16)
        startupLog.LogWarning("SECURITY: Agent:EnrollmentToken is short ({Length} characters). Use at least 16 random characters.", enrollment.EnrollmentToken.Length);
    if (!SecretPolicy.IsPlaceholder(admin.Password) && admin.Password.Length < 12)
        startupLog.LogWarning("SECURITY: Admin:Password is short ({Length} characters). Use at least 12.", admin.Password.Length);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Defensive browser headers: no framing (clickjacking), no MIME sniffing, no external resources, no referrer leaks.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    // dashboard pages contain event data: do not let browsers or proxies cache them
    if (!ctx.Request.Path.StartsWithSegments("/lib") && !ctx.Request.Path.StartsWithSegments("/css") && !ctx.Request.Path.StartsWithSegments("/js"))
        h["Cache-Control"] = "no-store";
    await next();
});

app.UseRateLimiter();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapAgentEndpoints();

app.Run();
