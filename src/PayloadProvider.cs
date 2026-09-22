using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EotInstaller {
  // GENRY V2 (2026-09-21, Shram): the PC Edition payload can come from anywhere,
  // in this order, and the installer never dead-ends on a missing download:
  //   1. a path the user picked by hand (payload folder or payload ZIP);
  //   2. an extracted "payload" folder next to the installer (or one level up);
  //   3. the local cache of an earlier run (LocalAppData, hash-verified);
  //   4. a payload ZIP lying next to the installer, in its "payload" folder or
  //      in the user's Downloads (offline distribution: installer + ZIP);
  //   5. the release channel: PayloadUrl, then every mirror in order.
  // The channel is embedded at build time; a release-channel.json placed next to
  // the installer overrides it, so the download can be re-pointed without a
  // rebuild. Whatever the origin, a ZIP is accepted only when its SHA-256
  // matches the channel (or, for a hand-picked ZIP with no channel, when it
  // contains a payload-manifest.json that the installer then verifies per file).
  public sealed class PayloadUnavailableException : Exception {
    public PayloadUnavailableException(string message) : base(message) { }
  }

  public sealed class PayloadProvider {
    const string ChannelResource = "EOT.release-channel.json";
    const string ChannelOverrideName = "release-channel.json";
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
    readonly List<string> report = new List<string>();

    public string ChannelVersion {
      get { string error; ReleaseChannel channel = TryLoadChannel(out error); return channel != null ? channel.Version : "development"; }
    }

    // Everything the last ResolveAsync tried, one line each, for the UI.
    public string LastReport { get { return String.Join("\n", report); } }

    // Where a user should drop the ZIP for an offline install.
    public string OfflineDropFolder { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'); } }

    public string ExpectedArchiveName {
      get { string error; ReleaseChannel channel = TryLoadChannel(out error); return channel == null ? "EOT-PC-Payload-*.zip" : AssetName(channel); }
    }

    public Task<string> ResolveAsync(Action<InstallProgress> progress, CancellationToken cancellation) {
      return ResolveAsync(null, progress, cancellation);
    }

    public async Task<string> ResolveAsync(string manualPath, Action<InstallProgress> progress, CancellationToken cancellation) {
      report.Clear();
      string channelError;
      ReleaseChannel channel = TryLoadChannel(out channelError);
      if (channel == null) report.Add("release channel: " + channelError);
      else report.Add("release channel: " + channel.Version + " (" + (1 + channel.Mirrors.Count) + " source(s))");

      // 1. Hand-picked folder or ZIP.
      if (!String.IsNullOrWhiteSpace(manualPath)) {
        string full = Path.GetFullPath(manualPath.Trim().Trim('"'));
        if (Directory.Exists(full)) {
          if (IsPayload(full)) { Report(progress, "Локальный payload", full, 1, 1); report.Add("manual folder: " + full); return full; }
          string nested = Path.Combine(full, "payload");
          if (IsPayload(nested)) { Report(progress, "Локальный payload", nested, 1, 1); report.Add("manual folder: " + nested); return nested; }
          throw new PayloadUnavailableException("В папке нет payload-manifest.json, port и patches: " + full);
        }
        if (File.Exists(full)) {
          report.Add("manual archive: " + full);
          return await ImportArchiveAsync(full, channel, false, progress, cancellation);
        }
        throw new PayloadUnavailableException("Путь не существует: " + full);
      }

      // 2. Extracted payload folder next to the installer.
      foreach (string local in LocalPayloadFolders()) {
        if (IsPayload(local)) { Report(progress, "Локальный payload", local, 1, 1); report.Add("local folder: " + local); return local; }
      }
      report.Add("local folder: none (" + String.Join("; ", LocalPayloadFolders()) + ")");

      // 3. Verified cache of an earlier run.
      if (channel != null) {
        string cache = CacheFolder(channel);
        if (IsPayload(cache) && File.Exists(ReadyMarker(cache)) &&
            String.Equals(File.ReadAllText(ReadyMarker(cache)).Trim(), channel.PayloadSha256, StringComparison.OrdinalIgnoreCase)) {
          Report(progress, "Payload уже загружен", channel.Version, channel.PayloadSize, channel.PayloadSize);
          report.Add("cache: " + cache);
          return cache;
        }
      }

      // 4. Offline ZIP next to the installer or in Downloads.
      foreach (string archive in LocalArchives(channel)) {
        cancellation.ThrowIfCancellationRequested();
        try {
          report.Add("local archive: " + archive);
          return await ImportArchiveAsync(archive, channel, channel != null, progress, cancellation);
        } catch (OperationCanceledException) { throw; }
        catch (Exception error) { report.Add("  rejected: " + error.Message); }
      }

      // 5. Download.
      if (channel == null) throw new PayloadUnavailableException(OfflineHint(null));
      var failures = new List<string>();
      foreach (string url in DownloadUrls(channel)) {
        cancellation.ThrowIfCancellationRequested();
        try {
          report.Add("download: " + url);
          string archive = await DownloadAsync(url, channel, progress, cancellation);
          return await ImportArchiveAsync(archive, channel, true, progress, cancellation);
        } catch (OperationCanceledException) { throw; }
        catch (Exception error) {
          string line = ShortHost(url) + " — " + error.Message;
          failures.Add(line); report.Add("  failed: " + error.Message);
        }
      }
      throw new PayloadUnavailableException(OfflineHint(channel) + "\n\n" + String.Join("\n", failures));
    }

    // ------------------------------------------------------------------ channel

    ReleaseChannel TryLoadChannel(out string error) {
      error = null;
      try {
        ReleaseChannel channel = LoadChannel();
        ValidateChannel(channel);
        return channel;
      } catch (Exception failure) { error = failure.Message; return null; }
    }

    ReleaseChannel LoadChannel() {
#if EOT_INSTALLER_TEST
      string overridePath = Environment.GetEnvironmentVariable("EOT_INSTALLER_TEST_CHANNEL");
      if (!String.IsNullOrWhiteSpace(overridePath))
        return json.Deserialize<ReleaseChannel>(File.ReadAllText(overridePath, Encoding.UTF8));
#endif
      foreach (string candidate in new[] {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ChannelOverrideName),
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ChannelOverrideName))
      }) if (File.Exists(candidate)) return json.Deserialize<ReleaseChannel>(File.ReadAllText(candidate, Encoding.UTF8));
      using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ChannelResource)) {
        if (stream == null) throw new InvalidOperationException(
          "development build without an embedded release channel; use a local payload folder or ZIP");
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
          return json.Deserialize<ReleaseChannel>(reader.ReadToEnd());
      }
    }

    static void ValidateChannel(ReleaseChannel channel) {
      if (channel == null || channel.Schema != 1 || String.IsNullOrWhiteSpace(channel.Version) ||
          channel.PayloadSize <= 0 || channel.PayloadSha256 == null || channel.PayloadSha256.Length != 64)
        throw new InvalidDataException("release channel is malformed");
      if (channel.Mirrors == null) channel.Mirrors = new List<string>();
      foreach (string url in DownloadUrls(channel)) {
        Uri uri;
        bool ok = Uri.TryCreate(url, UriKind.Absolute, out uri) &&
          (uri.Scheme == Uri.UriSchemeHttps
#if EOT_INSTALLER_TEST
           || (uri.Scheme == Uri.UriSchemeHttp && (uri.Host == "127.0.0.1" || uri.Host == "localhost"))
#endif
          );
        if (!ok) throw new InvalidDataException("release channel URL is not HTTPS: " + url);
      }
    }

    static IEnumerable<string> DownloadUrls(ReleaseChannel channel) {
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (string url in new[] { channel.PayloadUrl }.Concat(channel.Mirrors ?? new List<string>()))
        if (!String.IsNullOrWhiteSpace(url) && seen.Add(url.Trim())) yield return url.Trim();
    }

    static string AssetName(ReleaseChannel channel) {
      try {
        string leaf = Path.GetFileName(new Uri(channel.PayloadUrl).LocalPath);
        if (!String.IsNullOrWhiteSpace(leaf) && leaf.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return leaf;
      } catch { }
      return "EOT-PC-Payload-" + SafeName(channel.Version) + ".zip";
    }

    static string ShortHost(string url) { try { return new Uri(url).Host; } catch { return url; } }

    string OfflineHint(ReleaseChannel channel) {
      string name = channel == null ? "EOT-PC-Payload-<версия>.zip" : AssetName(channel);
      return "Файлы PC Edition не найдены ни рядом с установщиком, ни в Загрузках, а скачать их не удалось. " +
        "Положите " + name + " рядом с установщиком (" + OfflineDropFolder + ") и нажмите «Повторить», " +
        "либо укажите ZIP или папку payload вручную.";
    }

    // ------------------------------------------------------------------ places

    static IEnumerable<string> LocalPayloadFolders() {
      string exeDir = AppDomain.CurrentDomain.BaseDirectory;
      yield return Path.Combine(exeDir, "payload");
      yield return Path.GetFullPath(Path.Combine(exeDir, "..", "payload"));
      yield return Path.GetFullPath(Path.Combine(exeDir, "..", "..", "payload"));
    }

    static IEnumerable<string> ArchiveFolders() {
      string exeDir = AppDomain.CurrentDomain.BaseDirectory;
      yield return exeDir;
      yield return Path.Combine(exeDir, "payload");
      yield return Path.GetFullPath(Path.Combine(exeDir, ".."));
      string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      if (!String.IsNullOrEmpty(profile)) yield return Path.Combine(profile, "Downloads");
      yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
      yield return DownloadsCache();
    }

    IEnumerable<string> LocalArchives(ReleaseChannel channel) {
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      string preferred = channel == null ? null : AssetName(channel);
      foreach (string folder in ArchiveFolders()) {
        if (String.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(folder, "*.zip", SearchOption.TopDirectoryOnly).ToList(); }
        catch { continue; }
        // The channel's own asset name first, then anything that looks like a payload ZIP.
        foreach (string file in files.OrderByDescending(f => preferred != null && String.Equals(Path.GetFileName(f), preferred, StringComparison.OrdinalIgnoreCase))
                                     .ThenByDescending(f => Path.GetFileName(f).StartsWith("EOT-PC-Payload", StringComparison.OrdinalIgnoreCase))
                                     .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)) {
          string name = Path.GetFileName(file);
          bool looksRight = (preferred != null && String.Equals(name, preferred, StringComparison.OrdinalIgnoreCase)) ||
            name.StartsWith("EOT-PC-Payload", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Project2099-Payload", StringComparison.OrdinalIgnoreCase) ||
            (channel != null && SafeLength(file) == channel.PayloadSize);
          if (looksRight && seen.Add(Path.GetFullPath(file))) yield return Path.GetFullPath(file);
        }
      }
    }

    static long SafeLength(string file) { try { return new FileInfo(file).Length; } catch { return -1; } }

    /// <summary>Where the game is being installed. The payload is cached beside
    /// it when the system drive cannot hold it, which is the common case on a
    /// machine whose C: is full and whose games live elsewhere.</summary>
    public static string DestinationHint;

    static string ProfileRoot() {
#if EOT_INSTALLER_TEST
      string testRoot = Environment.GetEnvironmentVariable("EOT_INSTALLER_TEST_APPDATA");
      if (!String.IsNullOrWhiteSpace(testRoot))
        return Path.GetFullPath(testRoot);
#endif
      return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenryTheFox", "EOTInstaller");
    }

    static long FreeSpace(string path) {
      try {
        string root = Path.GetPathRoot(Path.GetFullPath(path));
        if (String.IsNullOrEmpty(root)) return -1;
        return new DriveInfo(root).AvailableFreeSpace;
      } catch { return -1; }
    }

    static string DriveName(string path) {
      try { return Path.GetPathRoot(Path.GetFullPath(path)).TrimEnd('\\'); } catch { return path; }
    }

    static string Gigabytes(long bytes) {
      return (bytes / 1073741824.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ГБ";
    }

    // The archive is kept while it is unpacked, so both have to fit at once.
    static long NeededBytes(ReleaseChannel channel) {
      long payload = channel == null || channel.PayloadSize <= 0 ? 1073741824L : channel.PayloadSize;
      return payload * 2 + 268435456L;
    }

    static string AppDataRoot() { return CacheRoot(null); }

    static string CacheRoot(ReleaseChannel channel) {
      string profile = ProfileRoot();
      long needed = NeededBytes(channel);
      string destination = DestinationHint;
      if (!String.IsNullOrWhiteSpace(destination)) {
        try {
          string beside = Path.Combine(Path.GetPathRoot(Path.GetFullPath(destination)) ?? "", "EOTInstallerCache");
          if (!String.IsNullOrWhiteSpace(Path.GetPathRoot(beside))) {
            bool profileFits = FreeSpace(profile) >= needed;
            if (!profileFits && FreeSpace(beside) >= needed) return beside;
          }
        } catch { }
      }
      return profile;
    }

    void RequireRoomFor(ReleaseChannel channel) {
      string root = CacheRoot(channel);
      long needed = NeededBytes(channel), free = FreeSpace(root);
      if (free >= 0 && free < needed)
        throw new IOException("Не хватает места на диске " + DriveName(root) + ": нужно " + Gigabytes(needed) +
          ", свободно " + Gigabytes(free) + ". Освободите место или выберите папку установки на другом диске.");
    }
    static string DownloadsCache() { return Path.Combine(AppDataRoot(), "downloads"); }
    static string DownloadsCache(ReleaseChannel channel) { return Path.Combine(CacheRoot(channel), "downloads"); }
    static string CacheFolder(ReleaseChannel channel) { return Path.Combine(CacheRoot(channel), "payloads", SafeName(channel.Version)); }
    static string ReadyMarker(string cache) { return cache + ".ready"; }

    // ---------------------------------------------------------------- download

    async Task<string> DownloadAsync(string url, ReleaseChannel channel, Action<InstallProgress> progress, CancellationToken cancellation) {
      RequireRoomFor(channel);
      Directory.CreateDirectory(DownloadsCache(channel));
      string archive = Path.Combine(DownloadsCache(channel), AssetName(channel));
      string part = archive + ".part";
      if (File.Exists(archive) && SafeLength(archive) == channel.PayloadSize &&
          String.Equals(HashFile(archive, progress, "Проверка архива", cancellation), channel.PayloadSha256, StringComparison.OrdinalIgnoreCase))
        return archive;
      if (File.Exists(archive)) File.Delete(archive);
      if (File.Exists(part) && SafeLength(part) > channel.PayloadSize) File.Delete(part);

      ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
      using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.None })) {
        client.Timeout = TimeSpan.FromHours(6);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Project2099-Installer/2.0 (+https://t.me/teamgenrythefox)");
        long resumeAt = File.Exists(part) ? SafeLength(part) : 0;
        using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
          if (resumeAt > 0) request.Headers.Range = new RangeHeaderValue(resumeAt, null);
          using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)) {
            if (response.StatusCode == HttpStatusCode.NotFound) throw new FileNotFoundException("HTTP 404 — файл на сервере отсутствует");
            response.EnsureSuccessStatusCode();
            bool resumed = resumeAt > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed) resumeAt = 0;
            long announced = response.Content.Headers.ContentLength ?? (channel.PayloadSize - resumeAt);
            if (resumeAt + announced != channel.PayloadSize)
              throw new InvalidDataException("размер на сервере " + (resumeAt + announced) + " не совпадает с каналом " + channel.PayloadSize);
            using (Stream input = await response.Content.ReadAsStreamAsync())
            using (var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan)) {
              var buffer = new byte[1 << 20]; long done = resumeAt;
              while (true) {
                cancellation.ThrowIfCancellationRequested();
                int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation); if (read == 0) break;
                await output.WriteAsync(buffer, 0, read, cancellation); done += read;
                Report(progress, "Скачивание PC Edition", ShortHost(url) + "  " + channel.Version, done, channel.PayloadSize);
              }
            }
          }
        }
      }
      if (SafeLength(part) != channel.PayloadSize ||
          !String.Equals(HashFile(part, progress, "Проверка архива", cancellation), channel.PayloadSha256, StringComparison.OrdinalIgnoreCase)) {
        File.Delete(part); throw new InvalidDataException("скачанный архив не прошёл проверку SHA-256");
      }
      File.Move(part, archive);
      return archive;
    }

    // ----------------------------------------------------------------- import

    async Task<string> ImportArchiveAsync(string archive, ReleaseChannel channel, bool requireChannelMatch,
      Action<InstallProgress> progress, CancellationToken cancellation) {
      if (!File.Exists(archive)) throw new FileNotFoundException("архив не найден", archive);
      bool verified = false;
      if (channel != null) {
        if (SafeLength(archive) == channel.PayloadSize) {
          string hash = HashFile(archive, progress, "Проверка архива", cancellation);
          verified = String.Equals(hash, channel.PayloadSha256, StringComparison.OrdinalIgnoreCase);
          if (!verified && requireChannelMatch) throw new InvalidDataException("SHA-256 архива не совпадает с выпуском " + channel.Version);
        } else if (requireChannelMatch) {
          throw new InvalidDataException("размер " + SafeLength(archive) + " не совпадает с выпуском " + channel.Version + " (" + channel.PayloadSize + ")");
        }
      }
      // A verified archive lands in the versioned cache; anything else gets its own folder keyed by content hash.
      string cacheBase = verified ? CacheFolder(channel)
        : Path.Combine(AppDataRoot(), "payloads", "manual-" + HashFile(archive, progress, "Проверка архива", cancellation).Substring(0, 16));
      if (IsPayload(cacheBase) && File.Exists(ReadyMarker(cacheBase))) {
        Report(progress, "Payload уже загружен", archive, 1, 1);
        return cacheBase;
      }
      string stage = cacheBase + ".extracting-" + Guid.NewGuid().ToString("N");
      try {
        Directory.CreateDirectory(stage);
        long done = 0;
        using (var zip = ZipFile.OpenRead(archive)) {
          long total = 0; foreach (ZipArchiveEntry entry in zip.Entries) total += entry.Length;
          foreach (ZipArchiveEntry entry in zip.Entries) {
            cancellation.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('\\', '/').Trim('/');
            if (relative.Length == 0) continue;
            string output = SafeZipPath(stage, relative);
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(output); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (Stream input = entry.Open())
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan)) {
              var buffer = new byte[1 << 20];
              while (true) {
                int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation); if (read == 0) break;
                await file.WriteAsync(buffer, 0, read, cancellation); done += read;
                Report(progress, "Распаковка payload", relative, done, total);
              }
            }
          }
        }
        // build_release.ps1 zips the "payload" folder itself; accept a root-level layout too.
        string extracted = IsPayload(Path.Combine(stage, "payload")) ? Path.Combine(stage, "payload") : IsPayload(stage) ? stage : null;
        if (extracted == null) throw new InvalidDataException("в архиве нет папки payload с payload-manifest.json, port и patches");
        if (Directory.Exists(cacheBase)) Directory.Delete(cacheBase, true);
        if (extracted == stage) { Directory.Move(stage, cacheBase); }
        else { Directory.Move(extracted, cacheBase); Directory.Delete(stage, true); }
        File.WriteAllText(ReadyMarker(cacheBase), verified ? channel.PayloadSha256 : "manual", new UTF8Encoding(false));
        Report(progress, "Локальный payload", cacheBase, 1, 1);
        return cacheBase;
      } catch {
        try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
        throw;
      }
    }

    // ---------------------------------------------------------------- helpers

    static bool IsPayload(string path) {
      return Directory.Exists(path) && File.Exists(Path.Combine(path, "payload-manifest.json")) &&
        Directory.Exists(Path.Combine(path, "port")) && Directory.Exists(Path.Combine(path, "patches"));
    }

    static string SafeName(string value) {
      var builder = new StringBuilder();
      foreach (char c in value) builder.Append(Char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
      return builder.ToString();
    }

    static string SafeZipPath(string root, string relative) {
      if (relative.Split('/').AnyPartUnsafe()) throw new InvalidDataException("Unsafe ZIP path");
      string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      string result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
      if (!result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP path escaped extraction root");
      return result;
    }

    static string HashFile(string path, Action<InstallProgress> progress, string phase, CancellationToken cancellation) {
      using (var hash = SHA256.Create())
      using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan)) {
        var buffer = new byte[1 << 20]; long done = 0, total = stream.Length; int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
          cancellation.ThrowIfCancellationRequested();
          hash.TransformBlock(buffer, 0, read, null, 0); done += read;
          Report(progress, phase, Path.GetFileName(path), done, total);
        }
        hash.TransformFinalBlock(buffer, 0, 0);
        return BitConverter.ToString(hash.Hash).Replace("-", "");
      }
    }

    static void Report(Action<InstallProgress> callback, string phase, string file, long done, long total) {
      if (callback != null) callback(new InstallProgress { Phase = phase, CurrentFile = file, CompletedBytes = done, TotalBytes = total });
    }
  }
}
