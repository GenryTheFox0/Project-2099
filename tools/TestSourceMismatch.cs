// Game-data-free checks of what the installer accepts as a source.
//
// The contract changed: a dump is no longer required to be one of the recorded
// revisions. What must still hold is that the thing pointed at really is this
// game, and that a complete but unrecognised dump is accepted rather than
// refused -- that refusal is what sent players hunting for "the right" ISO.
using System;
using System.IO;
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

  static void Write(string path, long size) {
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
      file.SetLength(size); // Sparse zero-filled test data; never game data.
  }

  static void Main() {
    string testRoot = Path.Combine(Path.GetTempPath(), "EOT-SourceProbe-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(testRoot, "Data"));
    try {
      var core = new InstallerCore();

      ExpectProbeError(core, testRoot, "игры нет");

      Write(Path.Combine(testRoot, "Default.xex"), 1);
      ExpectProbeError(core, testRoot, "не хватает Data/Main.pkz");

      Write(Path.Combine(testRoot, "Data", "Main.pkz"), 1);
      ExpectProbeError(core, testRoot, "не хватает Data/BaseGameplay.pkz");

      Write(Path.Combine(testRoot, "Data", "BaseGameplay.pkz"), 1);
      SourceProbe probe = core.ProbeAsync(testRoot, null, CancellationToken.None).GetAwaiter().GetResult();
      if (probe.ManifestId != "unknown-revision")
        throw new Exception("An unrecorded dump should probe as an unknown revision, got: " + probe.ManifestId);
      if (probe.FilesFound != 3)
        throw new Exception("Expected the three files that were written, got: " + probe.FilesFound);

      Console.WriteLine("PASS: game root required, missing parts named, unknown revision accepted");
    } finally {
      Directory.Delete(testRoot, true);
    }
  }
}
