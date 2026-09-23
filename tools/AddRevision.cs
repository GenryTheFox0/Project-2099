// Teaches the installer a dump it has never seen, from the files that carry text.
//
// When a dump cannot be translated the installer writes its file hashes into
// Support/Install/INSTALL_RECEIPT.json and installs in English. The only thing
// missing is a delta from those text files to the canonical English ones; the
// Russian stage after that is shared by every dump.
//
// Usage: AddRevision <repository root> <dump folder> <canonical Data\Original> [name] [--dry-run]
//
// The dump folder needs only the files the translation touches; a whole dump
// works too. Nothing is downloaded or uploaded.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

static class AddRevision {
  static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 32 };

  static string Hash(string path) {
    using (var sha = SHA256.Create())
    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
      return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
  }

  static bool Same(string left, string right) {
    return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
  }

  static void Main(string[] args) {
    if (args.Length < 3) throw new Exception(
      @"Usage: AddRevision <repository root> <dump folder> <canonical Data\Original> [name] [--dry-run]");
    string root = Path.GetFullPath(args[0]);
    string dump = Path.GetFullPath(args[1]);
    string english = Path.GetFullPath(args[2]);
    string name = args.Length >= 4 && !args[3].StartsWith("--") ? args[3] : "added revision";
    bool dryRun = args.Any(value => String.Equals(value, "--dry-run", StringComparison.OrdinalIgnoreCase));

    string patches = Path.Combine(root, @"payload\patches");
    string indexPath = Path.Combine(patches, "index.json");
    string builder = Path.Combine(root, @"build\tools\BuildEotpPatches.exe");
    var index = Json.Deserialize<PatchIndex>(File.ReadAllText(indexPath, Encoding.UTF8));
    if (index.Schema != 3)
      throw new InvalidDataException("Legacy AddRevision cannot modify schema 4 canonical proofs or chained patches. " +
        "Add the donor to patchsets and use build_patch_index.py with verified canonical and correction manifests. Index unchanged.");
    if (!File.Exists(Path.Combine(english, "Default.xex")))
      throw new Exception("That is not the canonical English tree: " + english);
    if (!File.Exists(builder))
      throw new Exception("Build the tools first (build_tools.ps1): " + builder);

    var known = new HashSet<string>(index.English.Select(e => e.Path.ToLowerInvariant() + "|" + e.SourceSha256.ToUpperInvariant()));
    var canonical = index.Russian.ToDictionary(e => e.Path.ToLowerInvariant(), e => e.SourceSha256, StringComparer.OrdinalIgnoreCase);

    var wanted = new List<ManifestFile>();
    foreach (string path in canonical.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) {
      string local = Path.Combine(dump, path.Replace('/', Path.DirectorySeparatorChar));
      if (!File.Exists(local)) throw new Exception("The folder is missing " + path);
      string digest = Hash(local);
      if (Same(digest, canonical[path])) { Console.WriteLine(path.PadRight(44) + "already canonical"); continue; }
      if (known.Contains(path.ToLowerInvariant() + "|" + digest.ToUpperInvariant())) {
        Console.WriteLine(path.PadRight(44) + "already known"); continue;
      }
      wanted.Add(new ManifestFile { Path = path, Size = new FileInfo(local).Length, Sha256 = digest });
      Console.WriteLine(path.PadRight(44) + "NEW " + digest.Substring(0, 12).ToLowerInvariant());
    }

    if (wanted.Count == 0) { Console.WriteLine("\r\nThis dump already installs; nothing to add."); return; }
    if (dryRun) { Console.WriteLine("\r\nDry run: " + wanted.Count + " files would be added."); return; }

    string work = Path.Combine(Path.GetTempPath(), "eot-revision-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    try {
      var manifest = new GameManifest {
        Schema = 1, Id = "added", Title = "Spider-Man: Edge of Time", TitleId = "415608B2", Region = name,
        QuickChecks = new List<string> { wanted[0].Path }, Files = wanted
      };
      string manifestPath = Path.Combine(work, "manifest.json");
      File.WriteAllText(manifestPath, Json.Serialize(manifest), new UTF8Encoding(false));
      string sets = Path.Combine(work, "sets");
      Directory.CreateDirectory(sets);

      var process = Process.Start(new ProcessStartInfo(builder) {
        Arguments = String.Join(" ", new[] { manifestPath, dump, english, sets, "english.json", "New revision to clean Original" }
          .Select(value => "\"" + value + "\"")),
        UseShellExecute = false
      });
      process.WaitForExit();
      if (process.ExitCode != 0) throw new Exception("BuildEotpPatches returned " + process.ExitCode);

      var produced = Json.Deserialize<PatchSet>(File.ReadAllText(Path.Combine(sets, "english.json"), Encoding.UTF8));
      int added = 0;
      foreach (IndexPatch patch in produced.Files) {
        string stored = "data/" + patch.DeltaSha256.ToLowerInvariant() + ".eotp";
        string target = Path.Combine(patches, stored.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target)) {
          Directory.CreateDirectory(Path.GetDirectoryName(target));
          File.Copy(Path.Combine(sets, patch.Delta.Replace('/', Path.DirectorySeparatorChar)), target);
        }
        patch.Delta = stored;
        index.English.Add(patch);
        added++;
      }

      // Every English target has to be a Russian source, or the second stage would
      // find nothing and the dump would still end up installed in English.
      foreach (IndexPatch patch in index.English) {
        string expected;
        if (canonical.TryGetValue(patch.Path.ToLowerInvariant(), out expected) && !Same(expected, patch.TargetSha256))
          throw new Exception("Target for " + patch.Path + " is not the canonical English file; index left untouched");
      }

      index.English = index.English.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
        .ThenBy(e => e.SourceSha256, StringComparer.OrdinalIgnoreCase).ToList();
      File.WriteAllText(indexPath, Json.Serialize(index), new UTF8Encoding(false));
      Console.WriteLine("\r\nAdded " + added + " entries. Now rebuild the payload:");
      Console.WriteLine(@"  powershell -File build_release.ps1 -Version vX.Y.Z");
    } finally {
      try { Directory.Delete(work, true); } catch { }
    }
  }

  sealed class ManifestFile { public string Path; public long Size; public string Sha256; }
  sealed class GameManifest {
    public int Schema; public string Id; public string Title; public string TitleId; public string Region;
    public List<string> QuickChecks = new List<string>(); public List<ManifestFile> Files = new List<ManifestFile>();
  }
  sealed class IndexPatch {
    public string Path; public long SourceSize; public string SourceSha256;
    public long TargetSize; public string TargetSha256;
    public string Delta; public long DeltaSize; public string DeltaSha256;
  }
  sealed class PatchSet { public int Schema; public string Name; public List<IndexPatch> Files = new List<IndexPatch>(); }
  sealed class PatchIndex {
    public int Schema;
    public List<IndexPatch> English = new List<IndexPatch>();
    public List<IndexPatch> Russian = new List<IndexPatch>();
  }
}
