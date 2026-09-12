using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EotInstaller {
  static class Program {
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args) {
      try { SetProcessDPIAware(); } catch { }
      if (args.Length >= 2 && String.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase)) {
        var core = new InstallerCore();
        SourceProbe probe = core.ProbeAsync(args[1], null, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("PASS {0} {1} {2}", probe.Kind, probe.DisplayName, probe.Region);
        return;
      }
      if (args.Length >= 2 && String.Equals(args[0], "--list-svod", StringComparison.OrdinalIgnoreCase)) {
        string container;
        if (!SvodImage.TryFindContainer(args[1], out container)) throw new InvalidDataException("SVOD container was not found");
        using (var image = new SvodImage(container)) {
          Console.WriteLine("CONTAINER {0} FILES {1}", image.Name, image.Count);
          foreach (var entry in image.Entries) Console.WriteLine("{0}\t{1}", entry.Length, entry.Path);
        }
        return;
      }
      if (args.Length >= 3 && String.Equals(args[0], "--extract-svod", StringComparison.OrdinalIgnoreCase)) {
        string container;
        if (!SvodImage.TryFindContainer(args[1], out container)) throw new InvalidDataException("SVOD container was not found");
        string target = Path.GetFullPath(args[2]);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).GetEnumerator().MoveNext())
          throw new IOException("Extraction target must be empty");
        Directory.CreateDirectory(target);
        using (var image = new SvodImage(container)) {
          foreach (var entry in image.Entries.OrderBy(value => value.Path, StringComparer.OrdinalIgnoreCase)) {
            string relative = InstallerCore.NormalizeRelative(entry.Path).Replace('/', Path.DirectorySeparatorChar);
            string output = Path.GetFullPath(Path.Combine(target, relative));
            if (!output.StartsWith(target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
              throw new InvalidDataException("SVOD path escaped extraction root");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (Stream input = image.OpenRead(entry.Path))
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
              input.CopyTo(file, 1024 * 1024);
            Console.WriteLine("EXTRACTED {0}\t{1}", entry.Length, entry.Path);
          }
        }
        Console.WriteLine("PASS extracted " + target);
        return;
      }
      if (args.Length >= 4 && String.Equals(args[0], "--install-test", StringComparison.OrdinalIgnoreCase)) {
        var core = new InstallerCore();
        int lastPercent = -5; string lastPhase = null;
        int installLanguage = args.Length >= 5 ? Int32.Parse(args[4]) : 12;
        core.InstallAsync(args[1], args[2], args[3], installLanguage, p => {
          int percent = (int)(p.Ratio * 100);
          if (!String.Equals(lastPhase, p.Phase, StringComparison.Ordinal) || percent >= lastPercent + 5 || percent == 100) {
            Console.WriteLine("{0:0.0}% {1} {2}", p.Ratio * 100, p.Phase, p.CurrentFile);
            lastPhase = p.Phase; lastPercent = percent;
          }
        },
          CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("PASS installed " + args[2]);
        return;
      }
      if (args.Length >= 1 && String.Equals(args[0], "--payload-test", StringComparison.OrdinalIgnoreCase)) {
        var provider = new PayloadProvider(); int last = -5;
        string path = provider.ResolveAsync(p => { int percent = (int)(p.Ratio * 100); if (percent >= last + 5 || percent == 100) { Console.WriteLine("{0}% {1}", percent, p.Phase); last = percent; } }, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("PASS payload " + path); return;
      }
      bool preview = Array.Exists(args, value => String.Equals(value, "--ui-preview", StringComparison.OrdinalIgnoreCase));
      bool installingPreview = Array.Exists(args, value => String.Equals(value, "--installing-preview", StringComparison.OrdinalIgnoreCase));
      var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
      try {
        var window = new InstallerWindow(preview);
        if (installingPreview) window.PrepareInstallingPreview(true);
        string autoSource = Array.Find(args, value => value.StartsWith("--auto-source=", StringComparison.OrdinalIgnoreCase));
        string autoDestination = Array.Find(args, value => value.StartsWith("--auto-destination=", StringComparison.OrdinalIgnoreCase));
        string autoPayload = Array.Find(args, value => value.StartsWith("--auto-payload=", StringComparison.OrdinalIgnoreCase));
        string autoLanguage = Array.Find(args, value => value.StartsWith("--auto-language=", StringComparison.OrdinalIgnoreCase));
        if (autoSource != null || autoDestination != null || autoPayload != null) {
          if (autoSource == null || autoDestination == null || autoPayload == null) throw new ArgumentException("All auto-install arguments are required");
          window.ScheduleAutomatedInstall(
            autoSource.Substring("--auto-source=".Length), autoDestination.Substring("--auto-destination=".Length),
            autoPayload.Substring("--auto-payload=".Length), autoLanguage == null ? 12 : Int32.Parse(autoLanguage.Substring("--auto-language=".Length)));
        }
        string languageArg = Array.Find(args, value => value.StartsWith("--preview-language=", StringComparison.OrdinalIgnoreCase));
        string pageArg = Array.Find(args, value => value.StartsWith("--preview-page=", StringComparison.OrdinalIgnoreCase));
        if (languageArg != null || pageArg != null) {
          int language = languageArg == null ? 12 : Int32.Parse(languageArg.Substring("--preview-language=".Length));
          string pageName = pageArg == null ? "language" : pageArg.Substring("--preview-page=".Length);
          window.PreparePageCapture(language, pageName);
        }
        app.MainWindow = window;
        string render = Array.Find(args, value => value.StartsWith("--render-preview=", StringComparison.OrdinalIgnoreCase));
        string renderInstalling = Array.Find(args, value => value.StartsWith("--render-installing-preview=", StringComparison.OrdinalIgnoreCase));
        if (renderInstalling != null) { render = "--render-preview=" + renderInstalling.Substring("--render-installing-preview=".Length); window.PrepareInstallingPreview(false); }
        if (render != null) {
          string output = Path.GetFullPath(render.Substring("--render-preview=".Length));
          window.Show(); window.UpdateLayout();
          window.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
          Thread.Sleep(420);
          window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
          window.PrepareStaticCapture(); window.UpdateLayout();
          var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
          bitmap.Render(window);
          var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
          Directory.CreateDirectory(Path.GetDirectoryName(output));
          using (var stream = File.Create(output)) encoder.Save(stream);
          window.Close(); Console.WriteLine("WROTE " + output); return;
        }
        app.Run(window);
      } catch (Exception error) {
        MessageBox.Show(error.ToString(), "Spider-Man: Edge of Time — Installer",
          MessageBoxButton.OK, MessageBoxImage.Error);
        Environment.ExitCode = 1;
      }
    }
  }
}
