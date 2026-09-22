using System;
using System.Collections.Generic;

namespace EotInstaller {
  public sealed class ManifestFile {
    public string Path;
    public long Size;
    public string Sha256;
  }

  public sealed class GameManifest {
    public int Schema;
    public string Id;
    public string Title;
    public string TitleId;
    public string Region;
    public List<string> QuickChecks = new List<string>();
    public List<ManifestFile> Files = new List<ManifestFile>();
  }

  public sealed class PatchFile {
    public string Path;
    public long SourceSize;
    public string SourceSha256;
    public long TargetSize;
    public string TargetSha256;
    public string Delta;
    public long DeltaSize;
    public string DeltaSha256;
  }

  public sealed class PatchManifest {
    public int Schema;
    public string Name;
    public List<PatchFile> Files = new List<PatchFile>();
  }

  // One step of work on one file: "a file with this sha256 becomes that one".
  public sealed class PatchEntry {
    public string Path;
    public long SourceSize;
    public string SourceSha256;
    public long TargetSize;
    public string TargetSha256;
    public string Delta;
    public long DeltaSize;
    public string DeltaSha256;
  }

  // Everything the installer knows how to do, keyed by hash rather than by dump:
  // English brings any known text file to the canonical English one, Russian
  // turns that into the translation.
  public sealed class PatchIndex {
    public int Schema;
    public List<PatchEntry> English = new List<PatchEntry>();
    public List<PatchEntry> Russian = new List<PatchEntry>();
  }

  public sealed class PayloadManifest {
    public int Schema;
    public string Build;
    public List<ManifestFile> Files = new List<ManifestFile>();
  }

  // Release channel: where the PC Edition payload ZIP lives and what it must
  // hash to. Embedded at build time; a release-channel.json next to the
  // installer overrides it, so a download can be re-pointed without a rebuild.
  public sealed class ReleaseChannel {
    public int Schema;
    public string Version;
    public string PayloadUrl;
    // Optional extra download locations tried after PayloadUrl, in order.
    public List<string> Mirrors = new List<string>();
    public long PayloadSize;
    public string PayloadSha256;
  }

  public sealed class SourceProbe {
    public string ManifestId;
    public string Kind;
    public string DisplayName;
    public string Region;
    public int FilesFound;
    public long RequiredBytes;
  }

  public sealed class InstallProgress {
    public string Phase;
    public string CurrentFile;
    public long CompletedBytes;
    public long TotalBytes;
    public double Ratio { get { return TotalBytes <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)CompletedBytes / TotalBytes)); } }
  }
}
