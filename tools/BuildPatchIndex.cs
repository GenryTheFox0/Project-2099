// Turns the per-image patch sets under patchsets/ into the one thing that ships:
// an index keyed by file hash, with every delta stored once by content.
//
// Two observations collapse the old per-revision model. Only thirteen files carry
// text, and every other difference between dumps is a level package -- one
// revision's level is the same level, so those deltas are pure weight. And the
// translation is one piece of work, not one per dump: bring a dump's text files
// to the canonical English first, and a single Russian set then serves them all.
//
// Usage: BuildPatchIndex <repository root>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

sealed class IndexPatch {
  public string Path; public long SourceSize; public string SourceSha256;
  public long TargetSize; public string TargetSha256;
  public string Delta; public long DeltaSize; public string DeltaSha256;
}
sealed class PatchSet { public int Schema; public string Name; public List<IndexPatch> Files = new List<IndexPatch>(); }
sealed class PatchIndex {
  public int Schema = 3;
  public List<IndexPatch> English = new List<IndexPatch>();
  public List<IndexPatch> Russian = new List<IndexPatch>();
}

static class BuildPatchIndex {
  static readonly string[] Variants = {
    "eu-retail", "usa-europe-retail", "usa-europe-retail-r2", "ru-god-alt", "sazanoff-rus-god"
  };
  static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 32 };

  static T Load<T>(string path) {
    if (!File.Exists(path)) throw new FileNotFoundException("Patch set is missing", path);
    return Json.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
  }

  static string Key(string path, string hash) {
    return path.Replace('\\', '/').ToLowerInvariant() + "|" + hash.ToLowerInvariant();
  }

  static void Main(string[] args) {
    string root = Path.GetFullPath(args.Length >= 1 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\.."));
    string sets = Path.Combine(root, "patchsets");
    string patches = Path.Combine(root, @"payload\patches");
    string pool = Path.Combine(patches, "data");
    if (!Directory.Exists(sets)) throw new DirectoryNotFoundException("No patchsets folder: " + sets);
    if (Directory.Exists(pool)) Directory.Delete(pool, true);
    Directory.CreateDirectory(pool);

    // eu-retail needs no normalisation, so its own files are the canonical English
    // and its Russian set defines exactly what the translation rewrites.
    if (Load<PatchSet>(Path.Combine(sets, @"eu-retail\original.json")).Files.Count != 0)
      throw new InvalidDataException("eu-retail is not canonical: its Original set is not empty");
    PatchSet baseRussian = Load<PatchSet>(Path.Combine(sets, @"eu-retail\russian.json"));
    var textPaths = new HashSet<string>(baseRussian.Files.Select(f => f.Path.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
    Console.WriteLine("Text-carrying files: " + textPaths.Count);

    var index = new PatchIndex();
    var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    long kept = 0, droppedBytes = 0; int dropped = 0;

    foreach (string variant in Variants) {
      foreach (IndexPatch patch in Load<PatchSet>(Path.Combine(sets, variant, "original.json")).Files) {
        string path = patch.Path.ToLowerInvariant();
        if (!textPaths.Contains(path) && !String.Equals(path, "default.xex", StringComparison.OrdinalIgnoreCase)) {
          dropped++; droppedBytes += patch.DeltaSize; continue;   // a repacked level is still the same level
        }
        string key = "E|" + Key(patch.Path, patch.SourceSha256);
        string existing;
        if (seen.TryGetValue(key, out existing)) {
          if (!String.Equals(existing, patch.TargetSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Two sets disagree on the target for " + patch.Path);
          continue;
        }
        seen.Add(key, patch.TargetSha256);
        kept += Store(patch, Path.Combine(sets, variant), pool);
        index.English.Add(patch);
      }
      // Every other variant's Russian set is the same translation reached from a
      // different starting point, which the English stage already covers.
      foreach (IndexPatch patch in Load<PatchSet>(Path.Combine(sets, variant, "russian.json")).Files) {
        if (!String.Equals(variant, "eu-retail", StringComparison.OrdinalIgnoreCase)) {
          dropped++; droppedBytes += patch.DeltaSize; continue;
        }
        kept += Store(patch, Path.Combine(sets, variant), pool);
        index.Russian.Add(patch);
      }
    }

    var canonical = index.Russian.ToDictionary(e => e.Path.ToLowerInvariant(), e => e.SourceSha256, StringComparer.OrdinalIgnoreCase);
    foreach (IndexPatch patch in index.English) {
      string wanted;
      if (canonical.TryGetValue(patch.Path.ToLowerInvariant(), out wanted) &&
          !String.Equals(wanted, patch.TargetSha256, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("English target for " + patch.Path + " is not the canonical English file");
    }

    index.English = index.English.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
      .ThenBy(e => e.SourceSha256, StringComparer.OrdinalIgnoreCase).ToList();
    index.Russian = index.Russian.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();

    Console.WriteLine("English entries: " + index.English.Count);
    Console.WriteLine("Russian entries: " + index.Russian.Count);
    Console.WriteLine(String.Format("Dropped {0} normalisation entries, {1:N1} MB", dropped, droppedBytes / 1048576.0));
    Console.WriteLine(String.Format("Unique deltas kept: {0:N1} MB", kept / 1048576.0));

    File.WriteAllText(Path.Combine(patches, "index.json"), Json.Serialize(index), new UTF8Encoding(false));
    Console.WriteLine("WROTE " + Path.Combine(patches, "index.json"));
  }

  static long Store(IndexPatch patch, string variantRoot, string pool) {
    string stored = "data/" + patch.DeltaSha256.ToLowerInvariant() + ".eotp";
    string target = Path.Combine(Path.GetDirectoryName(pool), stored.Replace('/', Path.DirectorySeparatorChar));
    long added = 0;
    if (!File.Exists(target)) {
      Directory.CreateDirectory(Path.GetDirectoryName(target));
      File.Copy(Path.Combine(variantRoot, patch.Delta.Replace('/', Path.DirectorySeparatorChar)), target);
      added = patch.DeltaSize;
    }
    patch.Delta = stored;
    return added;
  }
}
