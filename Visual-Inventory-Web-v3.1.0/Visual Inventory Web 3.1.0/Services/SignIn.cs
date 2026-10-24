using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace VisualInventory.Services;

/// <summary>
/// The sign-in. One username and password, set by you in Cloud Run (never in the app's files).
/// When you sign in, your browser is given one cookie, "vi_session". It holds only the time it
/// runs out and a signature made from your password, so the app doesn't need to remember
/// anything about who is signed in, and it keeps working when Cloud Run restarts the app.
/// Changing the password (or username) signs everyone out straight away.
/// Uses only what comes with .NET itself.
/// </summary>
public sealed class SignIn(AppSettings settings)
{
    public const string CookieName = "vi_session";
    public const int MinimumPasswordLength = 12;
    public static readonly TimeSpan SessionLength = TimeSpan.FromHours(12);

    /// <summary>False until a username and a long enough password are set. Then nobody can sign in.</summary>
    public bool IsSetUp =>
        !string.IsNullOrWhiteSpace(settings.Username) &&
        (settings.Password ?? string.Empty).Length >= MinimumPasswordLength;

    public bool CredentialsMatch(string? username, string? password)
    {
        if (!IsSetUp) return false;
        // Both are always compared, in a way that takes the same time whether they match or not.
        var userOk = SameText(username ?? string.Empty, settings.Username!);
        var passOk = SameText(password ?? string.Empty, settings.Password!);
        return userOk & passOk;
    }

    public string NewSessionValue()
    {
        var expires = DateTimeOffset.UtcNow.Add(SessionLength).ToUnixTimeSeconds();
        return expires + "." + Sign(expires);
    }

    public bool IsSignedIn(HttpContext context)
    {
        if (!IsSetUp) return false;
        var value = context.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(value)) return false;
        var dot = value.IndexOf('.');
        if (dot <= 0 || !long.TryParse(value.AsSpan(0, dot), out var expires)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expires) return false;
        return SameText(value[(dot + 1)..], Sign(expires));
    }

    public void GiveCookie(HttpContext context) =>
        context.Response.Cookies.Append(CookieName, NewSessionValue(), CookieRules(context, SessionLength));

    public void TakeCookie(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, CookieRules(context, null));

    // HttpOnly: the page's own scripts can't read it. Secure: only ever sent over https.
    // SameSite=Strict: never sent when another website links or posts to this one.
    private static CookieOptions CookieRules(HttpContext context, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true,
    };

    private string Sign(long expires)
    {
        var key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.Password ?? string.Empty),
            Encoding.UTF8.GetBytes("Visual Inventory session key|" + settings.Username));
        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("v1|" + expires));
        return Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool SameText(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

/// <summary>
/// Slows down anyone guessing the password. Counts wrong tries in memory only (nothing is
/// saved). After 5 wrong tries from one connection, or 20 from everyone together, sign-in is
/// refused for 15 minutes. This is why a long password matters: even a patient guesser only
/// gets about 2,000 tries a day.
/// </summary>
public sealed class SignInLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int PerClient = 5;
    private const int Everyone = 20;
    private const string EveryoneKey = "*";
    private readonly ConcurrentDictionary<string, (int Count, DateTime Since)> _wrong = new();

    public bool IsLockedOut(string client) => Count(client) >= PerClient || Count(EveryoneKey) >= Everyone;

    public void Wrong(string client)
    {
        if (_wrong.Count > 10_000) _wrong.Clear();
        Add(client);
        Add(EveryoneKey);
    }

    public void Right(string client) => _wrong.TryRemove(client, out _);

    private int Count(string key) =>
        _wrong.TryGetValue(key, out var e) && DateTime.UtcNow - e.Since < Window ? e.Count : 0;

    private void Add(string key) =>
        _wrong.AddOrUpdate(key, _ => (1, DateTime.UtcNow),
            (_, e) => DateTime.UtcNow - e.Since < Window ? (e.Count + 1, e.Since) : (1, DateTime.UtcNow));

    /// <summary>Who's asking, as best the app can tell. Online, Google passes it on in X-Forwarded-For.</summary>
    public static string ClientOf(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        return forwarded.Length > 0 ? forwarded : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
