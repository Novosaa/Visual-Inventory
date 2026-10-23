using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VisualInventory.Models;
using VisualInventory.Services;
using VisualInventory.Views;

namespace VisualInventory;

/// <summary>
/// The window. It only shows what is in the Library folder. Nothing here writes, moves,
/// renames, copies, opens or deletes anything, and nothing is remembered between runs.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string OverviewName = "Overview";
    private const double MinTileWidth = 238;     // the tile size from v1.3.0 is the smallest a tile gets
    private const double TileGap = 16;
    private const double PictureRatio = 150.0 / 238.0;
    private const double DefaultDetailsWidth = 340;

    private readonly ProjectPaths _paths = App.Paths;
    private readonly ProjectScanner _scanner = new();
    private WorkspaceSnapshot? _snapshot;
    private WorkspaceItem? _selectedItem;
    private FileDetails? _selectedDetails;
    private string _currentSection = OverviewName;
    private bool _detailsVisible = true;
    private double _detailsWidth = DefaultDetailsWidth;

    private string _pageTitle = string.Empty;
    private string _pageSubtitle = string.Empty;
    private string _visibleCountText = string.Empty;
    private string _lastReadText = string.Empty;
    private bool _overviewIsCurrent = true;
    private double _tileWidth = MinTileWidth;

    public ObservableCollection<SectionSummary> DashboardSections { get; } = [];
    public ObservableCollection<WorkspaceItem> VisibleItems { get; } = [];

    public string ProjectName => _paths.ProjectName;
    public string WindowTitle => $"{ProjectName} — Visual Inventory";
    public string LibraryPathText => _paths.LibraryPath;

    public double TileWidth
    {
        get => _tileWidth;
        private set
        {
            if (Math.Abs(_tileWidth - value) < 0.5) return;
            _tileWidth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TileImageRow));
        }
    }

    public GridLength TileImageRow => new(Math.Round(_tileWidth * PictureRatio));

    public WorkspaceItem? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (ReferenceEquals(_selectedItem, value)) return;
            if (_selectedItem is not null) _selectedItem.IsSelected = false;
            _selectedItem = value;
            if (_selectedItem is not null) _selectedItem.IsSelected = true;
            SelectedDetails = _selectedItem is null || _selectedItem.IsDirectory
                ? null
                : FileDetails.Read(_selectedItem.PrimaryPath, _selectedItem.Kind, _selectedItem.LibraryLocation);
            OnPropertyChanged();
        }
    }

    public FileDetails? SelectedDetails { get => _selectedDetails; private set { _selectedDetails = value; OnPropertyChanged(); } }
    public string PageTitle { get => _pageTitle; private set { _pageTitle = value; OnPropertyChanged(); } }
    public string PageSubtitle { get => _pageSubtitle; private set { _pageSubtitle = value; OnPropertyChanged(); } }
    public string VisibleCountText { get => _visibleCountText; private set { _visibleCountText = value; OnPropertyChanged(); } }
    public string LastReadText { get => _lastReadText; private set { _lastReadText = value; OnPropertyChanged(); } }
    public bool OverviewIsCurrent { get => _overviewIsCurrent; private set { _overviewIsCurrent = value; OnPropertyChanged(); } }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        FitWindowToScreen();
        ApplyDetailsVisibility();
        ApplyBrand();
        Loaded += (_, _) => LoadLibrary(null);
    }

    /// <summary>
    /// Side Eye build only: shows the Side Eye logo (built into the app) across the top of the
    /// sidebar in place of the pink badge. The generic build keeps the pink badge.
    /// </summary>
    private void ApplyBrand()
    {
#if SIDEEYE
        BrandLogo.Source = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Assets/SideEyeLogo.png", UriKind.Absolute));
        BrandLogo.Visibility = Visibility.Visible;
        PinkBadge.Visibility = Visibility.Collapsed;
        BadgeColumn.Width = new GridLength(0);
        NameBlock.Margin = new Thickness(0);
#endif
    }

    // ---------------------------------------------------------------- window size (worked out fresh every start)

    /// <summary>Fits the window inside the screen's usable area and centres it. Nothing is saved.</summary>
    private void FitWindowToScreen()
    {
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(900, work.Width);
        MinHeight = Math.Min(560, work.Height);
        Width = Math.Max(MinWidth, Math.Min(1480, work.Width * 0.94));
        Height = Math.Max(MinHeight, Math.Min(900, work.Height * 0.94));
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_detailsVisible && DetailsColumn.ActualWidth > 0) _detailsWidth = DetailsColumn.ActualWidth;
        _detailsVisible = !_detailsVisible;
        ApplyDetailsVisibility();
    }

    private void ApplyDetailsVisibility()
    {
        DetailsPanel.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailsSplitter.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailsColumn.MinWidth = _detailsVisible ? 260 : 0;
        DetailsColumn.Width = _detailsVisible ? new GridLength(Math.Clamp(_detailsWidth, 260, 700)) : new GridLength(0);
        DetailsToggle.Content = _detailsVisible ? "Hide details" : "Show details";
    }

    /// <summary>Spreads the tiles across the full width: as many columns as fit, each stretched to fill the row.</summary>
    private void ItemsScroller_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTileSize();

    private void UpdateTileSize()
    {
        // inner width = scroller width - left/right padding (28 + 20) - scroll bar (8) - 2 for rounding.
        // Every tile, including the last in a row, takes its width PLUS the 16px gap after it,
        // so a row of N tiles needs N x (width + gap). (Before 3.0.2 the last gap was left out,
        // so one tile always wrapped and rows showed fewer tiles than planned.)
        var available = ItemsScroller.ActualWidth - 28 - 20 - 8 - 2;
        if (available <= 0) return;
        var columns = Math.Max(1, (int)Math.Floor(available / (MinTileWidth + TileGap)));
        TileWidth = Math.Max(MinTileWidth, Math.Floor(available / columns - TileGap));
    }

    // ---------------------------------------------------------------- reading the Library

    private void LoadLibrary(string? keepSection)
    {
        if (!_paths.LibraryExists)
        {
            _snapshot = null;
            SelectedItem = null;
            DashboardSections.Clear();
            MissingLibraryView.Visibility = Visibility.Visible;
            DashboardView.Visibility = Visibility.Collapsed;
            SectionView.Visibility = Visibility.Collapsed;
            PageTitle = ProjectName;
            PageSubtitle = "No Library folder found";
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var previousSelection = SelectedItem?.PrimaryPath;
            var snapshot = _scanner.Scan(_paths.LibraryPath);
            SelectedItem = null;
            _snapshot = snapshot;
            DashboardSections.Clear();
            foreach (var section in snapshot.Sections) DashboardSections.Add(section);
            LastReadText = $"Read {snapshot.ReadAt:g}";

            MissingLibraryView.Visibility = Visibility.Collapsed;
            if (keepSection is not null && keepSection != OverviewName && FindSection(keepSection) is not null)
            {
                ShowSection(keepSection, keepSearch: true);
                var again = previousSelection is null ? null
                    : VisibleItems.FirstOrDefault(i => string.Equals(i.PrimaryPath, previousSelection, StringComparison.OrdinalIgnoreCase));
                if (again is not null) SelectedItem = again;
            }
            else
            {
                ShowOverview();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "The Library folder could not be read.\n\n" + ex.Message, "Visual Inventory", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadLibrary(_currentSection);

    // ---------------------------------------------------------------- moving around the app

    private SectionSummary? FindSection(string name) =>
        _snapshot?.Sections.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sidebar entries and Overview category cards both land here.</summary>
    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section }) return;
        if (section == OverviewName) ShowOverview(); else ShowSection(section, keepSearch: false);
    }

    private void ShowOverview()
    {
        if (_snapshot is null) { LoadLibrary(null); return; }
        _currentSection = OverviewName;
        DashboardView.Visibility = Visibility.Visible;
        SectionView.Visibility = Visibility.Collapsed;
        PageTitle = ProjectName;
        PageSubtitle = $"{_snapshot.Items.Count} items in {_snapshot.Sections.Count} categories";
        SelectedItem = null;
        EmptyLibraryPanel.Visibility = _snapshot.Sections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSidebarHighlight();
    }

    private void ShowSection(string section, bool keepSearch)
    {
        if (_snapshot is null) return;
        var summary = FindSection(section);
        if (summary is null) { ShowOverview(); return; }
        _currentSection = summary.Name;
        DashboardView.Visibility = Visibility.Collapsed;
        SectionView.Visibility = Visibility.Visible;
        PageTitle = summary.Name;
        PageSubtitle = summary.IsLooseFiles
            ? "Loose files at the top of the Library folder"
            : $"Library{Path.DirectorySeparatorChar}{summary.Name}";
        if (!keepSearch)
        {
            SearchBox.Text = string.Empty;
            ItemsScroller.ScrollToTop();
        }
        ApplyFilter();
        UpdateSidebarHighlight();
        Dispatcher.BeginInvoke(UpdateTileSize, DispatcherPriority.Loaded);
    }

    /// <summary>Marks the category you're looking at (or Overview) so the sidebar can highlight it.</summary>
    private void UpdateSidebarHighlight()
    {
        OverviewIsCurrent = _currentSection == OverviewName;
        for (var i = 0; i < DashboardSections.Count; i++)
        {
            var isCurrent = DashboardSections[i].Name.Equals(_currentSection, StringComparison.OrdinalIgnoreCase);
            if (DashboardSections[i].IsCurrent != isCurrent) DashboardSections[i] = DashboardSections[i].WithCurrent(isCurrent);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_snapshot is not null && _currentSection != OverviewName) ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_snapshot is null || SearchBox is null) return;
        var query = SearchBox.Text.Trim();
        var matches = _snapshot.Items
            .Where(item => item.Section.Equals(_currentSection, StringComparison.OrdinalIgnoreCase))
            .Where(item => query.Length == 0
                           || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.TypeLabel.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var keep = SelectedItem;
        VisibleItems.Clear();
        foreach (var item in matches) VisibleItems.Add(item);
        SelectedItem = keep is not null && matches.Contains(keep) ? keep : VisibleItems.FirstOrDefault();
        VisibleCountText = matches.Count == 1 ? "1 shown" : $"{matches.Count} shown";
    }

    private void ItemCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WorkspaceItem item }) SelectedItem = item;
    }

    // ---------------------------------------------------------------- keyboard

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { LoadLibrary(_currentSection); e.Handled = true; return; }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && SectionView.Visibility == Visibility.Visible)
        {
            SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return;
        }
        if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = string.Empty; e.Handled = true;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
