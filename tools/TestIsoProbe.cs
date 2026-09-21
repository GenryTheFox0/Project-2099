// Synthetic, game-data-free regression tests for XDVDFS format probing.
using System;
using System.IO;
using System.Text;
using EotInstaller;

static class TestIsoProbe {
  static void Put32(byte[] bytes, int offset, uint value) {
    bytes[offset] = (byte)value;
    bytes[offset + 1] = (byte)(value >> 8);
    bytes[offset + 2] = (byte)(value >> 16);
    bytes[offset + 3] = (byte)(value >> 24);
  }

  static void ExpectError(string path, string expected) {
    try {
      using (var ignored = new XdvdfsImage(path)) { }
      throw new Exception("Expected an InvalidDataException: " + expected);
    } catch (InvalidDataException error) {
      if (error.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
        throw new Exception("Unexpected diagnostic: " + error.Message);
    }
  }

  static void Main() {
    string testRoot = Path.Combine(Path.GetTempPath(), "EOT-IsoProbe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(testRoot);
    try {
      const int sector = 2048;
      const int offset = 4096; // Exercise the bounded magic scan, not just fixed offsets.
      var valid = new byte[offset + 35 * sector];
      int descriptor = offset + 32 * sector;
      byte[] magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");
      Buffer.BlockCopy(magic, 0, valid, descriptor, magic.Length);
      Put32(valid, descriptor + 20, 33);
      Put32(valid, descriptor + 24, 25);
      int node = offset + 33 * sector;
      Put32(valid, node + 4, 34);
      Put32(valid, node + 8, 3);
      valid[node + 13] = 11;
      byte[] name = Encoding.ASCII.GetBytes("Default.xex");
      Buffer.BlockCopy(name, 0, valid, node + 14, name.Length);
      valid[offset + 34 * sector] = 42;
      string validPath = Path.Combine(testRoot, "valid.iso");
      File.WriteAllBytes(validPath, valid);
      using (var image = new XdvdfsImage(validPath)) {
        if (!image.Exists("Default.xex") || image.GetLength("Default.xex") != 3 || image.Count != 1)
          throw new Exception("Valid XDVDFS fixture was not indexed");
        using (var file = image.OpenRead("Default.xex")) if (file.ReadByte() != 42)
          throw new Exception("XDVDFS slice was not read correctly");
      }

      string shortPath = Path.Combine(testRoot, "short.iso");
      File.WriteAllBytes(shortPath, new byte[32]);
      ExpectError(shortPath, "too short");

      string zipPath = Path.Combine(testRoot, "renamed-zip.iso");
      var zip = new byte[35 * sector];
      zip[0] = (byte)'P'; zip[1] = (byte)'K'; zip[2] = 3; zip[3] = 4;
      File.WriteAllBytes(zipPath, zip);
      ExpectError(zipPath, "ZIP archive");

      string rawPath = Path.Combine(testRoot, "raw-sectors.iso");
      var raw = new byte[35 * sector];
      for (int i = 1; i < 11; i++) raw[i] = 0xFF;
      File.WriteAllBytes(rawPath, raw);
      ExpectError(rawPath, "raw CD sectors");

      string unknownPath = Path.Combine(testRoot, "unknown.iso");
      File.WriteAllBytes(unknownPath, new byte[35 * sector]);
      ExpectError(unknownPath, "layout is unsupported or the file is incomplete");
      Console.WriteLine("PASS: valid XDVDFS, short, ZIP-as-ISO, raw-sector, unknown-layout diagnostics");
    } finally {
      Directory.Delete(testRoot, true);
    }
  }
}
