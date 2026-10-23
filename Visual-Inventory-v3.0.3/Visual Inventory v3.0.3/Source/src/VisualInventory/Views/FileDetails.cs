using System.Globalization;
using System.Windows.Media.Imaging;
using VisualInventory.Services;

namespace VisualInventory.Views;

/// <summary>Size, dates and (for pictures) pixel dimensions of the selected file. Read locally, only when you click a file.</summary>
public sealed class FileDetails
{
    public string Type { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public string Dimensions { get; init; } = string.Empty;
    public bool HasDimensions => Dimensions.Length > 0;
    public string Created { get; init; } = string.Empty;
    public string Modified { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;

    public static FileDetails? Read(string path, string type, string location)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            return new FileDetails
            {
                Type = type,
                Size = FormatSize(info.Length),
                Dimensions = ProjectScanner.IsPreviewImage(path) ? ReadDimensions(path) : string.Empty,
                Created = FormatDate(info.CreationTime),
                Modified = FormatDate(info.LastWriteTime),
                Location = location
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Reads only the picture's header to get its size in pixels; the image itself isn't loaded.</summary>
    private static string ReadDimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return $"{frame.PixelWidth} × {frame.PixelHeight} px";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes:N0} bytes" : $"{size.ToString(size < 10 ? "0.0" : "0", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    private static string FormatDate(DateTime value) =>
        value.ToString("d MMM yyyy, h:mm tt", CultureInfo.CurrentCulture);
}
