using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VisualInventory.Models;

/// <summary>Everything read from the Library folder at one moment. Nothing here is ever saved.</summary>
public sealed class WorkspaceSnapshot
{
    public required string LibraryPath { get; init; }
    public DateTime ReadAt { get; init; } = DateTime.Now;
    public List<WorkspaceItem> Items { get; } = [];
    public List<SectionSummary> Sections { get; } = [];
}

/// <summary>One category: a folder inside Library (or "Unsorted" for loose files).</summary>
public sealed class SectionSummary
{
    public required string Name { get; init; }
    public required string Accent { get; init; }
    public int ItemCount { get; init; }
    /// <summary>True for the "Unsorted" group of loose files at the top of Library (not a real folder).</summary>
    public bool IsLooseFiles { get; init; }
    /// <summary>True for the category you're looking at, so the sidebar can highlight it.</summary>
    public bool IsCurrent { get; init; }
    public string CountText => $"{ItemCount} item{(ItemCount == 1 ? string.Empty : "s")}";

    public SectionSummary WithCurrent(bool isCurrent) => new()
    {
        Name = Name, Accent = Accent, ItemCount = ItemCount, IsLooseFiles = IsLooseFiles, IsCurrent = isCurrent
    };
}

/// <summary>One file or folder shown as a tile.</summary>
public sealed class WorkspaceItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Section { get; init; }
    public required string Title { get; init; }
    public required string PrimaryPath { get; init; }
    public required string RelativePath { get; init; }
    public string LibraryLocation => "Library" + Path.DirectorySeparatorChar + RelativePath;
    public string FileName { get; init; } = string.Empty;
    public string Kind { get; init; } = "File";
    /// <summary>Short label shown when there is no picture, e.g. "PDF", "HTML", "FOLDER".</summary>
    public string TypeLabel { get; init; } = "FILE";
    public string? PreviewPath { get; init; }
    public bool HasPreview => PreviewPath is not null;
    public bool IsDirectory { get; init; }
    public int SourceFileCount { get; init; } = 1;

    public string SubText => IsDirectory
        ? $"Folder · {SourceFileCount} file{(SourceFileCount == 1 ? string.Empty : "s")}"
        : Kind;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
