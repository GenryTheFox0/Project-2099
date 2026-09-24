using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

namespace EotInstaller {
  interface IGameSource : IDisposable {
    string Kind { get; }
    string Name { get; }
    bool Exists(string relativePath);
    long GetLength(string relativePath);
    Stream OpenRead(string relativePath);
    IEnumerable<string> EnumerateFiles();
  }

  sealed class DirectoryGameSource : IGameSource {
    readonly string root;
    readonly string rootPrefix;
    public DirectoryGameSource(string path) {
      root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
      rootPrefix = root + Path.DirectorySeparatorChar;
    }
    public string Kind { get { return "folder"; } }
    public string Name { get { return new DirectoryInfo(root).Name; } }
    string Resolve(string relativePath) {
      string safe = InstallerCore.NormalizeRelative(relativePath).Replace('/', Path.DirectorySeparatorChar);
      string full = Path.GetFullPath(Path.Combine(root, safe));
      if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Source path escaped the selected directory");
      return full;
    }
    public bool Exists(string relativePath) { return File.Exists(Resolve(relativePath)); }
    public long GetLength(string relativePath) { string p = Resolve(relativePath); return File.Exists(p) ? new FileInfo(p).Length : -1; }
    public Stream OpenRead(string relativePath) {
      return new FileStream(Resolve(relativePath), FileMode.Open, FileAccess.Read, FileShare.Read,
        1024 * 1024, FileOptions.SequentialScan);
    }
    public IEnumerable<string> EnumerateFiles() {
      foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        yield return file.Substring(rootPrefix.Length).Replace(Path.DirectorySeparatorChar, '/');
    }
    public void Dispose() { }
  }

  sealed class IsoGameSource : IGameSource {
    readonly XdvdfsImage image;
    public IsoGameSource(string path) { image = new XdvdfsImage(path); }
    public string Kind { get { return "xdvdfs-iso"; } }
    public string Name { get { return image.Name; } }
    public bool Exists(string relativePath) { return image.Exists(relativePath); }
    public long GetLength(string relativePath) { return image.GetLength(relativePath); }
    public Stream OpenRead(string relativePath) { return image.OpenRead(relativePath); }
    public IEnumerable<string> EnumerateFiles() {
      foreach (var entry in image.Entries) yield return entry.Path;
    }
    public void Dispose() { image.Dispose(); }
  }

  sealed class SvodGameSource : IGameSource {
    readonly SvodImage image;
    // Set when the image was unpacked out of a ZIP by the installer itself, so
    // the copy can be dropped once the install has what it needs.
    public string Scratch;
    public SvodGameSource(string path) { image = new SvodImage(path); }
    public string Kind { get { return "xbox360-god-svod"; } }
    public string Name { get { return image.Name; } }
    public bool Exists(string relativePath) { return image.Exists(relativePath); }
    public long GetLength(string relativePath) { return image.GetLength(relativePath); }
    public Stream OpenRead(string relativePath) { return image.OpenRead(relativePath); }
    public IEnumerable<string> EnumerateFiles() {
      foreach (var entry in image.Entries) yield return entry.Path;
    }
    public void Dispose() { image.Dispose(); }
  }

  sealed class ZipGameSource : IGameSource {
    readonly string zipPath;
    readonly FileStream stream;
    readonly ZipArchive archive;
    readonly Dictionary<string, ZipArchiveEntry> entries =
      new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
    readonly string rootPrefix;

    public ZipGameSource(string path) {
      zipPath = Path.GetFullPath(path);
      if (!File.Exists(zipPath)) throw new FileNotFoundException("ZIP not found", zipPath);
      stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read,
        1024 * 1024, FileOptions.SequentialScan);
      try {
        archive = new ZipArchive(stream, ZipArchiveMode.Read, true, Encoding.UTF8);
        var rawEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries) {
          string raw = entry.FullName.Replace('\\', '/').TrimStart('/');
          if (raw.Length == 0 || raw.EndsWith("/", StringComparison.Ordinal)) continue;
          string normalized = InstallerCore.NormalizeRelative(raw);
          if (rawEntries.ContainsKey(normalized)) throw new InvalidDataException("Duplicate ZIP path: " + normalized);
          rawEntries.Add(normalized, entry);
        }

        var roots = new List<string>();
        foreach (string name in rawEntries.Keys) {
          const string marker = "Default.xex";
          if (!name.EndsWith(marker, StringComparison.OrdinalIgnoreCase)) continue;
          int markerIndex = name.Length - marker.Length;
          if (markerIndex != 0 && name[markerIndex - 1] != '/') continue;
          string prefix = name.Substring(0, markerIndex);
          if (rawEntries.ContainsKey(prefix + "Data/Main.pkz")) roots.Add(prefix);
        }
        roots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (roots.Count == 0) throw new InvalidDataException(
          "ZIP не содержит корень Xbox 360-игры с Default.xex и Data/Main.pkz");
        if (roots.Count > 1) throw new InvalidDataException(
          "ZIP содержит несколько игровых корней. Оставь в архиве одну копию Spider-Man: Edge of Time");
        rootPrefix = roots[0];

        foreach (var pair in rawEntries) {
          if (!pair.Key.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
          string relative = pair.Key.Substring(rootPrefix.Length);
          if (relative.Length == 0) continue;
          relative = InstallerCore.NormalizeRelative(relative);
          if (entries.ContainsKey(relative)) throw new InvalidDataException("Duplicate game path in ZIP: " + relative);
          entries.Add(relative, pair.Value);
        }
      } catch {
        if (archive != null) archive.Dispose();
        stream.Dispose();
        throw;
      }
    }

    public string Kind { get { return "zip-game"; } }
    public string Name {
      get {
        string folder = rootPrefix.TrimEnd('/');
        return Path.GetFileName(zipPath) + (folder.Length == 0 ? "" : " / " + folder);
      }
    }
    public bool Exists(string relativePath) { return entries.ContainsKey(InstallerCore.NormalizeRelative(relativePath)); }
    public long GetLength(string relativePath) {
      ZipArchiveEntry entry;
      return entries.TryGetValue(InstallerCore.NormalizeRelative(relativePath), out entry) ? entry.Length : -1;
    }
    public Stream OpenRead(string relativePath) {
      ZipArchiveEntry entry;
      if (!entries.TryGetValue(InstallerCore.NormalizeRelative(relativePath), out entry))
        throw new FileNotFoundException("File not present in ZIP", relativePath);
      return entry.Open();
    }
    public IEnumerable<string> EnumerateFiles() { return entries.Keys; }
    public void Dispose() { archive.Dispose(); stream.Dispose(); }
  }

  public sealed class InstallerCore {
    static readonly string[] EmbeddedManifests = {
      "EOT.source-manifest.eu.json",
      "EOT.source-manifest.ru-god.json",
      "EOT.source-manifest.usa-europe.json",
      "EOT.source-manifest.usa-europe-r2.json",
      "EOT.source-manifest.sazanoff.json"
    };
    const int BufferSize = 1024 * 1024;
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 32 };
    readonly List<GameManifest> gameManifests = new List<GameManifest>();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);

    public InstallerCore() {
      foreach (string resource in EmbeddedManifests) {
        GameManifest manifest = LoadEmbedded<GameManifest>(resource); ValidateGameManifest(manifest); gameManifests.Add(manifest);
      }
      if (gameManifests.Select(manifest => manifest.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != gameManifests.Count)
        throw new InvalidDataException("Duplicate source manifest ID");
    }

    public GameManifest Manifest { get { return gameManifests[0]; } }

    // What the last install actually managed, for the page that reports it.
    public int LastTranslatedFiles;
    public int LastExpectedTranslatedFiles;
    public List<string> LastUntranslatedFiles = new List<string>();
    public List<string> LastSourceDeviations = new List<string>();
    public string LastRevisionName;

    static readonly string[] RequiredFiles = { "Default.xex", "Data/Main.pkz", "Data/BaseGameplay.pkz" };
    Dictionary<string, PatchEntry> englishPatches;
    Dictionary<string, PatchEntry> russianPatches;
    Dictionary<string, ManifestFile> canonicalOriginal;
    Dictionary<string, ManifestFile> canonicalRussian;

#if EOT_INSTALLER_TEST
    // Answers "how far is this dump from each donor revision we know", which is the
    // question behind every "Source hash mismatch" report. Each file is read once and
    // compared against every manifest, so a 7 GB image costs one pass.
    // Writes every file the manifests know about into a plain folder, so a new donor
    // revision can be turned into a manifest and a delta set with the ordinary tools.
    public void ExtractKnownFiles(string sourcePath, string targetRoot, TextWriter writer) {
      using (IGameSource source = OpenSource(sourcePath)) {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GameManifest manifest in gameManifests)
          foreach (ManifestFile file in manifest.Files)
            if (seen.Add(file.Path)) paths.Add(file.Path);
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(targetRoot);
        int written = 0;
        foreach (string path in paths) {
          if (!source.Exists(path)) { writer.WriteLine("ABSENT\t" + path); continue; }
          string output = SafeJoin(targetRoot, path);
          Directory.CreateDirectory(Path.GetDirectoryName(output));
          using (Stream input = source.OpenRead(path))
          using (var file = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.SequentialScan)) input.CopyTo(file, BufferSize);
          written++;
        }
        writer.WriteLine("WROTE\t" + written + " of " + paths.Count);
      }
    }

    public void ReportSourceDifferences(string sourcePath, TextWriter writer) {
      using (IGameSource source = OpenSource(sourcePath)) {
        writer.WriteLine("SOURCE\t" + source.Kind + "\t" + source.Name);
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GameManifest manifest in gameManifests)
          foreach (ManifestFile file in manifest.Files)
            if (seen.Add(file.Path)) paths.Add(file.Path);
        paths.Sort(StringComparer.OrdinalIgnoreCase);

        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths) {
          if (!source.Exists(path)) continue;
          long length = source.GetLength(path);
          sizes[path] = length;
          using (Stream input = source.OpenRead(path))
            hashes[path] = HashStream(input, length, CancellationToken.None, null,
              "game source [" + source.Kind + "]: " + path);
        }
        writer.WriteLine("READ\t" + hashes.Count + " of " + paths.Count + " known paths");

        foreach (GameManifest manifest in gameManifests) {
          var missing = new List<string>();
          var differing = new List<ManifestFile>();
          foreach (ManifestFile file in manifest.Files.OrderBy(value => value.Path, StringComparer.OrdinalIgnoreCase)) {
            if (!hashes.ContainsKey(file.Path)) { missing.Add(file.Path); continue; }
            if (sizes[file.Path] != file.Size || !SameHash(hashes[file.Path], file.Sha256)) differing.Add(file);
          }
          writer.WriteLine("MANIFEST\t" + manifest.Id + "\tfiles=" + manifest.Files.Count +
            "\tmissing=" + missing.Count + "\tdiffers=" + differing.Count);
          foreach (string path in missing) writer.WriteLine("  MISSING\t" + path);
          foreach (ManifestFile file in differing)
            writer.WriteLine("  DIFFERS\t" + file.Path + "\tsize " + file.Size + " -> " + sizes[file.Path] +
              "\t" + file.Sha256.ToLowerInvariant() + " -> " + hashes[file.Path].ToLowerInvariant());
        }
        foreach (string path in paths)
          if (!hashes.ContainsKey(path)) writer.WriteLine("ABSENT\t" + path);
      }
    }
#endif

    T LoadEmbedded<T>(string name) {
      using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
        if (stream == null) throw new InvalidOperationException("Embedded resource is missing: " + name);
        using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return json.Deserialize<T>(reader.ReadToEnd());
      }
    }

    T LoadJson<T>(string path) {
      if (!File.Exists(path)) throw new FileNotFoundException("Installer payload file is missing", path);
      return json.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
    }

    static void ValidateGameManifest(GameManifest manifest) {
      if (manifest == null || manifest.Schema != 1 || String.IsNullOrWhiteSpace(manifest.Id) || manifest.Files == null || manifest.Files.Count == 0)
        throw new InvalidDataException("Invalid source manifest");
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (ManifestFile file in manifest.Files) {
        file.Path = NormalizeRelative(file.Path);
        if (!seen.Add(file.Path) || file.Size < 0 || !IsSha256(file.Sha256))
          throw new InvalidDataException("Invalid source manifest entry: " + file.Path);
      }
      if (manifest.QuickChecks == null || manifest.QuickChecks.Count == 0)
        throw new InvalidDataException("Source manifest has no quick checks");
    }

    static bool IsSha256(string value) {
      if (value == null || value.Length != 64) return false;
      for (int i = 0; i < value.Length; i++) {
        char c = value[i]; if (!Uri.IsHexDigit(c)) return false;
      }
      return true;
    }

    public static string NormalizeRelative(string path) {
      if (String.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        throw new InvalidDataException("Unsafe relative path");
      string value = path.Replace('\\', '/').Trim('/');
      string[] parts = value.Split('/');
      if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.IndexOf(':') >= 0))
        throw new InvalidDataException("Unsafe relative path: " + path);
      return value;
    }

    static bool IsDirectoryGameRoot(string path) {
      return Directory.Exists(path) && File.Exists(Path.Combine(path, "Default.xex")) &&
        Directory.Exists(Path.Combine(path, "Data"));
    }

    static string[] GetChildDirectories(string path) {
      try { return Directory.GetDirectories(path); }
      catch (UnauthorizedAccessException) { return new string[0]; }
      catch (IOException) { return new string[0]; }
    }

    static bool SkipWrapperDirectory(string path) {
      string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
      return String.Equals(name, "Data", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name, "video", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name, "$SystemUpdate", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name, "Support", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name, "payload", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name, "patches", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(name, ".git", StringComparison.OrdinalIgnoreCase);
    }

    static string FindDirectoryGameRoot(string selectedPath) {
      if (!Directory.Exists(selectedPath)) return null;
      string selected = Path.GetFullPath(selectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      if (IsDirectoryGameRoot(selected)) return selected;

      var matches = new List<string>();
      int inspected = 0;
      foreach (string child in GetChildDirectories(selected).Take(64)) {
        if (++inspected > 128) break;
        if (IsDirectoryGameRoot(child)) matches.Add(Path.GetFullPath(child));
        if (SkipWrapperDirectory(child)) continue;
        foreach (string grandchild in GetChildDirectories(child).Take(32)) {
          if (++inspected > 128) break;
          if (IsDirectoryGameRoot(grandchild)) matches.Add(Path.GetFullPath(grandchild));
        }
        if (inspected > 128) break;
      }
      matches = matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
      if (matches.Count == 1) return matches[0];
      if (matches.Count > 1) throw new InvalidDataException(
        "В выбранной папке найдено несколько копий игры. Выбери нужную папку с Default.xex: " +
        String.Join("; ", matches.Select(Path.GetFileName)));
      return null;
    }

    static bool IsPackagedSource(string file) {
      string extension = Path.GetExtension(file);
      return String.Equals(extension, ".iso", StringComparison.OrdinalIgnoreCase) ||
        String.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase);
    }

    // Same reach as the game-root search: a downloaded image usually sits one folder
    // deeper than the folder the player points at.
    static void CollectPackagedSources(string directory, int depth, List<string> matches, int[] budget) {
      if (budget[0]-- <= 0) return;
      try {
        foreach (string file in Directory.GetFiles(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
          if (IsPackagedSource(file)) matches.Add(Path.GetFullPath(file));
      } catch (UnauthorizedAccessException) { return; }
      catch (IOException) { return; }
      if (depth >= 2 || matches.Count > 0) return;
      foreach (string child in GetChildDirectories(directory).Take(32)) {
        if (SkipWrapperDirectory(child)) continue;
        CollectPackagedSources(child, depth + 1, matches, budget);
        if (matches.Count > 0) return;
      }
    }

    static string FindSinglePackagedSource(string selectedPath) {
      if (!Directory.Exists(selectedPath)) return null;
      var matches = new List<string>();
      CollectPackagedSources(Path.GetFullPath(selectedPath), 0, matches, new int[] { 128 });
      matches = matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
      if (matches.Count > 1) throw new InvalidDataException(
        "В выбранной папке несколько образов. Выбери нужный файл: " +
        String.Join("; ", matches.Select(Path.GetFileName).ToArray()));
      return matches.Count == 1 ? matches[0] : null;
    }

    IGameSource OpenSource(string path) { return OpenSource(path, null); }

    IGameSource OpenSource(string path, Action<InstallProgress> progress) {
      string gameRoot = FindDirectoryGameRoot(path);
      if (gameRoot != null) return new DirectoryGameSource(gameRoot);
      string container;
      if (SvodImage.TryFindContainer(path, out container)) return new SvodGameSource(container);
      string packaged = File.Exists(path) ? Path.GetFullPath(path) : FindSinglePackagedSource(path);
      if (packaged != null) {
        string extension = Path.GetExtension(packaged);
        if (String.Equals(extension, ".iso", StringComparison.OrdinalIgnoreCase)) return new IsoGameSource(packaged);
        if (String.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)) {
          // A ZIP of a GOD image is what the torrents hand out. Unpack it ourselves
          // rather than asking the player to -- that is how fragments get cut short.
          string unpackedContainer, scratch;
          if (SvodImage.TryExtractFromZip(packaged, progress, out unpackedContainer, out scratch))
            return new SvodGameSource(unpackedContainer) { Scratch = scratch };
          return new ZipGameSource(packaged);
        }
      }
      throw new InvalidDataException(
        "В выбранном месте игры нет. Подойдёт любое из трёх: файл .iso или .zip; папка GOD-образа (в ней лежит 00007000 или файл без расширения рядом с папкой .data); папка с Default.xex и каталогом Data");
    }

    public Task<SourceProbe> ProbeAsync(string sourcePath, Action<InstallProgress> progress,
      CancellationToken cancellation) {
      return Task.Run(() => {
        using (IGameSource source = OpenSource(sourcePath, progress)) {
          List<ManifestFile> files = CollectSourceFiles(source);
          GameManifest revision = RecogniseRevision(files, source, cancellation);
          return new SourceProbe {
            ManifestId = revision == null ? "unknown-revision" : revision.Id,
            Kind = source.Kind,
            DisplayName = source.Name,
            Region = revision == null ? "unknown revision" : revision.Region,
            FilesFound = files.Count,
            RequiredBytes = files.Sum(file => file.Size)
          };
        }
      }, cancellation);
    }

    // Discover the actual dump without requiring a whole-image revision match.
    // Installation separately verifies every file's route to the canonical tree;
    // an unknown embedded-dialogue package must not silently pass through.
    List<ManifestFile> CollectSourceFiles(IGameSource source) {
      var files = new List<ManifestFile>();
      foreach (string raw in source.EnumerateFiles()) {
        string path = NormalizeRelative(raw);
        if (path.StartsWith("$SystemUpdate/", StringComparison.OrdinalIgnoreCase)) continue;
        if (SamePath(path, "nxeart")) continue;
        long length = source.GetLength(path);
        if (length < 0) continue;
        files.Add(new ManifestFile { Path = path, Size = length, Sha256 = null });
      }
      files.Sort((left, right) => String.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
      foreach (string required in RequiredFiles)
        if (!files.Any(file => SamePath(file.Path, required)))
          throw new InvalidDataException(
            "Игра в источнике найдена не полностью: не хватает " + required +
            ". Это не Spider-Man: Edge of Time или образ распакован не целиком");
      return files;
    }

    // A known revision only changes what we call the dump on screen.
    GameManifest RecogniseRevision(List<ManifestFile> files, IGameSource source, CancellationToken cancellation) {
      foreach (GameManifest manifest in gameManifests) {
        bool matches = true;
        foreach (string quick in manifest.QuickChecks) {
          ManifestFile expected = manifest.Files.FirstOrDefault(file => SamePath(file.Path, quick));
          ManifestFile actual = files.FirstOrDefault(file => SamePath(file.Path, quick));
          if (expected == null || actual == null || actual.Size != expected.Size) { matches = false; break; }
          cancellation.ThrowIfCancellationRequested();
          string hash;
          using (Stream input = source.OpenRead(actual.Path)) hash = HashStream(input, actual.Size, cancellation, null,
            "game source [" + source.Kind + "]: " + actual.Path);
          if (!SameHash(hash, expected.Sha256)) { matches = false; break; }
        }
        if (matches) return manifest;
      }
      return null;
    }

    public Task InstallAsync(string sourcePath, string destination, string payloadRoot, int selectedLanguage,
      Action<InstallProgress> progress, CancellationToken cancellation) {
      return Task.Run(() => Install(sourcePath, destination, payloadRoot, selectedLanguage, progress, cancellation), cancellation);
    }

    void Install(string sourcePath, string destination, string payloadRoot, int selectedLanguage,
      Action<InstallProgress> progress, CancellationToken cancellation) {
      if (!new[] { 12, 1, 2, 3, 4, 5 }.Contains(selectedLanguage)) throw new InvalidDataException("Unsupported language ID");
      string target = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      if (String.Equals(target, Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Нельзя устанавливать игру прямо в корень диска");
      if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        throw new IOException("Папка установки должна быть пустой");
      string parent = Path.GetDirectoryName(target);
      if (String.IsNullOrEmpty(parent)) throw new InvalidOperationException("Некорректная папка установки");
      Directory.CreateDirectory(parent);
      string stage = target + ".installing-" + Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N");
      string portRoot = Path.Combine(payloadRoot, "port");
      string payloadManifestPath = Path.Combine(payloadRoot, "payload-manifest.json");
      string patchesRoot = Path.Combine(payloadRoot, "patches");
      PayloadManifest payload = LoadJson<PayloadManifest>(payloadManifestPath);
      ValidatePayloadManifest(payload);
      PatchIndex index = LoadJson<PatchIndex>(Path.Combine(patchesRoot, "index.json"));
      ValidatePatchIndex(index, patchesRoot);
      // The destination is the best hint about where a ZIP image may be unpacked
      // and where the payload may be cached; the window sets it too, but the
      // install must not depend on that.
      PayloadProvider.DestinationHint = target;
      string scratchToDrop = null;
      using (IGameSource source = OpenSource(sourcePath, progress)) {
        var unpacked = source as SvodGameSource;
        if (unpacked != null) scratchToDrop = unpacked.Scratch;
        List<ManifestFile> files = CollectSourceFiles(source);
        // Beyond the three files that say "this is the game", the packages the
        // translation lands in must be there too: a dump missing one of those is
        // truncated, and finding that out now beats finding it out in the game.
        foreach (string required in russianPatches.Values.Select(entry => entry.Path)
          .Distinct(StringComparer.OrdinalIgnoreCase))
          if (!files.Any(file => SamePath(file.Path, required)))
            throw new InvalidDataException("Игра в источнике неполная: не хватает " + required);
        GameManifest revision = RecogniseRevision(files, source, cancellation);
        long sourceBytes = files.Sum(file => file.Size);
        long total = payload.Files.Sum(file => file.Size) + sourceBytes * 3;
        long completed = 0;
        var applied = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try {
          Directory.CreateDirectory(stage);
          foreach (ManifestFile file in payload.Files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)) {
            cancellation.ThrowIfCancellationRequested();
            string relative = NormalizeRelative(file.Path);
            string payloadSource = SafeJoin(portRoot, relative);
            string output = SafeJoin(stage, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string hash = CopyFileWithHash(payloadSource, output, file.Size, cancellation, bytes => {
              completed += bytes; Report(progress, "PC Edition", relative, completed, total);
            }, "PC Edition payload: " + relative);
            if (!SameHash(hash, file.Sha256)) throw new InvalidDataException("Payload hash mismatch: " + relative);
          }

          string donorRoot = Path.Combine(stage, ".source");
          foreach (ManifestFile file in files) {
            cancellation.ThrowIfCancellationRequested();
            string output = SafeJoin(donorRoot, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (Stream input = source.OpenRead(file.Path))
            using (var targetStream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
              BufferSize, FileOptions.SequentialScan)) {
              file.Sha256 = CopyStreamWithHash(input, targetStream, file.Size, cancellation, bytes => {
                completed += bytes; Report(progress, "Распаковка источника", file.Path, completed, total);
              }, "game source [" + source.Kind + "]: " + file.Path);
            }
          }

          // Decide before writing anything: a half-translated tree is worse than
           // an English one, because the strings need the translated fonts to be
           // legible at all.
          ValidateOriginalRoute(files);
          List<string> missing = PlanTranslation(files);
          bool translate = missing.Count == 0;
          LastUntranslatedFiles = missing;   // the receipt is written before the move, and must say this
          var englishOriginal = new List<string>();
          var englishRussian = new List<string>();
          BuildTree("Original", files, index, donorRoot, Path.Combine(stage, "Data", "Original"),
            patchesRoot, false, englishOriginal, progress, cancellation, ref completed, total);
          applied["Original"] = englishOriginal;
          applied["Russian"] = BuildTree("Russian", files, index, donorRoot, Path.Combine(stage, "Data", "Russian"),
            patchesRoot, translate, englishRussian, progress, cancellation, ref completed, total);
          Directory.Delete(donorRoot, true);
          int effectiveLanguage = selectedLanguage == RussianLanguageId && !translate
            ? 1 : selectedLanguage;
          ApplyDefaultLanguage(stage, effectiveLanguage);
          ApplyUpdateSources(stage);
          WriteReceipt(stage, source, files, revision, payload, index, applied,
            selectedLanguage, effectiveLanguage);

          cancellation.ThrowIfCancellationRequested();
          if (Directory.Exists(target)) Directory.Delete(target, false);
          Directory.Move(stage, target);
          LastTranslatedFiles = applied["Russian"].Count;
          LastExpectedTranslatedFiles = russianPatches.Values.Select(entry => entry.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
          LastRevisionName = revision == null ? null : revision.Region;
          Report(progress, "Готово", "Launcher.exe", total, total);
        } catch {
          try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
          throw;
        }
      }
      // The unpacked copy of a ZIP image has served its purpose; a failed install
      // keeps it, so a retry does not unpack five gigabytes again.
      if (scratchToDrop != null) { try { Directory.Delete(scratchToDrop, true); } catch { } }
    }

    // Which of the translated files this dump cannot produce. Every hash is
    // already known from the copy, so this costs nothing and answers the only
    // question that matters before the Russian tree is written.
    List<string> PlanTranslation(List<ManifestFile> files) {
      var missing = new List<string>();
      foreach (string path in russianPatches.Values.Select(entry => entry.Path)
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) {
        ManifestFile file = files.FirstOrDefault(value => SamePath(value.Path, path));
        if (file == null) { missing.Add(path); continue; }
        string hash = file.Sha256;
        long size = file.Size;
        foreach (PatchEntry english in ResolvePatchChain(englishPatches, path, hash, size)) {
          hash = english.TargetSha256; size = english.TargetSize;
        }
        foreach (PatchEntry russian in ResolvePatchChain(russianPatches, path, hash, size)) {
          hash = russian.TargetSha256; size = russian.TargetSize;
        }
        ManifestFile expected;
        if (!canonicalRussian.TryGetValue(path, out expected) ||
            !SameHash(hash, expected.Sha256) || size != expected.Size) missing.Add(path);
      }
      return missing;
    }

    // Never label a passthrough level "Original" without checking its final hash.
    // Level PKZs carry embedded dialogue outside the global string tables.
    void ValidateOriginalRoute(List<ManifestFile> files) {
      foreach (ManifestFile file in files) {
        string hash = file.Sha256;
        long size = file.Size;
        foreach (PatchEntry patch in ResolvePatchChain(englishPatches, file.Path, hash, size)) {
          hash = patch.TargetSha256; size = patch.TargetSize;
        }
        ManifestFile expected;
        if (!canonicalOriginal.TryGetValue(file.Path, out expected) ||
            !SameHash(hash, expected.Sha256) || size != expected.Size)
          throw new InvalidDataException("Language normalization is not verified for " + file.Path +
            " (source SHA-256 " + file.Sha256 + "). An updated payload is required; no mixed-language installation was created.");
      }
      foreach (string path in canonicalOriginal.Keys)
        if (!files.Any(file => SamePath(file.Path, path)))
          throw new InvalidDataException("Language normalization source is incomplete: " + path);
    }

    internal static List<PatchEntry> ResolvePatchChain(Dictionary<string, PatchEntry> lookup,
      string path, string hash, long size) {
      var result = new List<PatchEntry>();
      var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      PatchEntry entry;
      while ((entry = FindPatch(lookup, path, hash)) != null) {
        if (result.Count >= 16 || !visited.Add(PatchKey(path, hash)))
          throw new InvalidDataException("Cyclic or excessive language patch chain: " + path);
        if (entry.SourceSize != size)
          throw new InvalidDataException("Language patch-chain source size mismatch: " + path);
        result.Add(entry);
        hash = entry.TargetSha256; size = entry.TargetSize;
      }
      return result;
    }

    // Normalize through a bounded chain, then apply the Russian route if selected.
    // Passthrough is allowed only when the exact final canonical hash matches.
    List<string> BuildTree(string language, List<ManifestFile> files, PatchIndex index, string donorRoot,
      string targetRoot, string patchesRoot, bool translate, List<string> englishApplied,
      Action<InstallProgress> progress, CancellationToken cancellation, ref long completed, long total) {
      var translated = new List<string>();
      foreach (ManifestFile file in files) {
        cancellation.ThrowIfCancellationRequested();
        string donor = SafeJoin(donorRoot, file.Path);
        string output = SafeJoin(targetRoot, file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        string current = donor;
        string hash = file.Sha256;
        string intermediate = null;

        long size = file.Size;
        int step = 0;
        foreach (PatchEntry english in ResolvePatchChain(englishPatches, file.Path, hash, size)) {
          string next = output + ".english." + step++;
          ApplyPatch(english, current, next, patchesRoot, cancellation);
          if (intermediate != null) File.Delete(intermediate);
          current = intermediate = next;
          hash = english.TargetSha256;
          size = english.TargetSize;
        }
        if (step != 0) englishApplied.Add(file.Path);

        var russianRoute = translate ? ResolvePatchChain(russianPatches, file.Path, hash, size)
          : new List<PatchEntry>();
        step = 0;
        foreach (PatchEntry russian in russianRoute) {
          string produced = output + ".russian." + step++;
          ApplyPatch(russian, current, produced, patchesRoot, cancellation);
          if (intermediate != null) File.Delete(intermediate);
          current = intermediate = produced;
          hash = russian.TargetSha256; size = russian.TargetSize;
        }
        if (step != 0) translated.Add(file.Path);
        ManifestFile expected;
        var canonical = translate ? canonicalRussian : canonicalOriginal;
        if (!canonical.TryGetValue(file.Path, out expected) ||
            !SameHash(hash, expected.Sha256) || size != expected.Size)
          throw new InvalidDataException(language + " final language target is not verified: " + file.Path);
        if (intermediate != null) {
          File.Move(intermediate, output);
          completed += size;
        } else {
          if (!CreateHardLink(output, donor, IntPtr.Zero)) File.Copy(donor, output, false);
          completed += file.Size;
        }
        Report(progress, language + " data", file.Path, completed, total);
      }
      foreach (string required in RequiredFiles)
        if (!File.Exists(SafeJoin(targetRoot, required)))
          throw new InvalidDataException(language + " data is incomplete: " + required);
      return translated;
    }

    void ApplyPatch(PatchEntry entry, string source, string output, string patchesRoot, CancellationToken cancellation) {
      string delta = SafeJoin(patchesRoot, entry.Delta);
      if (!File.Exists(delta) || new FileInfo(delta).Length != entry.DeltaSize ||
          !SameHash(HashFile(delta, cancellation), entry.DeltaSha256))
        throw new InvalidDataException("Delta is missing or corrupted: " + entry.Delta);
      if (File.Exists(output)) File.Delete(output);
      EotpPatch.Apply(source, delta, output);
      if (new FileInfo(output).Length != entry.TargetSize ||
          !SameHash(HashFile(output, cancellation), entry.TargetSha256))
        throw new InvalidDataException("Patch verification failed: " + entry.Path);
    }

    void ValidatePayloadManifest(PayloadManifest manifest) {
      if (manifest == null || manifest.Schema != 1 || manifest.Files == null || manifest.Files.Count == 0)
        throw new InvalidDataException("Invalid PC Edition payload manifest");
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (ManifestFile file in manifest.Files) {
        file.Path = NormalizeRelative(file.Path);
        if (!seen.Add(file.Path) || file.Size < 0 || !IsSha256(file.Sha256))
          throw new InvalidDataException("Invalid payload entry: " + file.Path);
      }
      foreach (string required in new[] { "Launcher.exe", "SpiderManEOT.exe", "rexruntime.dll", "runtime.manifest.json" })
        if (!manifest.Files.Any(file => SamePath(file.Path, required)))
          throw new InvalidDataException("Payload is missing " + required);
    }

    // The index is the whole knowledge of what can be translated: a file with a
    // given sha256 becomes a given other file. It is keyed by hash, not by dump,
    // which is what lets an unknown revision install.
    void ValidatePatchIndex(PatchIndex index, string patchesRoot) {
      if (index == null || index.Schema != 4 || index.English == null || index.Russian == null ||
          !index.CanonicalTargetsVerified || index.IndexOnlyNotInstallable)
        throw new InvalidDataException("This payload has no verified full-language normalization (schema 4 required). Download the corrected payload; beta 1/3 language data must not be mixed with this installer.");
      englishPatches = BuildPatchLookup(index.English, "English");
      russianPatches = BuildPatchLookup(index.Russian, "Russian");
      if (russianPatches.Count == 0) throw new InvalidDataException("Patch index carries no translation");
      canonicalOriginal = BuildCanonicalLookup(index.CanonicalOriginal, "Original");
      canonicalRussian = BuildCanonicalLookup(index.CanonicalRussian, "Russian");
      if (canonicalOriginal.Keys.Except(canonicalRussian.Keys, StringComparer.OrdinalIgnoreCase).Any() ||
          canonicalRussian.Keys.Except(canonicalOriginal.Keys, StringComparer.OrdinalIgnoreCase).Any())
        throw new InvalidDataException("Canonical language inventories differ");
      foreach (PatchEntry entry in index.English.Concat(index.Russian)) {
        var lookup = index.English.Contains(entry) ? englishPatches : russianPatches;
        ResolvePatchChain(lookup, entry.Path, entry.SourceSha256, entry.SourceSize);
      }
    }

    Dictionary<string, ManifestFile> BuildCanonicalLookup(List<ManifestFile> entries, string lane) {
      if (entries == null || entries.Count == 0)
        throw new InvalidDataException("Missing canonical language inventory: " + lane);
      var result = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
      foreach (ManifestFile entry in entries) {
        entry.Path = NormalizeRelative(entry.Path);
        if (entry.Size < 0 || !IsSha256(entry.Sha256) || result.ContainsKey(entry.Path))
          throw new InvalidDataException("Invalid canonical language entry: " + entry.Path);
        result.Add(entry.Path, entry);
      }
      return result;
    }

    Dictionary<string, PatchEntry> BuildPatchLookup(List<PatchEntry> entries, string stage) {
      var lookup = new Dictionary<string, PatchEntry>(StringComparer.OrdinalIgnoreCase);
      foreach (PatchEntry entry in entries) {
        entry.Path = NormalizeRelative(entry.Path);
        entry.Delta = NormalizeRelative(entry.Delta);
        if (entry.SourceSize < 0 || entry.TargetSize < 0 || entry.DeltaSize <= 0 ||
            !IsSha256(entry.SourceSha256) || !IsSha256(entry.TargetSha256) || !IsSha256(entry.DeltaSha256))
          throw new InvalidDataException("Invalid " + stage + " patch entry: " + entry.Path);
        string key = PatchKey(entry.Path, entry.SourceSha256);
        if (lookup.ContainsKey(key))
          throw new InvalidDataException("Duplicate " + stage + " patch entry: " + entry.Path);
        lookup.Add(key, entry);
      }
      return lookup;
    }

    static string PatchKey(string path, string hash) {
      return NormalizeRelative(path).ToLowerInvariant() + "|" + hash.ToLowerInvariant();
    }

    static PatchEntry FindPatch(Dictionary<string, PatchEntry> lookup, string path, string hash) {
      PatchEntry entry;
      return lookup != null && hash != null && lookup.TryGetValue(PatchKey(path, hash), out entry) ? entry : null;
    }

    void WriteReceipt(string stage, IGameSource source, List<ManifestFile> files, GameManifest revision,
      PayloadManifest payload, PatchIndex index, Dictionary<string, List<string>> applied,
      int selectedLanguage, int effectiveLanguage) {
      string directory = Path.Combine(stage, "Support", "Install");
      Directory.CreateDirectory(directory);
      var deviations = new List<string>();
      if (revision != null) {
        foreach (ManifestFile file in files) {
          ManifestFile known = revision.Files.FirstOrDefault(value => SamePath(value.Path, file.Path));
          if (known != null && (known.Size != file.Size || !SameHash(known.Sha256, file.Sha256)))
            deviations.Add(file.Path);
        }
      }
      LastSourceDeviations = deviations;
      string text = json.Serialize(new {
        Schema = 2,
        InstalledUtc = DateTime.UtcNow.ToString("o"),
        SourceKind = source.Kind,
        SourceName = source.Name,
        Revision = revision == null ? "unknown-revision" : revision.Id,
        RevisionName = revision == null ? null : revision.Region,
        PortBuild = payload.Build,
        SourceFiles = files.Count,
        SourceBytes = files.Sum(file => file.Size),
        EnglishPatchesApplied = applied["Original"].Count,
        TranslationComplete = LastUntranslatedFiles.Count == 0,
        UntranslatableFiles = LastUntranslatedFiles.ToArray(),
        UntranslatableSourceHashes = LastUntranslatedFiles
          .Select(path => new {
            Path = path,
            Sha256 = files.Where(file => SamePath(file.Path, path))
              .Select(file => file.Sha256).FirstOrDefault()
          }).ToArray(),
        TranslatedFiles = applied["Russian"].OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
        TranslatableFiles = index.Russian.Select(entry => entry.Path)
          .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
        SourceDeviations = deviations.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
        DefaultLanguage = selectedLanguage,
        EffectiveLanguage = effectiveLanguage
      });
      File.WriteAllText(Path.Combine(directory, "INSTALL_RECEIPT.json"), text, new UTF8Encoding(false));
    }

    const int RussianLanguageId = 12;

    static string SetTomlValue(string text, string key, string line) {
      var expression = new Regex(@"(?m)^\s*" + Regex.Escape(key) + @"\s*=\s*[^\r\n]+$");
      return expression.IsMatch(text) ? expression.Replace(text, line, 1) : line + "\r\n" + text;
    }

    // The language picks the tree as well as the track: Russian text is only
    // legible with the Russian fonts, and both live in Data/Russian.
    // Where the launcher looks for changed-file updates. Written here so a
    // payload that predates a move cannot leave a fresh install unable to
    // receive one; the launcher still verifies every file it downloads.
    static void ApplyUpdateSources(string stage) {
      string path = Path.Combine(stage, "update.sources.json");
      if (!File.Exists(path)) return;
      const string document =
        "{\r\n" +
        "  \"Format\": \"genry.eot.update-sources\",\r\n" +
        "  \"Schema\": 1,\r\n" +
        "  \"Channel\": \"stable\",\r\n" +
        "  \"MinimumGeneration\": 10303,\r\n" +
        "  \"Sources\": [\r\n" +
        "    {\r\n" +
        "      \"Name\": \"GitHub\",\r\n" +
        "      \"Kind\": \"github\",\r\n" +
        "      \"Enabled\": true,\r\n" +
        "      \"ManifestUrl\": \"https://github.com/GenryTheFox0/Project-2099/releases/latest/download/EOT.update-manifest.json\",\r\n" +
        "      \"ContentBaseUrl\": \"https://github.com/GenryTheFox0/Project-2099/releases/download/\"\r\n" +
        "    },\r\n" +
        "    {\r\n" +
        "      \"Name\": \"GitLab\",\r\n" +
        "      \"Kind\": \"gitlab\",\r\n" +
        "      \"Enabled\": false,\r\n" +
        "      \"ManifestUrl\": \"\",\r\n" +
        "      \"ContentBaseUrl\": \"\"\r\n" +
        "    },\r\n" +
        "    {\r\n" +
        "      \"Name\": \"Static CDN\",\r\n" +
        "      \"Kind\": \"static-cdn\",\r\n" +
        "      \"Enabled\": false,\r\n" +
        "      \"ManifestUrl\": \"\",\r\n" +
        "      \"ContentBaseUrl\": \"\"\r\n" +
        "    }\r\n" +
        "  ]\r\n" +
        "}\r\n";
      File.WriteAllText(path, document, new UTF8Encoding(false));
    }

    static void ApplyDefaultLanguage(string stage, int language) {
      string config = Path.Combine(stage, "spider_man_edge_of_time.toml");
      if (!File.Exists(config)) throw new FileNotFoundException("PC Edition config missing", config);
      bool russian = language == RussianLanguageId;
      string text = File.ReadAllText(config, Encoding.UTF8);
      text = SetTomlValue(text, "user_language", "user_language = " + language);
      text = SetTomlValue(text, "game_data_root",
        "game_data_root = '" + (russian ? "Data/Russian" : "Data/Original") + "'");
      File.WriteAllText(config, text, new UTF8Encoding(false));
    }

    static string SafeJoin(string root, string relative) {
      string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      string full = Path.GetFullPath(Path.Combine(basePath, NormalizeRelative(relative).Replace('/', Path.DirectorySeparatorChar)));
      if (!full.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Path escaped its package root");
      return full;
    }

    static string HashFile(string path, CancellationToken cancellation) {
      using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        BufferSize, FileOptions.SequentialScan)) return HashStream(stream, stream.Length, cancellation, null,
          "local file: " + path);
    }

    static string CopyFileWithHash(string source, string target, long expectedSize,
      CancellationToken cancellation, Action<int> advanced, string context) {
      using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
        BufferSize, FileOptions.SequentialScan))
      using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
        BufferSize, FileOptions.SequentialScan))
        return CopyStreamWithHash(input, output, expectedSize, cancellation, advanced, context);
    }

    static string HashStream(Stream stream, long expectedSize, CancellationToken cancellation, Action<int> advanced,
      string context) {
      using (var hash = SHA256.Create()) {
        var buffer = new byte[BufferSize]; long total = 0;
        while (true) {
          cancellation.ThrowIfCancellationRequested();
          int read = stream.Read(buffer, 0, buffer.Length); if (read == 0) break;
          hash.TransformBlock(buffer, 0, read, null, 0); total += read; if (advanced != null) advanced(read);
        }
        hash.TransformFinalBlock(new byte[0], 0, 0);
        if (total != expectedSize) throw new EndOfStreamException(
          "File is incomplete or changed while being read: " + context +
          " (expected " + expectedSize + " bytes, read " + total + ")");
        return ToHex(hash.Hash);
      }
    }

    static string CopyStreamWithHash(Stream input, Stream output, long expectedSize,
      CancellationToken cancellation, Action<int> advanced, string context) {
      using (var hash = SHA256.Create()) {
        var buffer = new byte[BufferSize]; long total = 0;
        while (true) {
          cancellation.ThrowIfCancellationRequested();
          int read = input.Read(buffer, 0, buffer.Length); if (read == 0) break;
          output.Write(buffer, 0, read); hash.TransformBlock(buffer, 0, read, null, 0);
          total += read; if (advanced != null) advanced(read);
        }
        hash.TransformFinalBlock(new byte[0], 0, 0);
        if (total != expectedSize) throw new EndOfStreamException(
          "File is incomplete or changed while being read: " + context +
          " (expected " + expectedSize + " bytes, read " + total + ")");
        return ToHex(hash.Hash);
      }
    }

    static string ToHex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); }
    static bool SameHash(string a, string b) { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    static bool SamePath(string a, string b) { return String.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase); }
    static void Report(Action<InstallProgress> callback, string phase, string file, long completed, long total) {
      if (callback != null) callback(new InstallProgress { Phase = phase, CurrentFile = file, CompletedBytes = completed, TotalBytes = total });
    }
  }
}
