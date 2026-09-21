// Game-data-free checks that format recognition and donor revision checks remain distinct.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using EotInstaller;

static class TestSourceMismatch {
  static void ExpectProbeError(InstallerCore core, string path, string expected) {
    try {
      core.ProbeAsync(path, null, CancellationToken.None).GetAwaiter().GetResult();
      throw new Exception("Expected an InvalidDataException: " + expected);
    } catch (InvalidDataException error) {
      if (error.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
        throw new Exception("Unexpected probe diagnostic: " + error.Message);
    }
  }

  static void Main() {
    string testRoot = Path.Combine(Path.GetTempPath(), "EOT-SourceProbe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(testRoot, "Data"));
    try {
      File.WriteAllBytes(Path.Combine(testRoot, "Default.xex"), new byte[] { 0 });
      var core = new InstallerCore();
      ExpectProbeError(core, testRoot, "game root");
      File.WriteAllBytes(Path.Combine(testRoot, "Data", "Main.pkz"), new byte[] { 0 });
      ExpectProbeError(core, testRoot, "quick-check file names or sizes");
      foreach (string path in core.Manifest.QuickChecks) {
        var entry = core.Manifest.Files.First(file => String.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
        string output = Path.Combine(testRoot, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        using (var file = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
          file.SetLength(entry.Size); // Sparse zero-filled test data; never game data.
      }
      ExpectProbeError(core, testRoot, "SHA-256 hashes differ");
      Console.WriteLine("PASS: missing game root, unsupported sizes, and matching sizes with wrong SHA-256");
    } finally {
      Directory.Delete(testRoot, true);
    }
  }
}
