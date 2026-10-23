using System.Text.RegularExpressions;
using VisualInventory.Services;

// Visual Inventory 3.0.0 tests.
//  Part 1: the reader shows the Library correctly.
//  Part 2: reading the Library changes nothing at all.
//  Part 3: the isolation check - the app's own code contains nothing able to write, move,
//          delete, open, start programs, use the clipboard, file pickers or the internet.
// (The test itself creates a throwaway test Library to read; the app never does.)
if (args.Length != 2) { Console.Error.WriteLine("Usage: VisualInventory.Tests <empty-test-folder> <app-source-folder>"); return 2; }

var failures = new List<string>();
void Check(bool ok, string message) { if (!ok) failures.Add(message); }

// ------------------------------------------------------------------ test Library (made by the test)
var testRoot = Path.GetFullPath(args[0]);
if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
var projectRoot = Path.Combine(testRoot, "Side Eye Series");
var paths = new ProjectPaths(projectRoot);
Directory.CreateDirectory(projectRoot);
Check(!paths.LibraryExists, "Library should not exist before the test makes it.");

var lib = paths.LibraryPath;
Directory.CreateDirectory(Path.Combine(lib, "4-Ads"));
Directory.CreateDirectory(Path.Combine(lib, "1-Card Covers"));
Directory.CreateDirectory(Path.Combine(lib, ".hidden-folder"));
File.WriteAllBytes(Path.Combine(lib, "4-Ads", "card-front.png"), [137, 80, 78, 71, 1, 2, 3]);
File.WriteAllText(Path.Combine(lib, "4-Ads", "stocklist.pdf"), "pdf placeholder");
File.WriteAllText(Path.Combine(lib, "4-Ads", "script.js"), "// js placeholder");
File.WriteAllText(Path.Combine(lib, "4-Ads", "desktop.ini"), "hidden");
File.WriteAllText(Path.Combine(lib, "4-Ads", ".htaccess"), "hidden");
Directory.CreateDirectory(Path.Combine(lib, "4-Ads", "Bundle", "inner"));
File.WriteAllBytes(Path.Combine(lib, "4-Ads", "Bundle", "cover.jpg"), [1, 2]);
File.WriteAllText(Path.Combine(lib, "4-Ads", "Bundle", "inner", "a.txt"), "a");
File.WriteAllText(Path.Combine(lib, "loose.txt"), "loose");
var outside = Path.Combine(testRoot, "outside");
Directory.CreateDirectory(outside);
File.WriteAllBytes(Path.Combine(outside, "secret.png"), [1]);
var linkMade = true;
try { Directory.CreateSymbolicLink(Path.Combine(lib, "4-Ads", "LinkToOutside"), outside); }
catch (Exception) { linkMade = false; }

Check(paths.ProjectName == "Side Eye Series", "Project name should be the project folder's name.");

// ------------------------------------------------------------------ Part 2 setup: fingerprint everything first
static Dictionary<string, string> Fingerprint(string root) =>
    new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
        .ToDictionary(i => i.FullName, i => i is FileInfo f ? $"{f.Length}|{f.LastWriteTimeUtc.Ticks}|{f.Attributes}" : $"dir|{i.LastWriteTimeUtc.Ticks}|{i.Attributes}");
var before = Fingerprint(testRoot);

// ------------------------------------------------------------------ Part 1: the reader
var snapshot = new ProjectScanner().Scan(lib);
Check(snapshot.Sections.Select(s => s.Name).SequenceEqual(["1-Card Covers", "4-Ads", "Unsorted"]),
    "Categories should be the real folders (hidden ones skipped) plus Unsorted: " + string.Join(", ", snapshot.Sections.Select(s => s.Name)));
Check(snapshot.Sections.Single(s => s.Name == "Unsorted").IsLooseFiles, "Unsorted should be marked as loose files, not a folder.");
var ads = snapshot.Items.Where(i => i.Section == "4-Ads").Select(i => i.FileName).OrderBy(n => n).ToList();
var expectedAds = new List<string> { "Bundle", "card-front.png", "script.js", "stocklist.pdf" };
Check(ads.SequenceEqual(expectedAds), "4-Ads should list exactly its visible items (no hidden files, no links): " + string.Join(", ", ads));
if (!linkMade) Console.WriteLine("note: could not create a linked folder on this machine; link-skipping not exercised");
var png = snapshot.Items.Single(i => i.FileName == "card-front.png");
Check(png.HasPreview && png.TypeLabel == "PNG" && png.Kind == "PNG file", "PNG should show as a picture.");
var pdf = snapshot.Items.Single(i => i.FileName == "stocklist.pdf");
Check(!pdf.HasPreview && pdf.TypeLabel == "PDF", "PDF should show its type, not a picture.");
var js = snapshot.Items.Single(i => i.FileName == "script.js");
Check(!js.HasPreview && js.TypeLabel == "JS", "JS files should be listed and show their type.");
var bundle = snapshot.Items.Single(i => i.FileName == "Bundle");
Check(bundle.IsDirectory && bundle.HasPreview && bundle.SourceFileCount == 2, "A folder item should use the picture inside it and count its files.");
Check(snapshot.Items.Single(i => i.FileName == "loose.txt").Section == "Unsorted", "Loose files should be under Unsorted.");
Check(png.LibraryLocation == Path.Combine("Library", "4-Ads", "card-front.png"), "Location should be shown relative to Library: " + png.LibraryLocation);

// ------------------------------------------------------------------ Part 2: nothing changed by reading
var after = Fingerprint(testRoot);
Check(before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var v) && v == kv.Value),
    "Reading the Library changed something on disk (a file or folder was created, changed or removed).");
Check(!Directory.Exists(Path.Combine(projectRoot, "Inventory Data")), "No 'Inventory Data' folder should ever be created.");

// ------------------------------------------------------------------ Part 3: the isolation check on the app's code
var source = Path.GetFullPath(args[1]);
var forbidden = new (string What, string Pattern)[]
{
    ("writes, copies, moves or deletes files",  @"\bFile\.(Write\w*|Append\w*|Create\w*|Copy\w*|Move\w*|Delete\w*|Replace\w*|Encrypt|Decrypt|Set\w+|Open|OpenWrite)\b"),
    ("creates, moves or deletes folders",        @"\bDirectory\.(Create\w*|Move|Delete|Set\w+)\b"),
    ("opens files for writing",                  @"\bFileMode\.(Create|CreateNew|Append|OpenOrCreate|Truncate)\b|\bFileAccess\.(Write|ReadWrite)\b"),
    ("writes with a writer",                     @"\b(StreamWriter|BinaryWriter|XmlWriter|TextWriter)\b"),
    // KNOWN GAP: FileInfo.Replace(...) looks exactly like ordinary text .Replace(...), so this check
    // cannot tell them apart and does not look for it. File.Replace(...) IS caught by the rule above.
    ("changes files via FileInfo/DirectoryInfo", @"\.(MoveTo|CopyTo|Delete|Encrypt|Decrypt|CreateSubdirectory|CreateText|AppendText|OpenWrite)\s*\("),
    ("starts other programs",                    @"\bProcess\.Start\b|\bProcessStartInfo\b|ShellExecute|explorer\.exe"),
    ("uses the clipboard",                       @"\bClipboard\b"),
    ("uses file pickers",                        @"\b(OpenFileDialog|SaveFileDialog|OpenFolderDialog)\b"),
    ("uses the internet",                        @"\b(HttpClient|WebClient|WebRequest|Socket|TcpClient|UdpClient|WebSocket)\b|\bSystem\.Net\b"),
    ("touches the registry",                     @"\bRegistry\b|\bMicrosoft\.Win32\b"),
    ("reads other system folders",               @"\bEnvironment\.(GetFolderPath|SpecialFolder|SetEnvironmentVariable)\b"),
    ("calls Windows directly",                   @"\b(DllImport|LibraryImport)\b|\bMarshal\."),
    ("saves anything",                           @"\bIsolatedStorage\b|\bJsonSerializer\b|\bSettings\.Default\b"),
    ("accepts dropped files",                    @"AllowDrop\s*=\s*""True""|\bDrop\s*="),
};

// The check must be able to catch things, or a pass means nothing. Prove it on known-bad lines first.
var knownBad = new[] { "File.WriteAllText(p, s);", "File.AppendAllText(p, s);", "File.Copy(a, b);", "File.Delete(p);", "File.Replace(a, b, c);",
                       "Directory.CreateDirectory(p);", "Directory.Delete(p, true);", "new FileStream(p, FileMode.Create)", "new FileStream(p, FileMode.Open, FileAccess.Write)",
                       "Process.Start(x);", "Clipboard.SetText(t);", "new OpenFileDialog()", "new HttpClient()", "Registry.CurrentUser",
                       "info.MoveTo(p);", "info.Delete();", "info.CreateText();", "AllowDrop=\"True\"" };
foreach (var line in knownBad)
    Check(forbidden.Any(f => Regex.IsMatch(line, f.Pattern)), "The isolation check failed to catch a known-bad line: " + line);
var knownGood = new[] { "File.Exists(p)", "Directory.EnumerateFiles(p)", "new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)",
                        "BitmapDecoder.Create(stream, o, c)", "File.GetAttributes(p)", "new FileInfo(p)" };
foreach (var line in knownGood)
    Check(!forbidden.Any(f => Regex.IsMatch(line, f.Pattern)), "The isolation check wrongly flags a read-only line: " + line);

var codeFiles = Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
    .OrderBy(f => f).ToList();
Check(codeFiles.Count >= 10, $"Expected the app's source files, found only {codeFiles.Count} in {source}.");
var hits = 0;
foreach (var file in codeFiles)
{
    var lines = File.ReadAllLines(file);
    for (var i = 0; i < lines.Length; i++)
    {
        var code = Regex.Replace(lines[i], @"//.*$", "");               // ignore C# comments
        code = Regex.Replace(code, @"<!--.*?-->", "");                   // ignore one-line XAML comments
        foreach (var (what, pattern) in forbidden)
            if (Regex.IsMatch(code, pattern))
            {
                hits++;
                failures.Add($"ISOLATION: {Path.GetRelativePath(source, file)} line {i + 1} {what}: {lines[i].Trim()}");
            }
    }
}
Console.WriteLine($"Isolation check: {codeFiles.Count} app files checked, {forbidden.Length} kinds of forbidden code looked for, {hits} found.");

if (failures.Count == 0) { Console.WriteLine("VISUAL INVENTORY 3.0.3 TESTS: PASS"); return 0; }
foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
Console.Error.WriteLine("VISUAL INVENTORY 3.0.3 TESTS: FAIL");
return 1;
