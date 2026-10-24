using System.Buffers.Binary;
using System.Globalization;

namespace VisualInventory.Services;

/// <summary>Size, dates and (for pictures) pixel dimensions of the selected file. Read only when you click a file.</summary>
public sealed class FileDetails
{
    public string Type { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public string Dimensions { get; init; } = string.Empty;
    public bool HasDimensions => Dimensions.Length > 0;
    public string Created { get; init; } = string.Empty;
    public string Modified { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;

    public static FileDetails? Read(string path, string type, string location, AppSettings settings)
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
                Created = settings.FormatDate(info.CreationTimeUtc),
                Modified = settings.FormatDate(info.LastWriteTimeUtc),
                Location = location
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads only the start of the picture to get its size in pixels; the image itself isn't loaded.
    /// Handles PNG, GIF, BMP and JPEG (the same picture types the app shows).
    /// </summary>
    private static string ReadDimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[32];
            if (stream.Read(head, 0, head.Length) < 26) return string.Empty;

            (int W, int H)? size = null;
            if (head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G')
                size = (BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(20)));
            else if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F')
                size = (BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(6)), BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(8)));
            else if (head[0] == 'B' && head[1] == 'M')
                size = (BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(18)), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(22))));
            else if (head[0] == 0xFF && head[1] == 0xD8)
                size = ReadJpegSize(stream);

            return size is { W: > 0, H: > 0 } s ? $"{s.W} × {s.H} px" : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static (int, int)? ReadJpegSize(Stream stream)
    {
        stream.Position = 2;
        var buffer = new byte[7];
        for (var guard = 0; guard < 2000; guard++)
        {
            int b;
            do { b = stream.ReadByte(); } while (b == 0xFF);
            if (b < 0) return null;
            var marker = b;
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
            if (stream.Read(buffer, 0, 2) < 2) return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(buffer);
            if (length < 2) return null;
            var isFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isFrame)
            {
                if (stream.Read(buffer, 0, 5) < 5) return null;
                return (BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(3)), BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(1)));
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        return null;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        var culture = CultureInfo.GetCultureInfo("en-AU");
        return unit == 0 ? $"{bytes.ToString("N0", culture)} bytes" : $"{size.ToString(size < 10 ? "0.0" : "0", culture)} {units[unit]}";
    }
}
