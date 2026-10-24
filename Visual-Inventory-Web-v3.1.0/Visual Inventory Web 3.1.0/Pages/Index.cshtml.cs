using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VisualInventory.Models;
using VisualInventory.Services;

namespace VisualInventory.Pages;

/// <summary>
/// The one page of the app: Overview when no category is chosen, otherwise the category
/// with its tiles and the details panel. Same as the desktop window, as a web page.
/// </summary>
public sealed class IndexModel(LibraryReader reader, AppSettings settings) : PageModel
{
    public const string OverviewName = "Overview";

    [BindProperty(SupportsGet = true, Name = "c")] public string? Category { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Query { get; set; }
    [BindProperty(SupportsGet = true, Name = "item")] public string? ItemPath { get; set; }
    [BindProperty(SupportsGet = true, Name = "refresh")] public string? Refresh { get; set; }

    public AppSettings Settings => settings;
    public WorkspaceSnapshot? Snapshot { get; private set; }
    public SectionSummary? Current { get; private set; }
    public List<WorkspaceItem> Visible { get; private set; } = [];
    public WorkspaceItem? Selected { get; private set; }
    public FileDetails? SelectedDetails { get; private set; }
    public bool MissingLibrary { get; private set; }
    public bool IsOverview => Current is null;

    public string PageTitle { get; private set; } = string.Empty;
    public string PageSubtitle { get; private set; } = string.Empty;
    public string LastReadText => Snapshot is null ? string.Empty : "Read " + settings.FormatDate(Snapshot.ReadAtUtc);
    public string VisibleCountText => Visible.Count == 1 ? "1 shown" : $"{Visible.Count} shown";

    public IActionResult OnGet()
    {
        Snapshot = reader.Read(fresh: Refresh is not null);
        if (Snapshot is null)
        {
            MissingLibrary = true;
            PageTitle = settings.ProjectName;
            PageSubtitle = "No Library folder found";
            return Page();
        }

        Current = string.IsNullOrEmpty(Category) || Category == OverviewName
            ? null
            : Snapshot.Sections.FirstOrDefault(s => s.Name.Equals(Category, StringComparison.OrdinalIgnoreCase));

        if (Current is null)
        {
            PageTitle = settings.ProjectName;
            PageSubtitle = $"{Snapshot.Items.Count} items in {Snapshot.Sections.Count} categories";
            return Page();
        }

        PageTitle = Current.Name;
        PageSubtitle = Current.IsLooseFiles ? "Loose files at the top of the Library folder" : $"Library/{Current.Name}";

        var query = (Query ?? string.Empty).Trim();
        Visible = Snapshot.Items
            .Where(item => item.Section.Equals(Current.Name, StringComparison.OrdinalIgnoreCase))
            .Where(item => query.Length == 0
                           || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.TypeLabel.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Like the desktop app: the first tile is selected unless you've clicked another.
        Selected = Visible.FirstOrDefault(i => i.RelativePath.Equals(ItemPath, StringComparison.Ordinal)) ?? Visible.FirstOrDefault();
        if (Selected is { IsDirectory: false })
            SelectedDetails = FileDetails.Read(Selected.PrimaryPath, Selected.Kind, Selected.LibraryLocation, settings);
        return Page();
    }

    public string CategoryUrl(string name) => Url.Page("/Index", new { c = name })!;
    public string OverviewUrl() => Url.Page("/Index")!;
    public string RefreshUrl() => IsOverview
        ? Url.Page("/Index", new { refresh = 1 })!
        : Url.Page("/Index", new { c = Current!.Name, q = string.IsNullOrWhiteSpace(Query) ? null : Query, item = Selected?.RelativePath, refresh = 1 })!;
    public string ItemUrl(WorkspaceItem item) =>
        Url.Page("/Index", new { c = Current!.Name, q = string.IsNullOrWhiteSpace(Query) ? null : Query, item = item.RelativePath })!;
    public static string PictureUrl(string relative) => "/picture?path=" + Uri.EscapeDataString(relative);
}
