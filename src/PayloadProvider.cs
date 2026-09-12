using System;
using System.IO;
using System.IO.Compression;
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
  public sealed class PayloadProvider {
    const string ChannelResource = "EOT.release-channel.json";
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };

    public string ChannelVersion {
      get { try { return LoadChannel().Version; } catch { return "development"; } }
    }

    public async Task<string> ResolveAsync(Action<InstallProgress> progress, CancellationToken cancellation) {
      foreach (string local in new[] {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload"),
        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "payload"))
      }) if (IsPayload(local)) {
        Report(progress, "Локальный payload", "payload-manifest.json", 1, 1);
        return local;
      }

      ReleaseChannel channel = LoadChannel();
      ValidateChannel(channel);
      string cacheBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GenryTheFox", "EOTInstaller", "payloads", SafeName(channel.Version));
      string ready = Path.Combine(cacheBase, ".ready");
      if (IsPayload(cacheBase) && File.Exists(ready) &&
          String.Equals(File.ReadAllText(ready).Trim(), channel.PayloadSha256, StringComparison.OrdinalIgnoreCase)) {
        Report(progress, "Payload уже загружен", channel.Version, channel.PayloadSize, channel.PayloadSize);
        return cacheBase;
      }

      string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GenryTheFox", "EOTInstaller", "downloads");
      Directory.CreateDirectory(downloads);
      string archive = Path.Combine(downloads, "EOT-PC-Payload-" + SafeName(channel.Version) + ".zip");
      string temporaryArchive = archive + ".part";
      if (!File.Exists(archive) || new FileInfo(archive).Length != channel.PayloadSize ||
          !String.Equals(HashFile(archive), channel.PayloadSha256, StringComparison.OrdinalIgnoreCase)) {
        if (File.Exists(archive)) File.Delete(archive);
        if (File.Exists(temporaryArchive) && new FileInfo(temporaryArchive).Length > channel.PayloadSize) File.Delete(temporaryArchive);
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        using (var client = new HttpClient()) {
          client.Timeout = TimeSpan.FromHours(4);
          client.DefaultRequestHeaders.UserAgent.ParseAdd("GenryTheFox-EOTInstaller/1.0");
          long resumeAt = File.Exists(temporaryArchive) ? new FileInfo(temporaryArchive).Length : 0;
          using (var request = new HttpRequestMessage(HttpMethod.Get, channel.PayloadUrl)) {
            if (resumeAt > 0) request.Headers.Range = new RangeHeaderValue(resumeAt, null);
          using (HttpResponseMessage response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellation)) {
            response.EnsureSuccessStatusCode();
            bool resumed = resumeAt > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed) resumeAt = 0;
            long announced = response.Content.Headers.ContentLength ?? (channel.PayloadSize - resumeAt);
            if (resumeAt + announced != channel.PayloadSize) throw new InvalidDataException("GitHub payload size differs from the embedded release channel");
            using (Stream input = await response.Content.ReadAsStreamAsync())
            using (var output = new FileStream(temporaryArchive, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None,
              1024 * 1024, FileOptions.SequentialScan)) {
              var buffer = new byte[1024 * 1024]; long done = resumeAt;
              while (true) {
                cancellation.ThrowIfCancellationRequested();
                int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation); if (read == 0) break;
                await output.WriteAsync(buffer, 0, read, cancellation); done += read;
                Report(progress, "Скачивание PC Edition", channel.Version, done, channel.PayloadSize);
              }
            }
          }
          }
        }
        if (new FileInfo(temporaryArchive).Length != channel.PayloadSize ||
            !String.Equals(HashFile(temporaryArchive), channel.PayloadSha256, StringComparison.OrdinalIgnoreCase)) {
          File.Delete(temporaryArchive); throw new InvalidDataException("Downloaded payload failed SHA-256 verification");
        }
        File.Move(temporaryArchive, archive);
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
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan)) {
              var buffer = new byte[1024 * 1024];
              while (true) { int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation); if (read == 0) break;
                await file.WriteAsync(buffer, 0, read, cancellation); done += read;
                Report(progress, "Распаковка payload", relative, done, total); }
            }
          }
        }
        string extracted = Path.Combine(stage, "payload");
        if (!IsPayload(extracted)) throw new InvalidDataException("GitHub archive contains no valid payload directory");
        if (Directory.Exists(cacheBase)) Directory.Delete(cacheBase, true);
        Directory.Move(extracted, cacheBase);
        File.WriteAllText(ready, channel.PayloadSha256, new UTF8Encoding(false));
        Directory.Delete(stage, true);
        return cacheBase;
      } catch {
        try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
        throw;
      }
    }

    ReleaseChannel LoadChannel() {
#if EOT_INSTALLER_TEST
      string overridePath = Environment.GetEnvironmentVariable("EOT_INSTALLER_TEST_CHANNEL");
      if (!String.IsNullOrWhiteSpace(overridePath))
        return json.Deserialize<ReleaseChannel>(File.ReadAllText(overridePath, Encoding.UTF8));
#endif
      using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ChannelResource)) {
        if (stream == null) throw new InvalidOperationException(
          "Этот development build не содержит release-channel. Для теста положите локальный payload рядом с build.");
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
          return json.Deserialize<ReleaseChannel>(reader.ReadToEnd());
      }
    }

    static void ValidateChannel(ReleaseChannel channel) {
      Uri uri;
      if (channel == null || channel.Schema != 1 || String.IsNullOrWhiteSpace(channel.Version) ||
          channel.PayloadSize <= 0 || channel.PayloadSha256 == null || channel.PayloadSha256.Length != 64 ||
          !Uri.TryCreate(channel.PayloadUrl, UriKind.Absolute, out uri) ||
#if EOT_INSTALLER_TEST
          !((uri.Scheme == Uri.UriSchemeHttp && (uri.Host == "127.0.0.1" || uri.Host == "localhost")) ||
            (uri.Scheme == Uri.UriSchemeHttps && String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)))
#else
          uri.Scheme != Uri.UriSchemeHttps || !String.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
#endif
          )
        throw new InvalidDataException("Embedded GitHub release channel is invalid");
    }

    static bool IsPayload(string path) {
      return Directory.Exists(path) && File.Exists(Path.Combine(path, "payload-manifest.json")) &&
        Directory.Exists(Path.Combine(path, "port")) && Directory.Exists(Path.Combine(path, "patches"));
    }

    static string SafeName(string value) {
      var builder = new StringBuilder(); foreach (char c in value) builder.Append(Char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
      return builder.ToString();
    }

    static string SafeZipPath(string root, string relative) {
      if (relative.Split('/').AnyPartUnsafe()) throw new InvalidDataException("Unsafe ZIP path");
      string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      string result = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
      if (!result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP path escaped extraction root");
      return result;
    }

    static string HashFile(string path) {
      using (var hash = SHA256.Create()) using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        1024 * 1024, FileOptions.SequentialScan)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }

    static void Report(Action<InstallProgress> callback, string phase, string file, long done, long total) {
      if (callback != null) callback(new InstallProgress { Phase = phase, CurrentFile = file, CompletedBytes = done, TotalBytes = total });
    }
  }
}
