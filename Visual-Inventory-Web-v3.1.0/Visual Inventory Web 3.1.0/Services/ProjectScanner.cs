using System.Text.RegularExpressions;
using VisualInventory.Models;

namespace VisualInventory.Services;

/// <summary>
/// Reads the Library folder. Every direct folder is a category; every direct file or
/// folder inside a category is one item. Loose files at the top of Library appear in a
/// "Unsorted" group. It only looks: nothing is created, changed, moved or opened.
/// (Same rules as the desktop app 3.0.3.)
/// </summary>
public sealed class ProjectScanner
{
    public const string UnsortedName = "Unsorted";
    public static readonly string[] PreviewExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif"];
    private static readonly string[] Accents = ["#E85D91", "#4CB7BF", "#8CBF67", "#E9C84A", "#D9845F", "#9B83C9", "#62AC91", "#648FCA", "#C982A2"];

    public WorkspaceSnapshot Scan(string libraryPath)
    {
        var library = Path.GetFullPath(libraryPath);
        if (!Directory.Exists(library)) throw new DirectoryNotFoundException("The Library folder was not found.");

        var snapshot = new WorkspaceSnapshot { LibraryPath = library };

        var categories = SafeDirectories(library)
            .Where(path => !IsHiddenName(Path.GetFileName(path)))
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var categoryPath in categories)
        {
            var section = Path.GetFileName(categoryPath);
            foreach (var entry in SafeEntries(categoryPath).Where(p => !IsHiddenName(Path.GetFileName(p))))
                snapshot.Items.Add(BuildItem(library, section, entry));
        }

        foreach (var loose in SafeTopFiles(library).Where(p => !IsHiddenName(Path.GetFileName(p))))
            snapshot.Items.Add(BuildItem(library, UnsortedName, loose));

        var index = 0;
        foreach (var categoryPath in categories)
            snapshot.Sections.Add(MakeSection(snapshot, Path.GetFileName(categoryPath), looseFiles: false, index++));

        var hasRealUnsorted = categories.Any(path => Path.GetFileName(path).Equals(UnsortedName, StringComparison.OrdinalIgnoreCase));
        if (!hasRealUnsorted && snapshot.Items.Any(item => item.Section == UnsortedName))
            snapshot.Sections.Add(MakeSection(snapshot, UnsortedName, looseFiles: true, index));

        return snapshot;
    }

    private static SectionSummary MakeSection(WorkspaceSnapshot snapshot, string name, bool looseFiles, int index) => new()
    {
        Name = name,
        IsLooseFiles = looseFiles,
        Accent = Accents[index % Accents.Length],
        ItemCount = snapshot.Items.Count(item => item.Section.Equals(name, StringComparison.OrdinalIgnoreCase))
    };

    private static WorkspaceItem BuildItem(string library, string section, string path)
    {
        var isDirectory = Directory.Exists(path);
        var files = isDirectory ? SafeFiles(path) : [path];
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        var preview = isDirectory
            ? files.Where(IsPreviewImage).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault()
            : IsPreviewImage(path) ? path : null;
        var title = Humanize(isDirectory ? name : Path.GetFileNameWithoutExtension(name));

        return new WorkspaceItem
        {
            Section = section,
            Title = string.IsNullOrWhiteSpace(title) ? name : title,
            FileName = name,
            PrimaryPath = path,
            RelativePath = Relative(library, path),
            IsDirectory = isDirectory,
            Kind = isDirectory ? "Folder" : string.IsNullOrWhiteSpace(extension) ? "File" : extension.TrimStart('.').ToUpperInvariant() + " file",
            TypeLabel = isDirectory ? "FOLDER" : string.IsNullOrWhiteSpace(extension) ? "FILE" : extension.TrimStart('.').ToUpperInvariant(),
            PreviewPath = preview,
            PreviewRelativePath = preview is null ? null : Relative(library, preview),
            SourceFileCount = files.Count
        };
    }

    private static string Relative(string library, string path) =>
        Path.GetRelativePath(library, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsHiddenName(string? name) => string.IsNullOrEmpty(name) || name.StartsWith('.') || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
    public static bool IsPreviewImage(string path) => PreviewExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static List<string> SafeEntries(string folder)
    {
        try { return Directory.EnumerateFileSystemEntries(folder).Where(p => !PathRules.IsLink(p)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static List<string> SafeDirectories(string folder)
    {
        try { return Directory.EnumerateDirectories(folder).Where(p => !PathRules.IsLink(p)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static List<string> SafeTopFiles(string folder)
    {
        try { return Directory.EnumerateFiles(folder).Where(p => !PathRules.IsLink(p)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>All files under a folder, without following linked folders (shortcuts to other places).</summary>
    private static List<string> SafeFiles(string folder)
    {
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            result.AddRange(SafeTopFiles(current));
            foreach (var child in SafeDirectories(current)) pending.Push(child);
        }
        return result;
    }

    private static string Humanize(string value) => Regex.Replace(value.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();
}
