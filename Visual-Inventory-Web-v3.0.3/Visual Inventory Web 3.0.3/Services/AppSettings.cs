namespace VisualInventory.Services;

/// <summary>
/// The app's own settings. They live in appsettings.json in the app folder, so nothing
/// has to be typed in when it goes online. (Cloud Run can override any of them, but you
/// don't need to.)
///   LibraryPath  - where the Library folder is. Online that's the storage bucket, connected
///                  read-only at /library.
///   ProjectName  - the name shown at the top of the sidebar.
///   Brand        - "SideEye" shows the Side Eye Series logo; anything else shows the pink badge.
///   TimeZone     - the time zone dates are shown in.
/// </summary>
public sealed class AppSettings
{
    public string LibraryPath { get; init; } = "/library";
    public string ProjectName { get; init; } = "Visual Inventory";
    public string Brand { get; init; } = "Generic";
    public string TimeZone { get; init; } = "Australia/Sydney";

    public bool IsSideEye => Brand.Equals("SideEye", StringComparison.OrdinalIgnoreCase);

    private TimeZoneInfo? _zone;
    public TimeZoneInfo Zone
    {
        get
        {
            if (_zone is not null) return _zone;
            try { _zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
            catch (Exception) { _zone = TimeZoneInfo.Utc; }
            return _zone;
        }
    }

    public string FormatDate(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone)
            .ToString("d MMM yyyy, h:mm tt", System.Globalization.CultureInfo.GetCultureInfo("en-AU"));
}

public static class PathRules
{
    public static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }

    /// <summary>
    /// Turns a path inside Library (as used in the page's addresses) into a real path, or null if it
    /// would land outside Library, goes through a link, or doesn't exist. This is what stops anyone
    /// asking for files outside the Library folder.
    /// </summary>
    public static string? ResolveInside(string libraryPath, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        if (relative.Contains('\0') || relative.Contains('\\')) return null;
        var library = Path.GetFullPath(libraryPath).TrimEnd('/') + "/";
        string full;
        try { full = Path.GetFullPath(Path.Combine(library, relative)); }
        catch (Exception) { return null; }
        if (!full.StartsWith(library, StringComparison.Ordinal)) return null;

        // No part of the path may be a link (a shortcut to somewhere else).
        var current = library.TrimEnd('/');
        foreach (var part in full[library.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith('.')) return null;
            current = current + "/" + part;
            if (!File.Exists(current) && !Directory.Exists(current)) return null;
            if (IsLink(current)) return null;
        }
        return full;
    }
}
