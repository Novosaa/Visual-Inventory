using Microsoft.AspNetCore.StaticFiles;
using VisualInventory.Services;

// Visual Inventory (web) - view only, behind a sign-in page.
// It shows what is in the Library folder (online: your storage bucket, connected read-only).
// It never writes, moves, renames, uploads or deletes anything. Nothing can be seen until you
// sign in with the one username and password you set. The only cookie is the sign-in itself.

var builder = WebApplication.CreateBuilder(args);

// Cloud Run tells the app which port to listen on.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

var settings = builder.Configuration.GetSection("VisualInventory").Get<AppSettings>() ?? new AppSettings();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<LibraryReader>();
builder.Services.AddSingleton<SignIn>();
builder.Services.AddSingleton<SignInLimiter>();
builder.Services.AddRazorPages(o => o.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute()));

var app = builder.Build();
var signIn = app.Services.GetRequiredService<SignIn>();
if (!signIn.IsSetUp)
    app.Logger.LogWarning("Sign-in is not set up: set VisualInventory__Username and VisualInventory__Password (at least {Min} characters). Nobody can sign in until then.", SignIn.MinimumPasswordLength);

// Only looking is allowed: GET/HEAD, plus sending the sign-in form and signing out.
// Those two are refused if they come from another website. Plus browser safety rules.
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["Content-Security-Policy"] = "default-src 'none'; img-src 'self'; connect-src 'self'; style-src 'self'; script-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["X-Frame-Options"] = "DENY";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    h["Strict-Transport-Security"] = "max-age=31536000";

    var method = context.Request.Method;
    var path = context.Request.Path;
    if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) { await next(); return; }
    var isForm = HttpMethods.IsPost(method) && (path.Equals("/login", StringComparison.OrdinalIgnoreCase) || path.Equals("/logout", StringComparison.OrdinalIgnoreCase));
    if (!isForm) { context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed; return; }
    if (!FromThisSite(context)) { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
    await next();
});

// The look (css, script, icons, logo) is the same for everyone, so it's shown before sign-in.
app.UseStaticFiles();

// Everything else needs you to be signed in. Not signed in: pages go to the sign-in page,
// anything else (pictures) is refused.
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/login", StringComparison.OrdinalIgnoreCase)) { await next(); return; }
    if (!signIn.IsSignedIn(context))
    {
        if (context.Request.Path.Equals("/", StringComparison.Ordinal)) context.Response.Redirect("/login");
        else context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    // Pages aren't kept by the browser, so pressing Back after signing out shows nothing.
    if (!context.Request.Path.Equals("/picture", StringComparison.OrdinalIgnoreCase))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});

app.MapRazorPages();

app.MapPost("/logout", (HttpContext context) =>
{
    signIn.TakeCookie(context);
    return Results.Redirect("/login?out=1");
});

// Pictures for the tiles and the details panel. Only picture files inside Library are ever sent.
var types = new FileExtensionContentTypeProvider();
app.MapGet("/picture", (string? path, HttpContext context) =>
{
    var full = PathRules.ResolveInside(settings.LibraryPath, path);
    if (full is null || !File.Exists(full) || !ProjectScanner.IsPreviewImage(full)) return Results.NotFound();
    if (!types.TryGetContentType(full, out var contentType)) return Results.NotFound();
    var info = new FileInfo(full);
    // "private": only your own browser may keep a copy, never anything in between.
    context.Response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(full, contentType, lastModified: info.LastWriteTimeUtc, enableRangeProcessing: false);
});

app.Run();

// A form sent from this site's own page says so (Origin / Sec-Fetch-Site). One from anywhere else is refused.
static bool FromThisSite(HttpContext context)
{
    var site = context.Request.Headers["Sec-Fetch-Site"].ToString();
    if (site.Length > 0 && site != "same-origin") return false;
    var origin = context.Request.Headers.Origin.ToString();
    if (origin.Length == 0) return true;
    return Uri.TryCreate(origin, UriKind.Absolute, out var o)
           && string.Equals(o.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
}
