// Compatibility entry point. There is ONE generator for the verified schema-4
// contract: retaining a second thirteen-file whitelist caused level dialogue
// to be silently excluded. All options are forwarded to that implementation.
// Usage: BuildPatchIndex <repository root> [build_patch_index.py options]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

static class BuildPatchIndex {
  static string Quote(string value) {
    var result = new StringBuilder("\"");
    int slashes = 0;
    foreach (char c in value) {
      if (c == '\\') { slashes++; continue; }
      result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
      result.Append(c);
      slashes = 0;
    }
    result.Append('\\', slashes * 2);
    return result.Append('"').ToString();
  }

  static int Main(string[] args) {
    string root = Path.GetFullPath(args.Length >= 1 ? args[0] :
      Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\.."));
    string script = Path.Combine(root, @"tools\build_patch_index.py");
    if (!File.Exists(script)) throw new FileNotFoundException("Verified patch generator is missing", script);
    var forwarded = new List<string> { script, "--root", root };
    forwarded.AddRange(args.Skip(1));
    var start = new ProcessStartInfo {
      FileName = Environment.GetEnvironmentVariable("EOT_PYTHON") ?? "python",
      Arguments = String.Join(" ", forwarded.Select(Quote)),
      UseShellExecute = false,
      CreateNoWindow = true
    };
    using (Process process = Process.Start(start)) {
      process.WaitForExit();
      return process.ExitCode;
    }
  }
}
