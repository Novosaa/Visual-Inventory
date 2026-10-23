namespace VisualInventory.Services;

/// <summary>
/// The only place the app looks:
///   [project]\VisualInventory.exe   (and its Runtime folder)
///   [project]\Library\              your categories and files - read only
/// The app never chooses, browses or writes to any location.
/// </summary>
public sealed class ProjectPaths
{
    public const string LibraryFolderName = "Library";

    public ProjectPaths(string projectRoot)
    {
        Root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Root.EndsWith(':')) Root += Path.DirectorySeparatorChar; // drive root such as E:\
        LibraryPath = Path.Combine(Root, LibraryFolderName);
    }

    public string Root { get; }
    public string LibraryPath { get; }
    public bool LibraryExists => Directory.Exists(LibraryPath);

    public string ProjectName
    {
        get
        {
            var name = Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? "Visual Inventory" : name;
        }
    }
}

public static class PathRules
{
    public static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }
}
