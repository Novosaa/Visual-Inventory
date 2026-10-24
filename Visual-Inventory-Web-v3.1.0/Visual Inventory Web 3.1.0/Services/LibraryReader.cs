using VisualInventory.Models;

namespace VisualInventory.Services;

/// <summary>
/// Keeps the last reading of Library for a short while so every page click doesn't re-read
/// the whole bucket. Refresh reads it again straight away. Only ever reads.
/// </summary>
public sealed class LibraryReader(AppSettings settings)
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(20);
    private readonly ProjectScanner _scanner = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WorkspaceSnapshot? _last;

    public bool LibraryExists => Directory.Exists(settings.LibraryPath);

    public WorkspaceSnapshot? Read(bool fresh)
    {
        if (!LibraryExists) return null;
        var last = _last;
        if (!fresh && last is not null && DateTime.UtcNow - last.ReadAtUtc < KeepFor) return last;

        _gate.Wait();
        try
        {
            last = _last;
            if (!fresh && last is not null && DateTime.UtcNow - last.ReadAtUtc < KeepFor) return last;
            _last = _scanner.Scan(settings.LibraryPath);
            return _last;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }
}
