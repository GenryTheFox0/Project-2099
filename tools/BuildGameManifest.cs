using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

sealed class ManifestFile { public string Path; public long Size; public string Sha256; }
sealed class GameManifest {
  public int Schema = 1;
  public string Id = "eu-retail";
  public string Title = "Spider-Man: Edge of Time";
  public string TitleId = "415608B2";
  public string Region = "Xbox 360 EU retail donor";
  public List<string> QuickChecks = new List<string> { "Default.xex", "Data/Main.pkz", "Data/Act01.pkz" };
  public List<ManifestFile> Files = new List<ManifestFile>();
}

static class BuildGameManifest {
  static string Hash(string path) {
    using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
      return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
  }
  static bool Include(string relative) {
    string path = relative.Replace('\\', '/');
    return !path.StartsWith("$SystemUpdate/", StringComparison.OrdinalIgnoreCase) &&
      !String.Equals(path, "nxeart", StringComparison.OrdinalIgnoreCase) &&
      !String.Equals(path, "Data/GENRY_BUILD_ALL_LANGS.json", StringComparison.OrdinalIgnoreCase);
  }
  static void Main(string[] args) {
    if (args.Length < 2 || args.Length > 4) throw new Exception("Usage: BuildGameManifest <source root> <output.json> [id] [region]");
    string root = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
    string output = Path.GetFullPath(args[1]);
    if (!File.Exists(Path.Combine(root, "Default.xex"))) throw new Exception("Default.xex is missing");
    var manifest = new GameManifest();
    if (args.Length >= 3) manifest.Id = args[2];
    if (args.Length >= 4) manifest.Region = args[3];
    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
      string relative = file.Substring(root.Length + 1).Replace('\\', '/');
      if (!Include(relative)) continue;
      var info = new FileInfo(file);
      manifest.Files.Add(new ManifestFile { Path = relative, Size = info.Length, Sha256 = Hash(file) });
      Console.WriteLine("SOURCE " + relative);
    }
    foreach (string quick in manifest.QuickChecks)
      if (!manifest.Files.Any(f => String.Equals(f.Path, quick, StringComparison.OrdinalIgnoreCase)))
        throw new Exception("Quick check is missing: " + quick);
    Directory.CreateDirectory(Path.GetDirectoryName(output));
    string json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(manifest);
    File.WriteAllText(output, json, new UTF8Encoding(false));
    Console.WriteLine("WROTE " + manifest.Files.Count + " entries to " + output);
  }
}
