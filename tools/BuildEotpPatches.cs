using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

sealed class ManifestFile { public string Path; public long Size; public string Sha256; }
sealed class GameManifest { public int Schema; public List<ManifestFile> Files; }
sealed class PatchFile {
  public string Path; public long SourceSize; public string SourceSha256;
  public long TargetSize; public string TargetSha256;
  public string Delta; public long DeltaSize; public string DeltaSha256;
}
sealed class PatchManifest { public int Schema = 1; public string Name; public List<PatchFile> Files = new List<PatchFile>(); }

static class BuildEotpPatches {
  const int BlockSize = 64 * 1024;
  static readonly byte[] Magic = Encoding.ASCII.GetBytes("EOTP1");

  static byte[] HashBytes(string path) {
    using (var sha = SHA256.Create()) using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
      1024 * 1024, FileOptions.SequentialScan)) return sha.ComputeHash(stream);
  }
  static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); }
  static string Resolve(string root, string relative) { return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)); }
  static bool Equal(byte[] a, int aCount, byte[] b, int bCount) {
    if (aCount != bCount) return false; for (int i = 0; i < aCount; i++) if (a[i] != b[i]) return false; return true;
  }
  static byte[] Compress(byte[] data, int count) {
    using (var memory = new MemoryStream()) { using (var zip = new DeflateStream(memory, CompressionLevel.Optimal, true)) zip.Write(data, 0, count); return memory.ToArray(); }
  }

  static void CreatePatch(string source, string target, string output, byte[] sourceHash, byte[] targetHash) {
    long sourceSize = new FileInfo(source).Length, targetSize = new FileInfo(target).Length;
    bool sparse = sourceSize == targetSize;
    Directory.CreateDirectory(Path.GetDirectoryName(output));
    if (File.Exists(output)) File.Delete(output);
    using (var patch = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
    using (var writer = new BinaryWriter(patch, Encoding.UTF8, true)) {
      writer.Write(Magic); writer.Write((byte)(sparse ? 0 : 1)); writer.Write(BlockSize);
      writer.Write(sourceSize); writer.Write(targetSize); writer.Write(sourceHash); writer.Write(targetHash);
      long countOffset = patch.Position; writer.Write(0); int records = 0;
      using (var targetStream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
      using (var sourceStream = sparse ? new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan) : null) {
        var targetBuffer = new byte[BlockSize]; var sourceBuffer = new byte[BlockSize]; long offset = 0;
        while (offset < targetSize) {
          int wanted = (int)Math.Min(BlockSize, targetSize - offset);
          int targetRead = ReadExactlyOrEof(targetStream, targetBuffer, wanted);
          int sourceRead = sparse ? ReadExactlyOrEof(sourceStream, sourceBuffer, wanted) : -1;
          if (!sparse || !Equal(sourceBuffer, sourceRead, targetBuffer, targetRead)) {
            byte[] compressed = Compress(targetBuffer, targetRead); bool useCompressed = compressed.Length < targetRead;
            writer.Write(offset); writer.Write(targetRead); writer.Write(useCompressed ? compressed.Length : targetRead);
            writer.Write((byte)(useCompressed ? 1 : 0)); writer.Write(useCompressed ? compressed : targetBuffer, 0, useCompressed ? compressed.Length : targetRead);
            records++;
          }
          offset += targetRead;
        }
      }
      long end = patch.Position; patch.Position = countOffset; writer.Write(records); patch.Position = end; writer.Flush();
    }
  }

  static int ReadExactlyOrEof(Stream stream, byte[] buffer, int wanted) {
    int done = 0; while (done < wanted) { int read = stream.Read(buffer, done, wanted - done); if (read <= 0) break; done += read; } return done;
  }

  static void ApplyForVerification(string source, string patchPath, string output) {
    // Keep this verifier independent from the installer implementation.
    using (var patch = File.OpenRead(patchPath)) using (var reader = new BinaryReader(patch, Encoding.UTF8)) {
      if (!reader.ReadBytes(5).SequenceEqual(Magic)) throw new InvalidDataException("Bad patch magic");
      byte mode = reader.ReadByte(); int block = reader.ReadInt32(); long sourceSize = reader.ReadInt64(); long targetSize = reader.ReadInt64();
      reader.ReadBytes(32); reader.ReadBytes(32); int records = reader.ReadInt32();
      if (mode == 0) File.Copy(source, output, false); else using (File.Create(output)) { }
      using (var target = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
        target.SetLength(targetSize);
        for (int i = 0; i < records; i++) {
          long offset = reader.ReadInt64(); int raw = reader.ReadInt32(); int stored = reader.ReadInt32(); byte codec = reader.ReadByte(); byte[] bytes = reader.ReadBytes(stored); byte[] data;
          if (codec == 0) data = bytes; else { data = new byte[raw]; using (var input = new DeflateStream(new MemoryStream(bytes, false), CompressionMode.Decompress)) { int done = 0; while (done < raw) { int read = input.Read(data, done, raw - done); if (read <= 0) throw new EndOfStreamException(); done += read; } } }
          target.Position = offset; target.Write(data, 0, data.Length);
        }
      }
    }
  }

  static void Main(string[] args) {
    if (args.Length != 6) throw new Exception("Usage: BuildEotpPatches <source-manifest> <source-root> <target-root> <patches-root> <output-name.json> <description>");
    string sourceManifestPath = Path.GetFullPath(args[0]), sourceRoot = Path.GetFullPath(args[1]);
    string targetRoot = Path.GetFullPath(args[2]), patchesRoot = Path.GetFullPath(args[3]);
    var json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 32 };
    GameManifest sourceManifest = json.Deserialize<GameManifest>(File.ReadAllText(sourceManifestPath, Encoding.UTF8));
    var result = new PatchManifest { Name = args[5] };
    string prefix = Path.GetFileNameWithoutExtension(args[4]);
    foreach (ManifestFile file in sourceManifest.Files.OrderBy(value => value.Path, StringComparer.OrdinalIgnoreCase)) {
      string source = Resolve(sourceRoot, file.Path), target = Resolve(targetRoot, file.Path);
      if (!File.Exists(target)) throw new FileNotFoundException("Target file missing", target);
      byte[] sourceHash = HashBytes(source), targetHash = HashBytes(target);
      if (sourceHash.SequenceEqual(targetHash)) continue;
      string relative = prefix + "/" + file.Path + ".eotp"; string patchPath = Resolve(patchesRoot, relative);
      Console.WriteLine("EOTP " + file.Path + " source=" + new FileInfo(source).Length + " target=" + new FileInfo(target).Length);
      CreatePatch(source, target, patchPath, sourceHash, targetHash);
      string verify = patchPath + ".verify.tmp"; if (File.Exists(verify)) File.Delete(verify);
      try { ApplyForVerification(source, patchPath, verify); if (!HashBytes(verify).SequenceEqual(targetHash)) throw new InvalidDataException("Verification failed: " + file.Path); }
      finally { if (File.Exists(verify)) File.Delete(verify); }
      var patchInfo = new FileInfo(patchPath);
      result.Files.Add(new PatchFile { Path = file.Path, SourceSize = file.Size, SourceSha256 = Hex(sourceHash),
        TargetSize = new FileInfo(target).Length, TargetSha256 = Hex(targetHash), Delta = relative,
        DeltaSize = patchInfo.Length, DeltaSha256 = Hex(HashBytes(patchPath)) });
    }
    string output = Path.Combine(patchesRoot, args[4]);
    File.WriteAllText(output, json.Serialize(result), new UTF8Encoding(false));
    Console.WriteLine("WROTE " + result.Files.Count + " verified EOTP patches to " + output);
  }
}
