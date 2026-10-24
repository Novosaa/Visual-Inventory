using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisualInventory.Services;

namespace VisualInventory.Pages;

/// <summary>
/// The sign-in page. Checks the username and password you set in Cloud Run and, if they match,
/// gives your browser the sign-in cookie and goes to the Overview. Nothing else happens here.
/// </summary>
public sealed class LoginModel(SignIn signIn, SignInLimiter limiter, AppSettings settings) : PageModel
{
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty(SupportsGet = true, Name = "out")] public string? SignedOut { get; set; }

    public AppSettings Settings => settings;
    public bool IsSetUp => signIn.IsSetUp;
    public string? Message { get; private set; }

    public IActionResult OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        if (signIn.IsSignedIn(HttpContext)) return Redirect("/");
        if (SignedOut is not null) Message = "You're signed out.";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Response.Headers.CacheControl = "no-store";
        var client = SignInLimiter.ClientOf(HttpContext);
        if (!signIn.IsSetUp)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Page();
        }
        if (limiter.IsLockedOut(client))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Message = "Too many wrong tries. Wait 15 minutes, then try again.";
            return Page();
        }
        if (!signIn.CredentialsMatch(Username, Password))
        {
            limiter.Wrong(client);
            await Task.Delay(TimeSpan.FromSeconds(1));
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Message = "That username and password don't match.";
            Password = null;
            return Page();
        }
        limiter.Right(client);
        signIn.GiveCookie(HttpContext);
        return Redirect("/");
    }
}
