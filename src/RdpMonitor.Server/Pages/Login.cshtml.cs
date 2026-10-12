using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using RdpMonitor.Server.Services;

namespace RdpMonitor.Server.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly AdminOptions _admin;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(IOptions<AdminOptions> admin, ILogger<LoginModel> logger)
    {
        _admin = admin.Value;
        _logger = logger;
    }

    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }

    /// <summary>True while Admin:Password or Admin:Username still has the placeholder from appsettings.json (public on GitHub).</summary>
    public bool DefaultCredentials => SecretPolicy.IsPlaceholder(_admin.Password) || SecretPolicy.IsPlaceholder(_admin.Username);

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var ip = HttpContext.Connection.RemoteIpAddress;

        // Fail closed: the default password is public, so it must never open the dashboard.
        if (DefaultCredentials)
        {
            _logger.LogError("Dashboard login refused: Admin:Username/Admin:Password in appsettings.json still has the default value. Set your own and restart the service.");
            ModelState.AddModelError("", "Sign-in is disabled until the administrator sets a real Admin:Password in appsettings.json.");
            return Page();
        }

        // Both comparisons always run, in constant time, so neither the user name nor the password can be guessed from timing.
        var userOk = SecretCompare.Matches(Username, _admin.Username);
        var passOk = SecretCompare.Matches(Password, _admin.Password);
        if (userOk && passOk)
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, _admin.Username) };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false });

            _logger.LogInformation("Dashboard sign-in succeeded from {Ip}", ip);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);
            return RedirectToPage("/Index");
        }

        _logger.LogWarning("Dashboard sign-in FAILED for user '{User}' from {Ip}", InputSanitizer.Line(Username, 64), ip);
        ModelState.AddModelError("", "Invalid credentials");
        return Page();
    }
}
