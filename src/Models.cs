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

  public sealed class PayloadManifest {
    public int Schema;
    public string Build;
    public List<ManifestFile> Files = new List<ManifestFile>();
  }

  public sealed class ReleaseChannel {
    public int Schema;
    public string Version;
    public string PayloadUrl;
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
