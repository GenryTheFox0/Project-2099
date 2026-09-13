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
    public void Dispose() { image.Dispose(); }
  }

  sealed class SvodGameSource : IGameSource {
    readonly SvodImage image;
    public SvodGameSource(string path) { image = new SvodImage(path); }
    public string Kind { get { return "xbox360-god-svod"; } }
    public string Name { get { return image.Name; } }
    public bool Exists(string relativePath) { return image.Exists(relativePath); }
    public long GetLength(string relativePath) { return image.GetLength(relativePath); }
    public Stream OpenRead(string relativePath) { return image.OpenRead(relativePath); }
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
    public void Dispose() { archive.Dispose(); stream.Dispose(); }
  }

  public sealed class InstallerCore {
    static readonly string[] EmbeddedManifests = {
      "EOT.source-manifest.eu.json",
      "EOT.source-manifest.ru-god.json",
      "EOT.source-manifest.usa-europe.json"
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

    static string FindSinglePackagedSource(string selectedPath) {
      if (!Directory.Exists(selectedPath)) return null;
      string[] files;
      try {
        files = Directory.GetFiles(selectedPath).Where(file => {
          string extension = Path.GetExtension(file);
          return String.Equals(extension, ".iso", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase);
        }).Take(3).ToArray();
      } catch (UnauthorizedAccessException) { return null; }
      catch (IOException) { return null; }
      return files.Length == 1 ? files[0] : null;
    }

    IGameSource OpenSource(string path) {
      string gameRoot = FindDirectoryGameRoot(path);
      if (gameRoot != null) return new DirectoryGameSource(gameRoot);
      string container;
      if (SvodImage.TryFindContainer(path, out container)) return new SvodGameSource(container);
      string packaged = File.Exists(path) ? Path.GetFullPath(path) : FindSinglePackagedSource(path);
      if (packaged != null) {
        string extension = Path.GetExtension(packaged);
        if (String.Equals(extension, ".iso", StringComparison.OrdinalIgnoreCase)) return new IsoGameSource(packaged);
        if (String.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)) return new ZipGameSource(packaged);
      }
      throw new InvalidDataException(
        "Выбери Xbox 360 USA/Europe ISO, ZIP, GOD/00007000 или внешнюю папку, внутри которой находится Default.xex и каталог Data");
    }

    public Task<SourceProbe> ProbeAsync(string sourcePath, Action<InstallProgress> progress,
      CancellationToken cancellation) {
      return Task.Run(() => {
        using (IGameSource source = OpenSource(sourcePath)) {
          GameManifest selected = IdentifySource(source, progress, cancellation);
          return new SourceProbe {
            ManifestId = selected.Id,
            Kind = source.Kind,
            DisplayName = source.Name,
            Region = selected.Region,
            FilesFound = selected.Files.Count,
            RequiredBytes = selected.Files.Sum(file => file.Size)
          };
        }
      }, cancellation);
    }

    GameManifest IdentifySource(IGameSource source, Action<InstallProgress> progress,
      CancellationToken cancellation) {
      foreach (GameManifest manifest in gameManifests) {
        var checks = manifest.QuickChecks.Select(path => manifest.Files.FirstOrDefault(file =>
          String.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (checks.Any(file => file == null)) throw new InvalidDataException("Quick-check manifest is inconsistent: " + manifest.Id);
        if (checks.Any(file => !source.Exists(file.Path) || source.GetLength(file.Path) != file.Size)) continue;
        long completed = 0, total = checks.Sum(file => file.Size); bool matches = true;
        foreach (ManifestFile file in checks) {
          cancellation.ThrowIfCancellationRequested();
          string hash;
          using (Stream input = source.OpenRead(file.Path))
            hash = HashStream(input, file.Size, cancellation, bytes => {
              completed += bytes; Report(progress, "Проверка источника " + manifest.Id, file.Path, completed, total);
            });
          if (!SameHash(hash, file.Sha256)) { matches = false; break; }
        }
        if (matches) return manifest;
      }
      throw new InvalidDataException("Ревизия игры не поддерживается: контрольные файлы не совпали ни с одним известным донором");
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
      using (IGameSource source = OpenSource(sourcePath)) {
        GameManifest sourceManifest = IdentifySource(source, progress, cancellation);
        string variantRoot = Path.Combine(patchesRoot, sourceManifest.Id);
        PatchManifest originalPatches = LoadJson<PatchManifest>(Path.Combine(variantRoot, "original.json"));
        PatchManifest russianPatches = LoadJson<PatchManifest>(Path.Combine(variantRoot, "russian.json"));
        ValidatePatchManifest(originalPatches, sourceManifest);
        ValidatePatchManifest(russianPatches, sourceManifest);
        long sourceBytes = sourceManifest.Files.Sum(file => file.Size);
        long originalBytes = TargetTreeBytes(sourceManifest, originalPatches);
        long russianBytes = TargetTreeBytes(sourceManifest, russianPatches);
        long total = payload.Files.Sum(file => file.Size) + sourceBytes + originalBytes + russianBytes;
        long completed = 0;
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
            });
            if (!SameHash(hash, file.Sha256)) throw new InvalidDataException("Payload hash mismatch: " + relative);
          }

          string donorRoot = Path.Combine(stage, ".source");
          foreach (ManifestFile file in sourceManifest.Files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)) {
            cancellation.ThrowIfCancellationRequested();
            if (!source.Exists(file.Path) || source.GetLength(file.Path) != file.Size)
              throw new InvalidDataException("Source file missing or wrong size: " + file.Path);
            string output = SafeJoin(donorRoot, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            string hash;
            using (Stream input = source.OpenRead(file.Path))
            using (var targetStream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None,
              BufferSize, FileOptions.SequentialScan)) {
              hash = CopyStreamWithHash(input, targetStream, file.Size, cancellation, bytes => {
                completed += bytes; Report(progress, "Распаковка источника", file.Path, completed, total);
              });
            }
            if (!SameHash(hash, file.Sha256)) throw new InvalidDataException("Source hash mismatch: " + file.Path);
          }

          BuildLanguageTree("Original", sourceManifest, originalPatches, donorRoot,
            Path.Combine(stage, "Data", "Original"), variantRoot, progress, cancellation, ref completed, total);
          BuildLanguageTree("Russian", sourceManifest, russianPatches, donorRoot,
            Path.Combine(stage, "Data", "Russian"), variantRoot, progress, cancellation, ref completed, total);
          Directory.Delete(donorRoot, true);
          ApplyDefaultLanguage(stage, selectedLanguage);
          WriteReceipt(stage, source, sourceManifest, payload, originalPatches, russianPatches, selectedLanguage);

          cancellation.ThrowIfCancellationRequested();
          if (Directory.Exists(target)) Directory.Delete(target, false);
          Directory.Move(stage, target);
          WriteLauncherFirstRunLanguage(selectedLanguage);
          Report(progress, "Готово", "Launcher.exe", total, total);
        } catch {
          try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
          throw;
        }
      }
    }

    static long TargetTreeBytes(GameManifest source, PatchManifest patches) {
      return source.Files.Sum(file => {
        PatchFile patch = patches.Files.FirstOrDefault(value => SamePath(value.Path, file.Path));
        return patch == null ? file.Size : patch.TargetSize;
      });
    }

    void BuildLanguageTree(string language, GameManifest sourceManifest, PatchManifest patches,
      string donorRoot, string targetRoot, string patchesRoot, Action<InstallProgress> progress,
      CancellationToken cancellation, ref long completed, long total) {
      var patchMap = patches.Files.ToDictionary(value => NormalizeRelative(value.Path), StringComparer.OrdinalIgnoreCase);
      foreach (ManifestFile file in sourceManifest.Files.OrderBy(value => value.Path, StringComparer.OrdinalIgnoreCase)) {
        cancellation.ThrowIfCancellationRequested();
        string donor = SafeJoin(donorRoot, file.Path);
        string output = SafeJoin(targetRoot, file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        PatchFile patch;
        if (patchMap.TryGetValue(file.Path, out patch)) {
          string delta = SafeJoin(patchesRoot, patch.Delta);
          if (!File.Exists(delta) || new FileInfo(delta).Length != patch.DeltaSize ||
              !SameHash(HashFile(delta, cancellation), patch.DeltaSha256))
            throw new InvalidDataException("Delta is missing or corrupted: " + patch.Delta);
          string temporary = output + ".tmp";
          EotpPatch.Apply(donor, delta, temporary);
          if (new FileInfo(temporary).Length != patch.TargetSize ||
              !SameHash(HashFile(temporary, cancellation), patch.TargetSha256))
            throw new InvalidDataException(language + " patch verification failed: " + file.Path);
          File.Move(temporary, output);
          completed += patch.TargetSize;
        } else {
          if (!CreateHardLink(output, donor, IntPtr.Zero)) File.Copy(donor, output, false);
          completed += file.Size;
        }
        Report(progress, language + " data", file.Path, completed, total);
      }
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

    void ValidatePatchManifest(PatchManifest manifest, GameManifest sourceManifest) {
      if (manifest == null || manifest.Schema != 1 || manifest.Files == null)
        throw new InvalidDataException("Invalid Russian patch manifest");
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (PatchFile patch in manifest.Files) {
        patch.Path = NormalizeRelative(patch.Path);
        patch.Delta = NormalizeRelative(patch.Delta);
        ManifestFile source = sourceManifest.Files.FirstOrDefault(file => SamePath(file.Path, patch.Path));
        if (source == null || !seen.Add(patch.Path) || patch.SourceSize != source.Size ||
            !SameHash(patch.SourceSha256, source.Sha256) || patch.TargetSize < 0 || patch.DeltaSize <= 0 ||
            !IsSha256(patch.TargetSha256) || !IsSha256(patch.DeltaSha256))
          throw new InvalidDataException("Invalid Russian patch entry: " + patch.Path);
      }
    }

    void WriteReceipt(string stage, IGameSource source, GameManifest sourceManifest, PayloadManifest payload,
      PatchManifest originalPatches, PatchManifest russianPatches, int selectedLanguage) {
      string directory = Path.Combine(stage, "Support", "Install");
      Directory.CreateDirectory(directory);
      string text = json.Serialize(new {
        Schema = 1,
        InstalledUtc = DateTime.UtcNow.ToString("o"),
        SourceKind = source.Kind,
        SourceName = source.Name,
        SourceManifest = sourceManifest.Id,
        TitleId = sourceManifest.TitleId,
        Region = sourceManifest.Region,
        PortBuild = payload.Build,
        SourceFiles = sourceManifest.Files.Count,
        OriginalDeltas = originalPatches.Files.Count,
        RussianDeltas = russianPatches.Files.Count,
        DefaultLanguage = selectedLanguage
      });
      File.WriteAllText(Path.Combine(directory, "INSTALL_RECEIPT.json"), text, new UTF8Encoding(false));
    }

    static void ApplyDefaultLanguage(string stage, int language) {
      string config = Path.Combine(stage, "spider_man_edge_of_time.toml");
      if (!File.Exists(config)) return;
      string text = File.ReadAllText(config, Encoding.UTF8);
      var expression = new Regex(@"(?m)^\s*user_language\s*=\s*[^\r\n]+$");
      string line = "user_language = " + language;
      text = expression.IsMatch(text) ? expression.Replace(text, line, 1) : line + "\r\n" + text;
      File.WriteAllText(config, text, new UTF8Encoding(false));
    }

    void WriteLauncherFirstRunLanguage(int language) {
      string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "spider_man_edge_of_time");
      string settings = Path.Combine(directory, "launcher_settings.json");
      if (File.Exists(settings)) return;
      Directory.CreateDirectory(directory);
      string text = json.Serialize(new { Language = language });
      File.WriteAllText(settings, text, new UTF8Encoding(false));
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
        BufferSize, FileOptions.SequentialScan)) return HashStream(stream, stream.Length, cancellation, null);
    }

    static string CopyFileWithHash(string source, string target, long expectedSize,
      CancellationToken cancellation, Action<int> advanced) {
      using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
        BufferSize, FileOptions.SequentialScan))
      using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
        BufferSize, FileOptions.SequentialScan))
        return CopyStreamWithHash(input, output, expectedSize, cancellation, advanced);
    }

    static string HashStream(Stream stream, long expectedSize, CancellationToken cancellation, Action<int> advanced) {
      using (var hash = SHA256.Create()) {
        var buffer = new byte[BufferSize]; long total = 0;
        while (true) {
          cancellation.ThrowIfCancellationRequested();
          int read = stream.Read(buffer, 0, buffer.Length); if (read == 0) break;
          hash.TransformBlock(buffer, 0, read, null, 0); total += read; if (advanced != null) advanced(read);
        }
        hash.TransformFinalBlock(new byte[0], 0, 0);
        if (total != expectedSize) throw new EndOfStreamException("Unexpected source size");
        return ToHex(hash.Hash);
      }
    }

    static string CopyStreamWithHash(Stream input, Stream output, long expectedSize,
      CancellationToken cancellation, Action<int> advanced) {
      using (var hash = SHA256.Create()) {
        var buffer = new byte[BufferSize]; long total = 0;
        while (true) {
          cancellation.ThrowIfCancellationRequested();
          int read = input.Read(buffer, 0, buffer.Length); if (read == 0) break;
          output.Write(buffer, 0, read); hash.TransformBlock(buffer, 0, read, null, 0);
          total += read; if (advanced != null) advanced(read);
        }
        hash.TransformFinalBlock(new byte[0], 0, 0);
        if (total != expectedSize) throw new EndOfStreamException("Unexpected source size");
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
