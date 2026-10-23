using Microsoft.AspNetCore.StaticFiles;
using VisualInventory.Services;

// Visual Inventory (web) - view only.
// It shows what is in the Library folder (online: your storage bucket, connected read-only).
// It never writes, moves, renames, uploads or deletes anything, and it has no sign-in,
// no cookies and nothing it remembers about visitors.

var builder = WebApplication.CreateBuilder(args);

// Cloud Run tells the app which port to listen on.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

var settings = builder.Configuration.GetSection("VisualInventory").Get<AppSettings>() ?? new AppSettings();
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<LibraryReader>();
builder.Services.AddRazorPages(o => o.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute()));

var app = builder.Build();

// Only looking is allowed: anything other than GET/HEAD is refused. Plus browser safety rules.
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["Content-Security-Policy"] = "default-src 'none'; img-src 'self'; connect-src 'self'; style-src 'self'; script-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["X-Frame-Options"] = "DENY";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    h["Strict-Transport-Security"] = "max-age=31536000";
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
    }
    await next();
});

app.UseStaticFiles();
app.MapRazorPages();

// Pictures for the tiles and the details panel. Only picture files inside Library are ever sent.
var types = new FileExtensionContentTypeProvider();
app.MapGet("/picture", (string? path, LibraryReader reader) =>
{
    var full = PathRules.ResolveInside(settings.LibraryPath, path);
    if (full is null || !File.Exists(full) || !ProjectScanner.IsPreviewImage(full)) return Results.NotFound();
    if (!types.TryGetContentType(full, out var contentType)) return Results.NotFound();
    var info = new FileInfo(full);
    return Results.File(full, contentType, lastModified: info.LastWriteTimeUtc, enableRangeProcessing: false);
});

app.Run();
