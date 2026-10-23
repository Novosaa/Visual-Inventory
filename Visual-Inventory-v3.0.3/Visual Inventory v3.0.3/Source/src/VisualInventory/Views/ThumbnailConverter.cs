using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace VisualInventory.Views;

/// <summary>
/// Loads a picture at display size, fully into memory, then lets go of the file.
/// The app reads the image itself; nothing is handed to Windows or other programs.
/// Because the file isn't held open, moving or renaming it afterwards works normally.
/// </summary>
public sealed class ThumbnailConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> CacheOrder = new();
    private const int CacheLimit = 600;

    public int DecodeWidth { get; set; } = 440;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            var key = $"{DecodeWidth}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{path}";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var bitmap = new BitmapImage();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = DecodeWidth;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();

            Cache[key] = bitmap;
            CacheOrder.Enqueue(key);
            while (CacheOrder.Count > CacheLimit) Cache.Remove(CacheOrder.Dequeue());
            return bitmap;
        }
        catch (Exception)
        {
            // Damaged or unsupported image: show the file-type label instead.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
