using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

sealed class ManifestFile { public string Path; public long Size; public string Sha256; }
sealed class PayloadManifest {
  public int Schema = 1;
  public string Build = "Project 2099 V2 BETA 3.1 TEST (Edge of Time PC Edition) 20260923";
  public List<ManifestFile> Files = new List<ManifestFile>();
}

static class BuildPayloadManifest {
  static string Hash(string path) {
    using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
      return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
  }
  static void Main(string[] args) {
    if (args.Length != 2) throw new Exception("Usage: BuildPayloadManifest <payload/port> <output.json>");
    string root = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
    string output = Path.GetFullPath(args[1]);
    var manifest = new PayloadManifest();
    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) {
      string relative = file.Substring(root.Length + 1).Replace('\\', '/');
      var info = new FileInfo(file);
      manifest.Files.Add(new ManifestFile { Path = relative, Size = info.Length, Sha256 = Hash(file) });
      Console.WriteLine("PAYLOAD " + relative);
    }
    Directory.CreateDirectory(Path.GetDirectoryName(output));
    string json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(manifest);
    File.WriteAllText(output, json, new UTF8Encoding(false));
    Console.WriteLine("WROTE " + manifest.Files.Count + " entries to " + output);
  }
}
