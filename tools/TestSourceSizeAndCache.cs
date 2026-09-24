// Focused, game-data-free regression checks for source-size diagnostics and
// payload-cache validation. The production assembly is loaded by reflection so
// this test exercises the exact EOTInstaller.Tests.exe passed by the runner.
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

static class TestSourceSizeAndCache {
  const string AbcSha256 = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
  const string ZeroSha256 = "0000000000000000000000000000000000000000000000000000000000000000";
  static int checks;

  static void Check(bool condition, string failure) {
    if (!condition) throw new Exception(failure);
    checks++;
  }

  static object Invoke(MethodInfo method, object target, params object[] arguments) {
    try { return method.Invoke(target, arguments); }
    catch (TargetInvocationException error) {
      if (error.InnerException != null) throw error.InnerException;
      throw;
    }
  }

  static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, Type[] parameters) {
    MethodInfo method = type.GetMethod(name, flags, null, parameters, null);
    if (method == null) throw new Exception("Required regression target is missing: " + type.FullName + "." + name);
    return method;
  }

  static void ExpectSizeMismatch(Func<object> action, string context, long expected, long actual) {
    try { action(); }
    catch (EndOfStreamException error) {
      string expectedText = "expected " + expected + " bytes, read " + actual;
      Check(error.Message.IndexOf(context, StringComparison.Ordinal) >= 0,
        "Size error lost its file context. Expected `" + context + "`, got: " + error.Message);
      Check(error.Message.IndexOf(expectedText, StringComparison.Ordinal) >= 0,
        "Size error lost expected/actual byte counts. Expected `" + expectedText + "`, got: " + error.Message);
      return;
    }
    throw new Exception("Expected EndOfStreamException for " + context +
      " (expected " + expected + ", actual " + actual + ")");
  }

  static string Manifest(long declaredSize) {
    return "{\"Schema\":1,\"Build\":\"size-cache-regression\",\"Files\":[{" +
      "\"Path\":\"tiny.bin\",\"Size\":" + declaredSize + ",\"Sha256\":\"" + ZeroSha256 + "\"}]}";
  }

  static void WritePayload(string root, long declaredSize, byte[] actual) {
    Directory.CreateDirectory(Path.Combine(root, "port"));
    Directory.CreateDirectory(Path.Combine(root, "patches"));
    File.WriteAllText(Path.Combine(root, "payload-manifest.json"), Manifest(declaredSize), new UTF8Encoding(false));
    File.WriteAllBytes(Path.Combine(root, "port", "tiny.bin"), actual);
  }

  static void WriteEntry(ZipArchive archive, string name, byte[] bytes) {
    ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
    using (Stream output = entry.Open()) output.Write(bytes, 0, bytes.Length);
  }

  static void CreatePayloadZip(string path, byte[] payloadBytes) {
    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    using (var archive = new ZipArchive(file, ZipArchiveMode.Create, false, Encoding.UTF8)) {
      WriteEntry(archive, "payload/payload-manifest.json", Encoding.UTF8.GetBytes(Manifest(payloadBytes.Length)));
      WriteEntry(archive, "payload/port/tiny.bin", payloadBytes);
      archive.CreateEntry("payload/patches/");
    }
  }

  static string HashFile(string path) {
    using (var sha = SHA256.Create())
    using (var stream = File.OpenRead(path))
      return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
  }

  static Exception ResolveFailure(object provider, MethodInfo resolve, string manualPath) {
    try {
      Resolve(provider, resolve, manualPath);
      return null;
    } catch (Exception error) { return error; }
  }

  static string Resolve(object provider, MethodInfo resolve, string manualPath) {
    object taskObject = Invoke(resolve, provider, manualPath, null, CancellationToken.None);
    Task task = taskObject as Task;
    if (task == null) throw new Exception("ResolveAsync did not return a Task");
    task.GetAwaiter().GetResult();
    PropertyInfo result = taskObject.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
    if (result == null) throw new Exception("ResolveAsync task has no Result property");
    return (string)result.GetValue(taskObject, null);
  }

  static bool PayloadSizesMatch(object provider, MethodInfo sizesMatch, string payload, out string issue) {
    object[] arguments = { payload, null };
    bool result = (bool)Invoke(sizesMatch, provider, arguments);
    issue = arguments[1] as string;
    return result;
  }

  static void TestStreams(Type coreType) {
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    MethodInfo hashStream = RequireMethod(coreType, "HashStream", StaticPrivate,
      new[] { typeof(Stream), typeof(long), typeof(CancellationToken), typeof(Action<int>), typeof(string) });
    MethodInfo copyStream = RequireMethod(coreType, "CopyStreamWithHash", StaticPrivate,
      new[] { typeof(Stream), typeof(Stream), typeof(long), typeof(CancellationToken), typeof(Action<int>), typeof(string) });
    byte[] abc = Encoding.ASCII.GetBytes("abc");

    int advanced = 0;
    using (var input = new MemoryStream(abc, false)) {
      string hash = (string)Invoke(hashStream, null, input, 3L, CancellationToken.None,
        new Action<int>(count => advanced += count), "clean hash fixture");
      Check(String.Equals(hash, AbcSha256, StringComparison.Ordinal), "Clean HashStream returned " + hash);
      Check(advanced == 3, "Clean HashStream reported " + advanced + " bytes instead of 3");
    }

    ExpectSizeMismatch(() => {
      using (var input = new MemoryStream(abc, false))
        return Invoke(hashStream, null, input, 5L, CancellationToken.None, null, "short hash fixture");
    }, "short hash fixture", 5, 3);
    ExpectSizeMismatch(() => {
      using (var input = new MemoryStream(abc, false))
        return Invoke(hashStream, null, input, 2L, CancellationToken.None, null, "long hash fixture");
    }, "long hash fixture", 2, 3);

    advanced = 0;
    using (var input = new MemoryStream(abc, false))
    using (var output = new MemoryStream()) {
      string hash = (string)Invoke(copyStream, null, input, output, 3L, CancellationToken.None,
        new Action<int>(count => advanced += count), "clean copy fixture");
      Check(String.Equals(hash, AbcSha256, StringComparison.Ordinal), "Clean CopyStreamWithHash returned " + hash);
      Check(advanced == 3, "Clean CopyStreamWithHash reported " + advanced + " bytes instead of 3");
      Check(BitConverter.ToString(output.ToArray()) == BitConverter.ToString(abc),
        "Clean CopyStreamWithHash changed output bytes");
    }

    ExpectSizeMismatch(() => {
      using (var input = new MemoryStream(abc, false))
      using (var output = new MemoryStream())
        return Invoke(copyStream, null, input, output, 5L, CancellationToken.None, null, "short copy fixture");
    }, "short copy fixture", 5, 3);
    ExpectSizeMismatch(() => {
      using (var input = new MemoryStream(abc, false))
      using (var output = new MemoryStream())
        return Invoke(copyStream, null, input, output, 2L, CancellationToken.None, null, "long copy fixture");
    }, "long copy fixture", 2, 3);
  }

  static void TestPayloads(Assembly installer, string fixtureRoot) {
    Type providerType = installer.GetType("EotInstaller.PayloadProvider", true);
    const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    MethodInfo sizesMatch = RequireMethod(providerType, "PayloadFileSizesMatch", InstancePrivate,
      new[] { typeof(string), typeof(string).MakeByRefType() });
    MethodInfo resolve = RequireMethod(providerType, "ResolveAsync", BindingFlags.Instance | BindingFlags.Public,
      new[] { typeof(string), typeof(Action<>).MakeGenericType(installer.GetType("EotInstaller.InstallProgress", true)),
        typeof(CancellationToken) });
    MethodInfo profileRoot = RequireMethod(providerType, "ProfileRoot", BindingFlags.Static | BindingFlags.NonPublic,
      new Type[0]);

    string appData = Path.Combine(fixtureRoot, "appdata");
    string nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
    string version = "ssc-" + nonce;
    string assetName = "payload-size-cache-" + nonce + ".zip";
    string archive = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assetName);
    byte[] validBytes = Encoding.ASCII.GetBytes("12345");
    CreatePayloadZip(archive, validBytes);
    string archiveHash = HashFile(archive);
    long archiveSize = new FileInfo(archive).Length;
    string channel = Path.Combine(fixtureRoot, "release-channel.json");
    Directory.CreateDirectory(fixtureRoot);
    File.WriteAllText(channel, "{\"Schema\":1,\"Version\":\"" + version +
      "\",\"PayloadUrl\":\"https://example.invalid/" + assetName +
      "\",\"PayloadSize\":" + archiveSize + ",\"PayloadSha256\":\"" + archiveHash + "\"}",
      new UTF8Encoding(false));

    Environment.SetEnvironmentVariable("EOT_INSTALLER_TEST_APPDATA", appData);
    Environment.SetEnvironmentVariable("EOT_INSTALLER_TEST_CHANNEL", channel);
    FieldInfo destinationHint = providerType.GetField("DestinationHint", BindingFlags.Static | BindingFlags.Public);
    if (destinationHint == null) throw new Exception("PayloadProvider.DestinationHint is missing");
    destinationHint.SetValue(null, null);

    // Hard safety guard: a production build ignores EOT_INSTALLER_TEST_APPDATA.
    // Refuse to run any cache test unless this assembly resolves to our fixture.
    string actualProfile = (string)Invoke(profileRoot, null);
    Check(String.Equals(Path.GetFullPath(actualProfile).TrimEnd('\\'), Path.GetFullPath(appData).TrimEnd('\\'),
      StringComparison.OrdinalIgnoreCase),
      "Refusing cache test: assembly is not an EOT_INSTALLER_TEST build (ProfileRoot=" + actualProfile + ")");

    object provider = Activator.CreateInstance(providerType);
    PropertyInfo channelVersion = providerType.GetProperty("ChannelVersion", BindingFlags.Instance | BindingFlags.Public);
    Check(channelVersion != null && String.Equals((string)channelVersion.GetValue(provider, null), version, StringComparison.Ordinal),
      "Test release-channel override was not loaded");

    string incomplete = Path.Combine(fixtureRoot, "manual-incomplete");
    WritePayload(incomplete, 5, Encoding.ASCII.GetBytes("12"));
    string issue;
    Check(!PayloadSizesMatch(provider, sizesMatch, incomplete, out issue),
      "Truncated manual payload unexpectedly passed PayloadFileSizesMatch");
    Check(issue != null && issue.IndexOf("tiny.bin has 2 bytes instead of 5", StringComparison.Ordinal) >= 0,
      "Truncated payload issue is not actionable: " + issue);
    Exception incompleteError = ResolveFailure(provider, resolve, incomplete);
    Check(incompleteError != null && incompleteError.GetType().Name == "PayloadUnavailableException",
      "Truncated manual payload was not rejected as PayloadUnavailableException");
    Check(incompleteError.Message.IndexOf("tiny.bin has 2 bytes instead of 5", StringComparison.Ordinal) >= 0,
      "Manual-payload rejection lost file sizes: " + incompleteError.Message);

    string valid = Path.Combine(fixtureRoot, "manual-valid");
    WritePayload(valid, validBytes.Length, validBytes);
    Check(PayloadSizesMatch(provider, sizesMatch, valid, out issue),
      "Valid manual payload failed PayloadFileSizesMatch: " + issue);
    string resolvedManual = Resolve(Activator.CreateInstance(providerType), resolve, valid);
    Check(String.Equals(Path.GetFullPath(resolvedManual), Path.GetFullPath(valid), StringComparison.OrdinalIgnoreCase),
      "Valid manual payload resolved to the wrong folder: " + resolvedManual);

    string cache = Path.Combine(appData, "payloads", version);
    WritePayload(cache, validBytes.Length, Encoding.ASCII.GetBytes("12"));
    File.WriteAllText(cache + ".ready", archiveHash, Encoding.ASCII);
    object repairingProvider = Activator.CreateInstance(providerType);
    string resolvedCache;
    try { resolvedCache = Resolve(repairingProvider, resolve, null); }
    catch (Exception error) {
      PropertyInfo failedReport = providerType.GetProperty("LastReport", BindingFlags.Instance | BindingFlags.Public);
      string details = failedReport == null ? "" : (string)failedReport.GetValue(repairingProvider, null);
      throw new Exception("Ready-cache repair failed: " + error.Message + "\nPayloadProvider report:\n" + details, error);
    }
    Check(String.Equals(Path.GetFullPath(resolvedCache), Path.GetFullPath(cache), StringComparison.OrdinalIgnoreCase),
      "Repaired payload resolved to the wrong cache: " + resolvedCache);
    byte[] repaired = File.ReadAllBytes(Path.Combine(cache, "port", "tiny.bin"));
    Check(BitConverter.ToString(repaired) == BitConverter.ToString(validBytes),
      "Truncated ready cache was reused instead of being replaced from the verified ZIP");
    PropertyInfo lastReport = providerType.GetProperty("LastReport", BindingFlags.Instance | BindingFlags.Public);
    string repairReport = lastReport == null ? "" : (string)lastReport.GetValue(repairingProvider, null);
    Check(repairReport.IndexOf("cache rejected:", StringComparison.OrdinalIgnoreCase) >= 0,
      "Ready-cache rejection was not recorded: " + repairReport);

    object cachedProvider = Activator.CreateInstance(providerType);
    string resolvedAgain = Resolve(cachedProvider, resolve, null);
    string cacheReport = lastReport == null ? "" : (string)lastReport.GetValue(cachedProvider, null);
    Check(String.Equals(Path.GetFullPath(resolvedAgain), Path.GetFullPath(cache), StringComparison.OrdinalIgnoreCase),
      "Valid ready cache resolved to the wrong folder: " + resolvedAgain);
    Check(cacheReport.IndexOf("cache: " + cache, StringComparison.OrdinalIgnoreCase) >= 0,
      "Valid ready cache was not accepted directly: " + cacheReport);
  }

  static void Main(string[] args) {
    if (args.Length != 1) throw new ArgumentException("Pass the EOTInstaller.Tests.exe path");
    string assemblyPath = Path.GetFullPath(args[0]);
    if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Test-host assembly not found", assemblyPath);
    Assembly installer = Assembly.LoadFrom(assemblyPath);
    Type coreType = installer.GetType("EotInstaller.InstallerCore", true);
    string fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(fixtureRoot);
    try {
      TestStreams(coreType);
      TestPayloads(installer, fixtureRoot);
      Console.WriteLine("PASS " + checks + " source-size diagnostics and payload-cache guards");
    } finally {
      Environment.SetEnvironmentVariable("EOT_INSTALLER_TEST_APPDATA", null);
      Environment.SetEnvironmentVariable("EOT_INSTALLER_TEST_CHANNEL", null);
      try { if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, true); } catch { }
      foreach (string archive in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "payload-size-cache-*.zip")) {
        try { File.Delete(archive); } catch { }
      }
    }
  }
}
