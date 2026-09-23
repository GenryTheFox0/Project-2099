// Game-data-free tests of the same route and schema guards used by InstallerCore.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using EotInstaller;

static class TestPatchChains {
  const string Level = "Data/01A_SMA_010_PortalRoom_FlashBack.pkz";
  static int checks;
  static string Hash(char value) { return new String(value, 64); }
  static ManifestFile File(char digest) {
    return new ManifestFile { Path = Level, Size = 1, Sha256 = Hash(digest) };
  }
  static PatchEntry Patch(char source, char target) {
    return new PatchEntry { Path = Level, SourceSize = 1, SourceSha256 = Hash(source),
      TargetSize = 1, TargetSha256 = Hash(target), Delta = "data/test.eotp",
      DeltaSize = 1, DeltaSha256 = Hash('D') };
  }
  static object Invoke(InstallerCore core, string method, params object[] args) {
    try { return typeof(InstallerCore).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
      .Invoke(core, args); }
    catch (TargetInvocationException error) { throw error.InnerException; }
  }
  static void Fails(Action action, string expected) {
    try { action(); } catch (InvalidDataException error) {
      if (error.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
        throw new Exception("Unexpected rejection: " + error.Message);
      checks++; return;
    }
    throw new Exception("Expected rejection: " + expected);
  }
  static PatchIndex Index() {
    return new PatchIndex { Schema = 4, CanonicalTargetsVerified = true,
      English = new List<PatchEntry> { Patch('A', 'B'), Patch('B', 'C') },
      Russian = new List<PatchEntry> { Patch('C', 'B') },
      CanonicalOriginal = new List<ManifestFile> { File('C') },
      CanonicalRussian = new List<ManifestFile> { File('B') } };
  }
  static void Main(string[] args) {
    var core = new InstallerCore();
    var index = Index();
    Invoke(core, "ValidatePatchIndex", index, "unused"); checks++;
    Invoke(core, "ValidateOriginalRoute", new List<ManifestFile> { File('A') }); checks++;
    var missing = (List<string>)Invoke(core, "PlanTranslation", new List<ManifestFile> { File('A') });
    if (missing.Count != 0) throw new Exception("Two-step normalized source should translate");
    checks++;
    Fails(() => Invoke(core, "ValidateOriginalRoute", new List<ManifestFile> { File('D') }), "not verified");
    index.English.RemoveAt(0);
    Invoke(core, "ValidatePatchIndex", index, "unused");
    Fails(() => Invoke(core, "ValidateOriginalRoute", new List<ManifestFile> { File('A') }), "not verified");
    index = Index(); index.Schema = 3;
    Fails(() => Invoke(core, "ValidatePatchIndex", index, "unused"), "schema 4 required");
    index = Index(); index.CanonicalTargetsVerified = false;
    Fails(() => Invoke(core, "ValidatePatchIndex", index, "unused"), "schema 4 required");
    index = Index(); index.IndexOnlyNotInstallable = true;
    Fails(() => Invoke(core, "ValidatePatchIndex", index, "unused"), "schema 4 required");
    index = Index(); index.English.Add(Patch('C', 'A'));
    Fails(() => Invoke(core, "ValidatePatchIndex", index, "unused"), "Cyclic");
    index = Index(); index.English[1].SourceSize = 2;
    Fails(() => Invoke(core, "ValidatePatchIndex", index, "unused"), "source size mismatch");
    Console.WriteLine("PASS " + checks + " installer chain/schema/passthrough guards");
    if (args.Length != 0) {
      var json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
      var actual = json.Deserialize<PatchIndex>(System.IO.File.ReadAllText(args[0]));
      Invoke(core, "ValidatePatchIndex", actual, Path.GetDirectoryName(args[0]));
      foreach (string source in new[] { "eu", "ru-god", "usa-europe", "usa-europe-r2", "sazanoff" }) {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
          "EOT.source-manifest." + source + ".json"))
        using (var reader = new StreamReader(stream)) {
          var manifest = json.Deserialize<GameManifest>(reader.ReadToEnd());
          Invoke(core, "ValidateOriginalRoute", manifest.Files);
          var untranslated = (List<string>)Invoke(core, "PlanTranslation", manifest.Files);
          if (untranslated.Count != 0)
            throw new Exception(source + " translation routes missing: " + String.Join(", ", untranslated));
          Console.WriteLine("PASS matrix " + source + ": " + manifest.Files.Count +
            " Original final hashes; all Russian routes verified");
        }
      }
    }
  }
}
