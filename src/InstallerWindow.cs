using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using IOPath = System.IO.Path;

namespace EotInstaller {
  // GENRY V2 (2026-09-21, Shram): Project 2099 installer window.
  //
  // The V1 wizard walked nine pages in a fixed order: language, welcome,
  // components, source, destination, ready, installing... and if the payload
  // download failed on page three, the whole thing dead-ended there with no way
  // forward. This version keeps the look and the art, and replaces the march
  // with one setup screen holding three independent cards -- game source,
  // install folder, PC Edition files -- that can be filled in any order and
  // each show their own state. INSTALL lights up when all three are green.
  //
  // Convenience over the V1 flow: drag and drop an ISO/ZIP/folder anywhere on
  // the window, the payload resolves by itself in the background while the user
  // picks a source, an offline ZIP can be pointed at by hand, free disk space is
  // checked before the install rather than after it fails, and the failure page
  // states what to do next instead of only what went wrong.
  public sealed class InstallerWindow : Window {
    enum Page { Language, Setup, Installing, Done, Failed }
    enum CardState { Empty, Busy, Ready, Warn, Failed }

    readonly InstallerCore core = new InstallerCore();
    readonly PayloadProvider payloadProvider = new PayloadProvider();
    readonly bool previewMode;
    readonly Grid logicalRoot = new Grid();
    readonly StackPanel pageContent = new StackPanel();
    readonly TextBlock pageTitle = new TextBlock();
    readonly TextBlock pageKicker = new TextBlock();
    readonly TextBlock footerStatus = new TextBlock();
    readonly Button backButton;
    readonly Button nextButton;
    readonly Button closeButton;
    readonly Image miguelImage;
    readonly Image peterImage;
    Border dropHint;

    ProgressBar progressBar;
    TextBlock progressText;
    TextBlock progressDetails;
    TextBlock sourceStatus;
    TextBlock payloadStatus;
    TextBlock destinationStatus;
    ProgressBar payloadProgress;
    Button payloadRetry;
    Button payloadManual;

    Page page = Page.Language;
    int selectedLanguage = 12;
    string sourcePath;
    string payloadPath;
    string destinationPath;
    string payloadError;
    string failureHint;
    SourceProbe probe;
    CardState sourceState = CardState.Empty;
    CardState payloadState = CardState.Empty;
    CancellationTokenSource cancellation;
    CancellationTokenSource payloadCancellation;
    Stopwatch installClock;
    bool payloadRunning;

    public InstallerWindow(bool preview) {
      previewMode = preview;
      Title = "Project 2099: На грани времени";
      Width = 1280; Height = 720; MinWidth = 960; MinHeight = 540;
      WindowStartupLocation = WindowStartupLocation.CenterScreen;
      WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
      Background = Brushes.Black;
      SnapsToDevicePixels = true;
      AllowDrop = true;

      var viewbox = new Viewbox { Stretch = Stretch.Uniform };
      logicalRoot.Width = 1280; logicalRoot.Height = 720;
      viewbox.Child = logicalRoot; Content = viewbox;
      BuildBackdrop();

      peterImage = Character("EOT.peter.png", 250, 108, 540, 540, 0.62);
      miguelImage = Character("EOT.miguel.png", -14, 62, 430, 650, 0.95);
      logicalRoot.Children.Add(peterImage);
      logicalRoot.Children.Add(miguelImage);

      BuildHeader();
      BuildPanel();
      backButton = AccentButton("НАЗАД", false);
      nextButton = AccentButton("ДАЛЕЕ", true);
      closeButton = AccentButton("ВЫХОД", false);
      BuildFooter();
      BuildDropHint();

      backButton.Click += delegate { Back(); };
      nextButton.Click += async delegate { await Next(); };
      closeButton.Click += delegate { if (page == Page.Installing) CancelInstall(); else Close(); };
      KeyDown += OnKeyDown;
      DragEnter += OnDragOver; DragOver += OnDragOver; DragLeave += delegate { ShowDropHint(false); };
      Drop += OnDrop;
      Closed += delegate {
        if (cancellation != null) cancellation.Cancel();
        if (payloadCancellation != null) payloadCancellation.Cancel();
      };
      destinationPath = DefaultDestination();
      RenderPage();
      Loaded += delegate { Fade(logicalRoot); };
    }

    // The PC Edition needs ~14 GB. Documents often sits on a full system drive,
    // so the default lands on the fixed drive with the most room and falls back
    // to Documents only when nothing can be measured.
    static string DefaultDestination() {
      try {
        DriveInfo best = null;
        foreach (DriveInfo drive in DriveInfo.GetDrives()) {
          try {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            if (best == null || drive.AvailableFreeSpace > best.AvailableFreeSpace) best = drive;
          } catch { }
        }
        if (best != null && best.AvailableFreeSpace > (20L << 30))
          return IOPath.Combine(best.RootDirectory.FullName, "Games", "Project 2099");
      } catch { }
      return IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Project 2099");
    }

    // ------------------------------------------------------ preview / automation

    public void PrepareStaticCapture() {
      logicalRoot.BeginAnimation(UIElement.OpacityProperty, null);
      pageContent.BeginAnimation(UIElement.OpacityProperty, null);
      logicalRoot.Opacity = 1; pageContent.Opacity = 1;
    }

    public void PreparePageCapture(int language, string pageName) {
      if (!new[] { 12, 1, 2, 3, 4, 5 }.Contains(language)) throw new ArgumentOutOfRangeException("language");
      selectedLanguage = language;
      string name = (pageName ?? "language").ToLowerInvariant();
      // V1 page names still resolve: the middle of the old wizard is one screen now.
      if (name == "language") page = Page.Language;
      else if (name == "setup" || name == "welcome" || name == "components" || name == "source" ||
               name == "destination" || name == "ready") page = Page.Setup;
      else if (name == "installing") page = Page.Installing;
      else if (name == "done") page = Page.Done;
      else if (name == "failed") page = Page.Failed;
      else throw new ArgumentException("Unknown preview page: " + pageName);
      RenderPage(); PrepareStaticCapture();
    }

    public void ScheduleAutomatedInstall(string source, string destination, string payload, int language) {
      sourcePath = source; destinationPath = destination; payloadPath = payload; selectedLanguage = language;
      Loaded += async delegate { await BeginInstall(); };
    }

    public void PrepareInstallingPreview(bool animate) {
      page = Page.Installing; RenderPage();
      Action<double> update = value => {
        if (progressBar != null) progressBar.Value = value;
        if (progressText != null) progressText.Text = Phase("Распаковка источника") + "  //  " + value.ToString("0.0") + "%\nData/Streams.dat";
        if (progressDetails != null) progressDetails.Text = "5.21 ГБ / 11.92 ГБ     612 МБ/с     осталось ~11 сек";
      };
      update(47.3);
      if (animate) {
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        timer.Tick += delegate { update((DateTime.UtcNow - started).TotalSeconds * 8.5 % 100); };
        timer.Start();
      }
    }

    // ------------------------------------------------------------------ chrome

    static Brush Solid(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }

    void BuildBackdrop() {
      logicalRoot.Background = new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString("#020409"),
        (Color)ColorConverter.ConvertFromString("#071827"), 0);

      var glow = new Rectangle { IsHitTestVisible = false };
      glow.Fill = new RadialGradientBrush {
        Center = new Point(0.33, 0.48), GradientOrigin = new Point(0.33, 0.48), RadiusX = 0.66, RadiusY = 0.92,
        GradientStops = {
          new GradientStop((Color)ColorConverter.ConvertFromString("#8842CFFF"), 0),
          new GradientStop((Color)ColorConverter.ConvertFromString("#44204C85"), 0.38),
          new GradientStop(Colors.Transparent, 1)
        }
      };
      glow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.62, 1.0,
        TimeSpan.FromSeconds(2.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
      logicalRoot.Children.Add(glow);

      var canvas = new Canvas { IsHitTestVisible = false, Opacity = 0.34 };
      for (int x = 0; x <= 1280; x += 32) canvas.Children.Add(new Line {
        X1 = x, X2 = x, Y1 = 95, Y2 = 650, Stroke = Solid(x < 510 ? "#183A6888" : "#1320BFCF"), StrokeThickness = 1 });
      for (int y = 110; y <= 650; y += 24) canvas.Children.Add(new Line {
        X1 = 0, X2 = 1280, Y1 = y, Y2 = y, Stroke = Solid("#1327B8D8"), StrokeThickness = 1 });
      for (int i = 0; i < 13; i++) {
        double x = 360 + i * 24;
        canvas.Children.Add(new Line { X1 = x, Y1 = 70, X2 = x + 150 + i * 7, Y2 = 680,
          Stroke = Solid(i % 2 == 0 ? "#5564DFFF" : "#55FF365C"), StrokeThickness = i % 3 == 0 ? 3 : 1 });
      }
      canvas.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.22, 0.46,
        TimeSpan.FromSeconds(1.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
      logicalRoot.Children.Add(canvas);

      var scan = new Rectangle { IsHitTestVisible = false, Opacity = 0.12 };
      var drawing = new GeometryDrawing(null, new Pen(Solid("#BBD8FFFF"), 1), new LineGeometry(new Point(0, 0), new Point(0, 1)));
      scan.Fill = new DrawingBrush(drawing) { TileMode = TileMode.Tile,
        Viewport = new Rect(0, 0, 1, 4), ViewportUnits = BrushMappingMode.Absolute,
        Viewbox = new Rect(0, 0, 1, 4), ViewboxUnits = BrushMappingMode.Absolute };
      logicalRoot.Children.Add(scan);
    }

    Image Character(string resource, double left, double top, double width, double height, double opacity) {
      var image = new Image { Source = LoadImage(resource), Width = width, Height = height,
        Stretch = Stretch.Uniform, Opacity = opacity, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(left, top, 0, 0) };
      bool miguel = resource.IndexOf("miguel", StringComparison.OrdinalIgnoreCase) >= 0;
      image.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.72,
        Color = (Color)ColorConverter.ConvertFromString(miguel ? "#228CFF" : "#FF264A") };
      var drift = new TranslateTransform(); image.RenderTransform = drift;
      drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 8,
        TimeSpan.FromSeconds(miguel ? 3.4 : 4.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
      return image;
    }

    static BitmapImage LoadImage(string resource) {
      using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)) {
        if (stream == null) throw new FileNotFoundException("Embedded installer art is missing", resource);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
      }
    }

    void BuildHeader() {
      var header = new Grid { Height = 96, VerticalAlignment = VerticalAlignment.Top,
        Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#F20A0E16"),
          (Color)ColorConverter.ConvertFromString("#D9071626"), 0) };
      header.MouseLeftButtonDown += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
      header.Children.Add(new Rectangle { Height = 2, VerticalAlignment = VerticalAlignment.Bottom,
        Fill = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#FFEE294F"),
          (Color)ColorConverter.ConvertFromString("#FF29CFFF"), 0) });
      header.Children.Add(new Image { Source = LoadImage("EOT.project2099_logo.png"), Width = 355, Height = 82,
        Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(32, 2, 0, 0), IsHitTestVisible = false });
      header.Children.Add(new TextBlock { Text = "V2 BETA 1 TEST  //  INSTALLER", Foreground = Solid("#9CE7FF"),
        FontFamily = new FontFamily("Consolas"), FontSize = 13, FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(405, 0, 0, 15), IsHitTestVisible = false });
      var windowButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 17, 18, 0) };
      var minimize = SmallButton("—"); minimize.Click += delegate { WindowState = WindowState.Minimized; };
      var exit = SmallButton("×"); exit.Click += delegate { if (page != Page.Installing) Close(); };
      windowButtons.Children.Add(minimize); windowButtons.Children.Add(exit); header.Children.Add(windowButtons);
      logicalRoot.Children.Add(header);
    }

    Button SmallButton(string text) {
      var button = new Button { Content = text, Width = 42, Height = 32, Margin = new Thickness(5, 0, 0, 0),
        Foreground = Brushes.White, Background = Solid("#4514212F"), BorderBrush = Solid("#665BCDF2"),
        FontSize = 18, Cursor = Cursors.Hand, Focusable = false };
      button.Style = ButtonStyle(false, true);
      return button;
    }

    void BuildPanel() {
      var panel = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch,
        Width = 760, Margin = new Thickness(0, 112, 34, 88) };
      var border = new Border { Background = Solid("#E508111C"), BorderBrush = Solid("#804BD8FF"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(30, 22, 30, 22) };
      border.Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 0, Opacity = 0.80, Color = Colors.Black };
      var stack = new StackPanel();
      pageKicker.FontFamily = new FontFamily("Consolas"); pageKicker.FontSize = 12; pageKicker.Foreground = Solid("#FF5CD8FF");
      pageTitle.FontFamily = new FontFamily("Segoe UI Semibold"); pageTitle.FontWeight = FontWeights.SemiBold;
      pageTitle.FontSize = 32; pageTitle.Foreground = Brushes.White; pageTitle.Margin = new Thickness(0, 5, 0, 16);
      stack.Children.Add(pageKicker); stack.Children.Add(pageTitle); stack.Children.Add(pageContent);
      border.Child = stack; panel.Children.Add(border); logicalRoot.Children.Add(panel);
    }

    void BuildFooter() {
      var footer = new Grid { Height = 74, VerticalAlignment = VerticalAlignment.Bottom, Background = Solid("#F2090D14") };
      footer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
      footer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
      footer.Children.Add(new Rectangle { Fill = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#FF29CFFF"),
        (Color)ColorConverter.ConvertFromString("#FFEE294F"), 0) });
      var bar = new Grid { Margin = new Thickness(34, 0, 34, 0) }; Grid.SetRow(bar, 1);
      footerStatus.VerticalAlignment = VerticalAlignment.Center; footerStatus.Foreground = Solid("#99B7C9D9");
      footerStatus.FontFamily = new FontFamily("Consolas"); footerStatus.FontSize = 12;
      bar.Children.Add(footerStatus);
      var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center };
      buttons.Children.Add(backButton); buttons.Children.Add(nextButton); buttons.Children.Add(closeButton);
      bar.Children.Add(buttons); footer.Children.Add(bar); logicalRoot.Children.Add(footer);
    }

    void BuildDropHint() {
      dropHint = new Border { Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        Background = Solid("#CC04121C"), BorderBrush = Solid("#FF5CD8FF"), BorderThickness = new Thickness(3),
        CornerRadius = new CornerRadius(6), Margin = new Thickness(90, 130, 90, 110) };
      dropHint.Child = new TextBlock { Text = "", Name = "dropText", FontFamily = new FontFamily("Segoe UI Semibold"),
        FontSize = 26, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
      logicalRoot.Children.Add(dropHint);
    }

    Button AccentButton(string text, bool primary) {
      var button = new Button { Content = text, Height = 38, MinWidth = 126, Margin = new Thickness(9, 0, 0, 0),
        Padding = new Thickness(18, 0, 18, 0), FontFamily = new FontFamily("Segoe UI Semibold"),
        FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brushes.White, Cursor = Cursors.Hand, Focusable = false,
        Background = Solid(primary ? "#D41878A2" : "#A914202D"),
        BorderBrush = Solid(primary ? "#FF65DEFF" : "#776F889E"), BorderThickness = new Thickness(1) };
      button.Style = ButtonStyle(primary, false);
      return button;
    }

    Button CardButton(string text, bool primary) {
      var button = AccentButton(text, primary);
      button.Height = 34; button.MinWidth = 0; button.FontSize = 12;
      button.Margin = new Thickness(0, 0, 9, 0); button.Padding = new Thickness(15, 0, 15, 0);
      return button;
    }

    Style ButtonStyle(bool primary, bool compact) {
      var style = new Style(typeof(Button));
      style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
      style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
      style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
      style.Setters.Add(new Setter(Control.BackgroundProperty, Solid(primary ? "#D41878A2" : "#A914202D")));
      style.Setters.Add(new Setter(Control.BorderBrushProperty, Solid(primary ? "#FF65DEFF" : "#776F889E")));
      style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
      style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
      style.Setters.Add(new Setter(Control.PaddingProperty, compact ? new Thickness(0) : new Thickness(18, 0, 18, 0)));
      var border = new FrameworkElementFactory(typeof(Border));
      border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
      border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
      border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
      border.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
      var content = new FrameworkElementFactory(typeof(ContentPresenter));
      content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
      content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
      content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
      border.AppendChild(content);
      var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
      var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
      hover.Setters.Add(new Setter(Control.BackgroundProperty, Solid(primary ? "#FF2398C7" : "#D51E3448")));
      hover.Setters.Add(new Setter(Control.BorderBrushProperty, Solid(primary ? "#FFFFFFFF" : "#FF65DEFF")));
      var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
      pressed.Setters.Add(new Setter(Control.BackgroundProperty, Solid(primary ? "#FF0E5F82" : "#FF102333")));
      pressed.Setters.Add(new Setter(Control.BorderBrushProperty, Solid("#FFFF3159")));
      var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
      disabled.Setters.Add(new Setter(Control.BackgroundProperty, Solid("#66111A23")));
      disabled.Setters.Add(new Setter(Control.BorderBrushProperty, Solid("#443C5468")));
      disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
      template.Triggers.Add(hover); template.Triggers.Add(pressed); template.Triggers.Add(disabled);
      style.Setters.Add(new Setter(Control.TemplateProperty, template));
      return style;
    }

    Style ProgressStyle() {
      var style = new Style(typeof(ProgressBar));
      var grid = new FrameworkElementFactory(typeof(Grid));
      var track = new FrameworkElementFactory(typeof(Border)); track.Name = "PART_Track";
      track.SetValue(Border.BackgroundProperty, Solid("#FF08121C"));
      track.SetValue(Border.BorderBrushProperty, Solid("#704DD9FF")); track.SetValue(Border.BorderThicknessProperty, new Thickness(1));
      var indicator = new FrameworkElementFactory(typeof(Border)); indicator.Name = "PART_Indicator";
      indicator.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
      indicator.SetValue(Border.BackgroundProperty, new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString("#FF16A7D5"), (Color)ColorConverter.ConvertFromString("#FFFF3159"), 0));
      indicator.SetValue(Border.MarginProperty, new Thickness(2));
      grid.AppendChild(track); grid.AppendChild(indicator);
      style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ProgressBar)) { VisualTree = grid }));
      return style;
    }

    TextBlock Body(string text, double size) {
      return new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = size,
        Foreground = Solid("#E6EAF3F8"), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.42,
        Margin = new Thickness(0, 0, 0, 12) };
    }

    TextBlock Mono(string text, string colour) {
      return new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 12.5,
        Foreground = Solid(colour), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 0) };
    }

    Border InfoBox(UIElement child) {
      return new Border { Child = child, Background = Solid("#B50C1C29"), BorderBrush = Solid("#4057D9FF"),
        BorderThickness = new Thickness(1), Padding = new Thickness(14), Margin = new Thickness(0, 8, 0, 12) };
    }

    // One setup card: state dot, title, status text, its own buttons.
    Border Card(CardState state, string title, TextBlock status, params UIElement[] controls) {
      string accent = state == CardState.Ready ? "#FF3BE08A" : state == CardState.Busy || state == CardState.Warn ? "#FFFFC24B"
        : state == CardState.Failed ? "#FFFF5470" : "#FF4BB4E6";
      var border = new Border { Background = Solid("#9E091724"), BorderBrush = Solid(state == CardState.Empty ? "#3350B7E0" : accent),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
        Padding = new Thickness(14, 11, 14, 12), Margin = new Thickness(0, 0, 0, 10) };
      var grid = new Grid();
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      var dot = new Ellipse { Width = 11, Height = 11, Fill = Solid(accent),
        VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0) };
      if (state == CardState.Busy) dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1,
        TimeSpan.FromSeconds(0.65)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
      dot.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Color = (Color)ColorConverter.ConvertFromString(accent), Opacity = 0.9 };
      Grid.SetColumn(dot, 0); grid.Children.Add(dot);

      var stack = new StackPanel(); Grid.SetColumn(stack, 1);
      stack.Children.Add(new TextBlock { Text = title, FontFamily = new FontFamily("Segoe UI Semibold"),
        FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
      stack.Children.Add(status);
      if (controls != null && controls.Length > 0) {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        foreach (UIElement control in controls) if (control != null) row.Children.Add(control);
        stack.Children.Add(row);
      }
      grid.Children.Add(stack); border.Child = grid;
      return border;
    }

    // -------------------------------------------------------------------- pages

    void RenderPage() {
      pageContent.Children.Clear();
      nextButton.IsEnabled = true;
      pageKicker.Text = "PROJECT 2099 INSTALL SYSTEM  //  " + (page == Page.Language ? "01" : page == Page.Setup ? "02" : "03");
      backButton.Content = Nav("back");
      backButton.Visibility = page == Page.Setup ? Visibility.Visible : Visibility.Collapsed;
      nextButton.Visibility = page == Page.Installing || page == Page.Done ? Visibility.Collapsed : Visibility.Visible;
      closeButton.Content = page == Page.Installing ? Nav("cancel") : page == Page.Done ? Nav("close") : Nav("quit");
      footerStatus.Text = previewMode ? "UI PREVIEW  //  GAME DATA IS NOT INCLUDED" :
        "TITLE ID 415608B2  //  " + payloadProvider.ChannelVersion + "  //  " +
        L("НУЖНА ВАША КОПИЯ ДЛЯ XBOX 360", "YOUR OWN XBOX 360 COPY IS REQUIRED", "EIGENE XBOX-360-KOPIE ERFORDERLICH",
          "VOTRE PROPRE COPIE XBOX 360 EST REQUISE", "SERVE UNA TUA COPIA XBOX 360", "SE REQUIERE TU PROPIA COPIA DE XBOX 360");
      peterImage.Opacity = page == Page.Setup ? 0.62 : 0.42;
      miguelImage.Opacity = page == Page.Done ? 1.0 : 0.95;

      if (page == Page.Language) ShowLanguage();
      else if (page == Page.Setup) ShowSetup();
      else if (page == Page.Installing) ShowInstalling();
      else if (page == Page.Done) ShowDone();
      else ShowFailed();
      Fade(pageContent);
    }

    void ShowLanguage() {
      pageTitle.Text = L("ВЫБЕРИТЕ ЯЗЫК", "SELECT LANGUAGE", "SPRACHE WÄHLEN", "CHOISISSEZ LA LANGUE", "SELEZIONA LA LINGUA", "SELECCIONA EL IDIOMA");
      pageContent.Children.Add(Body(L(
        "Язык установщика и язык игры по умолчанию. Все шесть языков ставятся вместе — переключить можно прямо в игре.",
        "The installer language and the game's default. All six languages are installed together and can be switched in-game.",
        "Sprache des Installers und Standardsprache des Spiels. Alle sechs Sprachen werden installiert und sind im Spiel umschaltbar.",
        "Langue de l’installateur et langue par défaut du jeu. Les six langues sont installées et permutables en jeu.",
        "Lingua dell’installer e predefinita del gioco. Tutte e sei le lingue vengono installate e sono selezionabili nel gioco.",
        "Idioma del instalador y predeterminado del juego. Se instalan los seis idiomas y se pueden cambiar dentro del juego."), 16));
      var grid = new UniformGrid { Columns = 2, Rows = 3, Margin = new Thickness(0, 4, 0, 0) };
      AddLanguage(grid, "РУССКИЙ", 12); AddLanguage(grid, "ENGLISH", 1);
      AddLanguage(grid, "DEUTSCH", 2); AddLanguage(grid, "FRANÇAIS", 3);
      AddLanguage(grid, "ITALIANO", 4); AddLanguage(grid, "ESPAÑOL", 5);
      pageContent.Children.Add(grid);
      nextButton.Content = Nav("next");
    }

    void AddLanguage(Panel panel, string name, int id) {
      var button = AccentButton(name, selectedLanguage == id); button.Margin = new Thickness(5); button.MinWidth = 0;
      button.Click += delegate { selectedLanguage = id; RenderPage(); };
      panel.Children.Add(button);
    }

    void ShowSetup() {
      pageTitle.Text = L("СБОРКА PC EDITION", "BUILD THE PC EDITION", "PC EDITION ERSTELLEN",
        "CRÉER LA PC EDITION", "CREA LA PC EDITION", "CREAR LA PC EDITION");
      pageContent.Children.Add(Body(L(
        "Заполните три пункта в любом порядке. ISO, ZIP или папку можно просто перетащить в это окно.",
        "Fill in the three items in any order. An ISO, ZIP or folder can simply be dropped onto this window.",
        "Fülle die drei Punkte in beliebiger Reihenfolge aus. ISO, ZIP oder Ordner kannst du einfach hierher ziehen.",
        "Renseignez les trois points dans n’importe quel ordre. Une ISO, un ZIP ou un dossier peut être glissé sur cette fenêtre.",
        "Completa i tre punti in qualsiasi ordine. Puoi trascinare qui una ISO, uno ZIP o una cartella.",
        "Completa los tres puntos en cualquier orden. Puedes arrastrar aquí una ISO, un ZIP o una carpeta."), 15));

      // --- 1. game source -----------------------------------------------------
      sourceStatus = Mono(SourceText(), probe != null ? "#FFA8F0C8" : sourceState == CardState.Failed ? "#FFFFA9B8" : "#FF9FC4D8");
      var pickIso = CardButton(L("ISO / ZIP", "ISO / ZIP", "ISO / ZIP", "ISO / ZIP", "ISO / ZIP", "ISO / ZIP"), probe == null);
      var pickFolder = CardButton(L("ПАПКА", "FOLDER", "ORDNER", "DOSSIER", "CARTELLA", "CARPETA"), false);
      pickIso.Click += async delegate { await PickIso(); };
      pickFolder.Click += async delegate { await PickFolder(); };
      pageContent.Children.Add(Card(probe != null ? CardState.Ready : sourceState,
        "1  " + L("ВАША КОПИЯ ИГРЫ", "YOUR COPY OF THE GAME", "DEINE SPIELKOPIE", "VOTRE COPIE DU JEU", "LA TUA COPIA DEL GIOCO", "TU COPIA DEL JUEGO"),
        sourceStatus, pickIso, pickFolder));

      // --- 2. destination -----------------------------------------------------
      destinationStatus = Mono(DestinationText(), !DestinationReady() ? "#FFFFA9B8" : DestinationRoomy() ? "#FFA8F0C8" : "#FFFFDFA0");
      var changeFolder = CardButton(L("ИЗМЕНИТЬ", "CHANGE", "ÄNDERN", "MODIFIER", "CAMBIA", "CAMBIAR"), false);
      changeFolder.Click += delegate { PickDestination(); };
      pageContent.Children.Add(Card(!DestinationReady() ? CardState.Empty : DestinationRoomy() ? CardState.Ready : CardState.Warn,
        "2  " + L("КУДА УСТАНОВИТЬ", "WHERE TO INSTALL", "INSTALLATIONSORT", "OÙ INSTALLER", "DOVE INSTALLARE", "DÓNDE INSTALAR"),
        destinationStatus, changeFolder));

      // --- 3. PC Edition files ------------------------------------------------
      payloadStatus = Mono(PayloadText(), payloadPath != null ? "#FFA8F0C8" : payloadState == CardState.Failed ? "#FFFFA9B8" : "#FF9FC4D8");
      payloadProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, Width = 300,
        Value = payloadPath != null ? 100 : 0, Margin = new Thickness(0, 9, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left,
        Visibility = payloadRunning ? Visibility.Visible : Visibility.Collapsed };
      payloadProgress.Style = ProgressStyle();
      payloadRetry = CardButton(L("ПОВТОРИТЬ", "RETRY", "ERNEUT VERSUCHEN", "RÉESSAYER", "RIPROVA", "REINTENTAR"), payloadPath == null);
      payloadManual = CardButton(L("УКАЗАТЬ ФАЙЛЫ", "POINT TO FILES", "DATEIEN WÄHLEN", "INDIQUER LES FICHIERS", "INDICA I FILE", "INDICAR ARCHIVOS"), false);
      payloadRetry.Click += async delegate { await ResolvePayload(null, true); };
      payloadManual.Click += async delegate { await PickPayload(); };
      payloadRetry.IsEnabled = !payloadRunning; payloadManual.IsEnabled = !payloadRunning;
      var payloadCard = Card(payloadPath != null ? CardState.Ready : payloadRunning ? CardState.Busy : payloadState,
        "3  " + L("ФАЙЛЫ PC EDITION", "PC EDITION FILES", "PC-EDITION-DATEIEN", "FICHIERS PC EDITION", "FILE PC EDITION", "ARCHIVOS PC EDITION"),
        payloadStatus, payloadRetry, payloadManual);
      pageContent.Children.Add(payloadCard);
      ((StackPanel)((Grid)payloadCard.Child).Children[1]).Children.Insert(2, payloadProgress);

      pageContent.Children.Add(Mono(L(
        "Игровой образ не входит в установщик и никуда не отправляется: он только читается с вашего диска.",
        "No game image is bundled or uploaded: your copy is only read from your disk.",
        "Kein Spielabbild wird mitgeliefert oder hochgeladen: deine Kopie wird nur gelesen.",
        "Aucune image du jeu n’est fournie ni envoyée : votre copie est seulement lue.",
        "Nessuna immagine del gioco è inclusa o caricata: la tua copia viene solo letta.",
        "No se incluye ni se envía ninguna imagen del juego: tu copia solo se lee."), "#88A7C0D2"));

      nextButton.Content = Nav("install");
      nextButton.IsEnabled = probe != null && payloadPath != null && DestinationReady();
    }

    string SourceText() {
      if (probe != null)
        return String.Format(L("Проверено: {0}\n{1} · ревизия: {2}", "Verified: {0}\n{1} · revision: {2}",
          "Geprüft: {0}\n{1} · Revision: {2}", "Vérifié : {0}\n{1} · révision : {2}",
          "Verificato: {0}\n{1} · revisione: {2}", "Verificado: {0}\n{1} · revisión: {2}"),
          probe.DisplayName, probe.Kind,
          probe.ManifestId == "unknown-revision"
            ? L("не из известных, и это нормально", "not one we know, and that is fine",
                "keine bekannte, und das ist in Ordnung", "inconnue, et ce n’est pas un problème",
                "non tra quelle note, e va bene", "no es de las conocidas, y no pasa nada")
            : probe.Region);
      if (sourceState == CardState.Busy)
        return L("Проверяю ревизию…", "Checking the revision…", "Revision wird geprüft…", "Vérification de la révision…", "Verifica della revisione…", "Comprobando la revisión…");
      if (sourceState == CardState.Failed) return failureHint;
      return L("Xbox 360 ISO, ZIP, GOD или распакованная папка. Корень с Default.xex найдётся сам.",
        "An Xbox 360 ISO, ZIP, GOD container or an extracted folder. The root with Default.xex is found automatically.",
        "Xbox-360-ISO, ZIP, GOD-Container oder entpackter Ordner. Der Stamm mit Default.xex wird selbst gefunden.",
        "Une ISO Xbox 360, un ZIP, un conteneur GOD ou un dossier extrait. La racine avec Default.xex est trouvée automatiquement.",
        "Una ISO Xbox 360, uno ZIP, un contenitore GOD o una cartella estratta. La radice con Default.xex viene trovata da sola.",
        "Una ISO de Xbox 360, un ZIP, un contenedor GOD o una carpeta extraída. La raíz con Default.xex se encuentra sola.");
    }

    bool DestinationReady() {
      if (String.IsNullOrWhiteSpace(destinationPath)) return false;
      try {
        string full = IOPath.GetFullPath(destinationPath);
        if (String.Equals(full.TrimEnd('\\', '/'), (IOPath.GetPathRoot(full) ?? "").TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return false;
        return !(Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()) && FreeBytes(full) >= 0;
      } catch { return false; }
    }

    long NeededBytes() { return probe != null ? probe.RequiredBytes * 2 + (3L << 30) : 14L << 30; }

    bool DestinationRoomy() {
      long free = FreeBytes(destinationPath);
      return free < 0 || free >= NeededBytes();
    }

    string DestinationText() {
      string text = destinationPath ?? "";
      try {
        string full = IOPath.GetFullPath(destinationPath);
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
          return text + "\n" + L("Папка не пуста — выберите другую.", "The folder is not empty — choose another one.",
            "Der Ordner ist nicht leer — wähle einen anderen.", "Le dossier n’est pas vide — choisissez-en un autre.",
            "La cartella non è vuota — scegline un’altra.", "La carpeta no está vacía — elige otra.");
        long free = FreeBytes(full);
        long needed = NeededBytes();
        string line = String.Format(L("Свободно {0}, нужно около {1}.", "{0} free, about {1} needed.",
          "{0} frei, etwa {1} nötig.", "{0} libres, environ {1} nécessaires.",
          "{0} liberi, servono circa {1}.", "{0} libres, se necesitan unos {1}."), FormatBytes(free), FormatBytes(needed));
        if (free >= 0 && free < needed) line += "  " + L("Места может не хватить.", "This may not be enough.",
          "Das könnte zu wenig sein.", "Cela peut être insuffisant.", "Potrebbe non bastare.", "Puede que no sea suficiente.");
        return text + "\n" + line;
      } catch { return text; }
    }

    static long FreeBytes(string path) {
      try { return new DriveInfo(IOPath.GetPathRoot(IOPath.GetFullPath(path))).AvailableFreeSpace; } catch { return -1; }
    }

    string PayloadText() {
      if (payloadPath != null)
        return L("Файлы проверены и готовы.", "The files are verified and ready.", "Die Dateien sind geprüft und bereit.",
          "Les fichiers sont vérifiés et prêts.", "I file sono verificati e pronti.", "Los archivos están verificados y listos.") + "\n" + payloadPath;
      if (payloadRunning)
        return L("Ищу и проверяю файлы…", "Looking for the files and verifying them…", "Dateien werden gesucht und geprüft…",
          "Recherche et vérification des fichiers…", "Ricerca e verifica dei file…", "Buscando y verificando los archivos…");
      if (payloadState == CardState.Failed) return payloadError;
      return L("Рантайм, лаунчер, шрифты и дельты перевода. Берутся рядом с установщиком или скачиваются.",
        "Runtime, launcher, fonts and translation deltas. Taken from next to the installer or downloaded.",
        "Runtime, Launcher, Schriften und Übersetzungs-Deltas. Neben dem Installer oder als Download.",
        "Runtime, launcher, polices et deltas de traduction. Pris à côté de l’installateur ou téléchargés.",
        "Runtime, launcher, font e delta di traduzione. Presi accanto all’installer o scaricati.",
        "Runtime, launcher, fuentes y deltas de traducción. Tomados junto al instalador o descargados.");
    }

    void ShowInstalling() {
      pageTitle.Text = L("СБОРКА PC EDITION", "BUILDING THE PC EDITION", "PC EDITION WIRD ERSTELLT",
        "CONSTRUCTION DE LA PC EDITION", "CREAZIONE DELLA PC EDITION", "CREANDO LA PC EDITION");
      progressText = Body(L("Подготовка…", "Preparing…", "Vorbereitung…", "Préparation…", "Preparazione…", "Preparando…"), 15);
      progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18, Value = 0, Margin = new Thickness(0, 10, 0, 16) };
      progressBar.Style = ProgressStyle();
      progressDetails = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13,
        Foreground = Solid("#FF6DDCFF"), Margin = new Thickness(0, 0, 0, 12) };
      pageContent.Children.Add(progressText); pageContent.Children.Add(progressBar); pageContent.Children.Add(progressDetails);
      pageContent.Children.Add(InfoBox(Body(L(
        "Готовая папка появляется только после полной проверки. «Отмена» удаляет лишь временную папку сборки.",
        "The final folder appears only after full verification. Cancel removes just the temporary build folder.",
        "Der Zielordner erscheint erst nach vollständiger Prüfung. Abbrechen entfernt nur den temporären Ordner.",
        "Le dossier final n’apparaît qu’après vérification complète. Annuler ne supprime que le dossier temporaire.",
        "La cartella finale compare solo dopo la verifica completa. Annulla rimuove solo la cartella temporanea.",
        "La carpeta final aparece solo tras la verificación completa. Cancelar elimina solo la carpeta temporal."), 14)));
    }

    void ShowDone() {
      pageTitle.Text = L("PC EDITION ГОТОВА", "THE PC EDITION IS READY", "PC EDITION IST BEREIT",
        "LA PC EDITION EST PRÊTE", "LA PC EDITION È PRONTA", "LA PC EDITION ESTÁ LISTA");
      pageContent.Children.Add(Body(L(
        "Установка и проверка завершены. Запускайте игру через Launcher.exe — там же настройки, моды и обновления.",
        "Installation and verification are complete. Start through Launcher.exe — settings, mods and updates live there.",
        "Installation und Prüfung sind abgeschlossen. Starte über Launcher.exe — dort liegen Einstellungen, Mods und Updates.",
        "L’installation et la vérification sont terminées. Lancez Launcher.exe — réglages, mods et mises à jour s’y trouvent.",
        "Installazione e verifica completate. Avvia Launcher.exe — lì trovi impostazioni, mod e aggiornamenti.",
        "La instalación y la verificación han terminado. Inicia Launcher.exe — allí están los ajustes, mods y actualizaciones."), 17));
      pageContent.Children.Add(InfoBox(Mono(destinationPath, "#FFA8F0C8")));
      if (core.LastUntranslatedFiles.Count > 0) {
        int expected = core.LastExpectedTranslatedFiles;
        pageContent.Children.Add(Body(L(
          "Игра установлена на английском. Русский текст к этому образу не подошёл: " + core.LastUntranslatedFiles.Count + " файлов из " + expected + " не совпали ни с одной известной сборкой, а перевод работает только целиком — его шрифты лежат в тех же файлах, и без них русские буквы превратились бы в мусор вроде «ÌÈ Ü À». Поэтому русский не ставился вовсе, а не наполовину.\n\nПришлите автору файл Support\\Install\\INSTALL_RECEIPT.json из папки игры — там записаны хеши ваших файлов, и вашу сборку добавят.",
          "The game is installed in English. The Russian text did not fit this dump: " + core.LastUntranslatedFiles.Count + " files out of " + expected + " matched no build we know, and the translation only works as a whole -- its fonts live in those same files, and without them Russian letters would come out as garbage like \"ÌÈ Ü À\". So Russian was not installed at all, rather than halfway.\n\nSend the author Support\\Install\\INSTALL_RECEIPT.json from the game folder: it records your files' hashes, and your build can be added.",
          "Das Spiel ist auf Englisch installiert. Der russische Text passt nicht zu diesem Abbild: " + core.LastUntranslatedFiles.Count + " von " + expected + " Dateien passten zu keinem bekannten Build, und die Übersetzung funktioniert nur als Ganzes -- ihre Schriften liegen in denselben Dateien. Schicke dem Autor Support\\Install\\INSTALL_RECEIPT.json aus dem Spielordner.",
          "Le jeu est installé en anglais. Le texte russe ne correspond pas à cette image : " + core.LastUntranslatedFiles.Count + " fichiers sur " + expected + " ne correspondent à aucune version connue, et la traduction ne fonctionne qu'entière -- ses polices sont dans ces mêmes fichiers. Envoyez à l'auteur Support\\Install\\INSTALL_RECEIPT.json depuis le dossier du jeu.",
          "Il gioco è installato in inglese. Il testo russo non combacia con questa immagine: " + core.LastUntranslatedFiles.Count + " file su " + expected + " non corrispondono a nessuna build nota, e la traduzione funziona solo intera -- i suoi font stanno negli stessi file. Manda all'autore Support\\Install\\INSTALL_RECEIPT.json dalla cartella del gioco.",
          "El juego está instalado en inglés. El texto ruso no encaja con esta imagen: " + core.LastUntranslatedFiles.Count + " archivos de " + expected + " no coinciden con ninguna versión conocida, y la traducción solo funciona entera -- sus fuentes están en esos mismos archivos. Envía al autor Support\\Install\\INSTALL_RECEIPT.json desde la carpeta del juego."), 14));
        pageContent.Children.Add(InfoBox(Mono(
          String.Join("\n", core.LastUntranslatedFiles.Take(6).ToArray()) +
          (core.LastUntranslatedFiles.Count > 6 ? "\n+ " + (core.LastUntranslatedFiles.Count - 6) : ""), "#FFF0D2A0")));
      }
      if (core.LastSourceDeviations.Count > 0) {
        pageContent.Children.Add(Body(L(
          "Файлов, отличных от эталонной копии этой ревизии: " + core.LastSourceDeviations.Count + ". Установка прошла целиком — просто знай, что образ не идеально совпадает с эталоном.",
          "Files differing from the reference copy of this revision: " + core.LastSourceDeviations.Count + ". The installation completed in full — just know the dump is not an exact match.",
          "Dateien, die von der Referenzkopie dieser Revision abweichen: " + core.LastSourceDeviations.Count + ". Die Installation ist vollständig — das Abbild stimmt nur nicht exakt überein.",
          "Fichiers différents de la copie de référence de cette révision : " + core.LastSourceDeviations.Count + ". L’installation est complète — sachez simplement que l’image n’est pas identique.",
          "File diversi dalla copia di riferimento di questa revisione: " + core.LastSourceDeviations.Count + ". L’installazione è completa — sappi solo che l’immagine non combacia esattamente.",
          "Archivos distintos de la copia de referencia de esta revisión: " + core.LastSourceDeviations.Count + ". La instalación se completó — solo debes saber que la imagen no coincide exactamente."), 14));
      }
      var row = new StackPanel { Orientation = Orientation.Horizontal };
      var launch = CardButton(L("ЗАПУСТИТЬ LAUNCHER", "START LAUNCHER", "LAUNCHER STARTEN", "LANCER LE LAUNCHER", "AVVIA LAUNCHER", "INICIAR LAUNCHER"), true);
      launch.Height = 38; launch.FontSize = 13;
      launch.Click += delegate {
        try {
          Process.Start(new ProcessStartInfo(IOPath.Combine(destinationPath, "Launcher.exe")) {
            WorkingDirectory = destinationPath, UseShellExecute = true });
          Close();
        } catch (Exception error) { footerStatus.Text = error.Message; }
      };
      var open = CardButton(L("ОТКРЫТЬ ПАПКУ", "OPEN FOLDER", "ORDNER ÖFFNEN", "OUVRIR LE DOSSIER", "APRI CARTELLA", "ABRIR CARPETA"), false);
      open.Height = 38; open.FontSize = 13;
      open.Click += delegate { try { Process.Start(new ProcessStartInfo(destinationPath) { UseShellExecute = true }); } catch { } };
      row.Children.Add(launch); row.Children.Add(open);
      pageContent.Children.Add(row);
      closeButton.Content = Nav("close");
    }

    void ShowFailed() {
      pageTitle.Text = L("УСТАНОВКА ОСТАНОВЛЕНА", "INSTALLATION STOPPED", "INSTALLATION ANGEHALTEN",
        "INSTALLATION ARRÊTÉE", "INSTALLAZIONE INTERROTTA", "INSTALACIÓN DETENIDA");
      nextButton.Visibility = Visibility.Visible;
      nextButton.Content = L("ВЕРНУТЬСЯ", "GO BACK", "ZURÜCK", "REVENIR", "TORNA INDIETRO", "VOLVER");
      pageContent.Children.Add(InfoBox(Mono(failureHint ?? L("Неизвестная ошибка.", "Unknown error.", "Unbekannter Fehler.", "Erreur inconnue.", "Errore sconosciuto.", "Error desconocido."), "#FFFFC9D2")));
      pageContent.Children.Add(Body(L(
        "Готовая папка не изменена, временные файлы удалены. Исправьте причину и нажмите «Вернуться» — выбранные пункты сохранились.",
        "The destination folder is untouched and temporary files were removed. Fix the cause and press Go back — your choices are kept.",
        "Der Zielordner ist unverändert, temporäre Dateien wurden entfernt. Behebe die Ursache und drücke Zurück — deine Auswahl bleibt erhalten.",
        "Le dossier de destination est intact et les fichiers temporaires ont été supprimés. Corrigez la cause puis revenez — vos choix sont conservés.",
        "La cartella di destinazione è intatta e i file temporanei sono stati rimossi. Risolvi la causa e torna indietro — le scelte restano.",
        "La carpeta de destino está intacta y los archivos temporales se eliminaron. Corrige la causa y vuelve — tus elecciones se conservan."), 14));
    }

    // ------------------------------------------------------------------ actions

    async Task Next() {
      if (page == Page.Language) { page = Page.Setup; RenderPage(); await ResolvePayload(null, false); return; }
      if (page == Page.Setup) { await BeginInstall(); return; }
      if (page == Page.Failed) { page = Page.Setup; RenderPage(); return; }
      RenderPage();
    }

    void Back() { if (page == Page.Setup) { page = Page.Language; RenderPage(); } }

    async Task PickIso() {
      var dialog = new Microsoft.Win32.OpenFileDialog {
        Title = L("Выберите ISO или ZIP с игрой", "Select the game ISO or ZIP", "Spiel-ISO oder ZIP wählen",
          "Sélectionnez l’ISO ou le ZIP du jeu", "Seleziona la ISO o lo ZIP del gioco", "Selecciona la ISO o el ZIP del juego"),
        Filter = "Xbox 360 source (*.iso;*.zip)|*.iso;*.zip|Xbox 360 ISO (*.iso)|*.iso|ZIP archive (*.zip)|*.zip|All files (*.*)|*.*",
        CheckFileExists = true, Multiselect = false };
      if (dialog.ShowDialog(this) == true) await Probe(dialog.FileName);
    }

    async Task PickFolder() {
      using (var dialog = new Forms.FolderBrowserDialog {
        Description = L("Выберите папку игры, её внешнюю папку либо GOD/00007000", "Select the game folder, its outer folder or GOD/00007000",
          "Spielordner, äußeren Ordner oder GOD/00007000 wählen", "Sélectionnez le dossier du jeu, son dossier extérieur ou GOD/00007000",
          "Seleziona la cartella del gioco, quella esterna o GOD/00007000", "Selecciona la carpeta del juego, la exterior o GOD/00007000"),
        ShowNewFolderButton = false })
        if (dialog.ShowDialog() == Forms.DialogResult.OK) await Probe(dialog.SelectedPath);
    }

    void PickDestination() {
      using (var dialog = new Forms.FolderBrowserDialog {
        Description = L("Выберите пустую папку установки", "Select an empty installation folder", "Leeren Installationsordner wählen",
          "Sélectionnez un dossier d’installation vide", "Seleziona una cartella d’installazione vuota", "Selecciona una carpeta de instalación vacía"),
        SelectedPath = Directory.Exists(destinationPath) ? destinationPath : IOPath.GetDirectoryName(destinationPath),
        ShowNewFolderButton = true })
        if (dialog.ShowDialog() == Forms.DialogResult.OK) { destinationPath = dialog.SelectedPath; RenderPage(); }
    }

    async Task PickPayload() {
      var dialog = new Microsoft.Win32.OpenFileDialog {
        Title = L("Выберите EOT-PC-Payload ZIP или payload-manifest.json", "Select the EOT-PC-Payload ZIP or payload-manifest.json",
          "EOT-PC-Payload-ZIP oder payload-manifest.json wählen", "Sélectionnez le ZIP EOT-PC-Payload ou payload-manifest.json",
          "Seleziona lo ZIP EOT-PC-Payload o payload-manifest.json", "Selecciona el ZIP EOT-PC-Payload o payload-manifest.json"),
        Filter = "PC Edition files (*.zip;payload-manifest.json)|*.zip;payload-manifest.json|All files (*.*)|*.*",
        CheckFileExists = true, Multiselect = false };
      if (dialog.ShowDialog(this) != true) return;
      string chosen = dialog.FileName;
      if (String.Equals(IOPath.GetFileName(chosen), "payload-manifest.json", StringComparison.OrdinalIgnoreCase))
        chosen = IOPath.GetDirectoryName(chosen);
      await ResolvePayload(chosen, true);
    }

    async Task Probe(string path) {
      sourcePath = path; probe = null; sourceState = CardState.Busy; RenderPage();
      var token = new CancellationTokenSource();
      try {
        probe = await core.ProbeAsync(path, p => Dispatcher.BeginInvoke(new Action(delegate {
          if (sourceStatus != null) sourceStatus.Text = Phase(p.Phase) + "  " + (p.Ratio * 100).ToString("0") + "%\n" + p.CurrentFile;
        })), token.Token);
        sourceState = CardState.Ready;
      } catch (Exception error) {
        sourceState = CardState.Failed;
        failureHint = L("Источник отклонён: ", "Source rejected: ", "Quelle abgelehnt: ", "Source refusée : ", "Sorgente rifiutata: ", "Origen rechazado: ") + error.Message;
      } finally { token.Dispose(); RenderPage(); }
    }

    async Task ResolvePayload(string manualPath, bool force) {
      // The payload is cached on whichever drive can hold it, and the chosen
      // destination is the best hint we have about that.
      PayloadProvider.DestinationHint = destinationPath;
      if (payloadRunning) return;
      if (payloadPath != null && !force) return;
      payloadRunning = true; payloadState = CardState.Busy; payloadPath = null; RenderPage();
      payloadCancellation = new CancellationTokenSource();
      try {
        payloadPath = await payloadProvider.ResolveAsync(manualPath, p => Dispatcher.BeginInvoke(new Action(delegate {
          if (payloadProgress != null) { payloadProgress.Visibility = Visibility.Visible; payloadProgress.Value = p.Ratio * 100; }
          if (payloadStatus != null) payloadStatus.Text = Phase(p.Phase) + "  " + (p.Ratio * 100).ToString("0.0") + "%\n" +
            p.CurrentFile + (p.TotalBytes > 1 ? "\n" + FormatBytes(p.CompletedBytes) + " / " + FormatBytes(p.TotalBytes) : "");
        })), payloadCancellation.Token);
        payloadState = CardState.Ready; payloadError = null;
      } catch (OperationCanceledException) {
        payloadState = CardState.Empty;
      } catch (Exception error) {
        payloadState = CardState.Failed;
        payloadError = error.Message;
      } finally {
        payloadRunning = false;
        payloadCancellation.Dispose(); payloadCancellation = null;
        RenderPage();
      }
    }

    async Task BeginInstall() {
      page = Page.Installing; RenderPage(); cancellation = new CancellationTokenSource();
      installClock = Stopwatch.StartNew();
      try {
        if (String.IsNullOrWhiteSpace(payloadPath)) throw new InvalidOperationException("PC Edition payload is not ready");
        await core.InstallAsync(sourcePath, destinationPath, payloadPath, selectedLanguage, p => Dispatcher.BeginInvoke(new Action(delegate {
          if (progressBar != null) progressBar.Value = p.Ratio * 100;
          if (progressText != null) progressText.Text = Phase(p.Phase) + "  //  " + (p.Ratio * 100).ToString("0.0") + "%\n" + p.CurrentFile;
          if (progressDetails != null) {
            double seconds = Math.Max(0.05, installClock.Elapsed.TotalSeconds);
            double speed = p.CompletedBytes / seconds;
            double remaining = speed <= 1 ? 0 : Math.Max(0, p.TotalBytes - p.CompletedBytes) / speed;
            progressDetails.Text = FormatBytes(p.CompletedBytes) + " / " + FormatBytes(p.TotalBytes) + "     " +
              FormatBytes((long)speed) + L("/с", "/s", "/s", "/s", "/s", "/s") +
              (remaining > 0 ? L("     осталось ~", "     remaining ~", "     verbleibend ~", "     restant ~", "     rimanente ~", "     restante ~") +
                TimeSpan.FromSeconds(remaining).ToString(remaining >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "");
          }
        })), cancellation.Token);
        page = Page.Done;
      } catch (OperationCanceledException) {
        failureHint = L("Установка отменена.", "The installation was cancelled.", "Die Installation wurde abgebrochen.",
          "L’installation a été annulée.", "L’installazione è stata annullata.", "La instalación fue cancelada.");
        page = Page.Failed;
      } catch (Exception error) {
        failureHint = error.Message; page = Page.Failed;
      } finally {
        if (installClock != null) installClock.Stop();
        cancellation.Dispose(); cancellation = null; RenderPage();
      }
    }

    void CancelInstall() { if (cancellation != null) cancellation.Cancel(); }

    // -------------------------------------------------------- drag and drop

    void OnDragOver(object sender, DragEventArgs e) {
      bool accept = page == Page.Setup && e.Data.GetDataPresent(DataFormats.FileDrop);
      e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
      ShowDropHint(accept);
      e.Handled = true;
    }

    void ShowDropHint(bool visible) {
      if (dropHint == null) return;
      var text = dropHint.Child as TextBlock;
      if (text != null) text.Text = L("Отпустите — я сам пойму, что это: образ игры или файлы PC Edition",
        "Drop it — the installer works out whether it is the game or the PC Edition files",
        "Loslassen — der Installer erkennt selbst, ob es das Spiel oder die PC-Edition-Dateien sind",
        "Déposez — l’installateur détermine lui-même s’il s’agit du jeu ou des fichiers PC Edition",
        "Rilascia — l’installer capisce da solo se è il gioco o i file della PC Edition",
        "Suéltalo — el instalador deduce solo si es el juego o los archivos de PC Edition");
      dropHint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    async void OnDrop(object sender, DragEventArgs e) {
      ShowDropHint(false);
      if (page != Page.Setup || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
      var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
      if (paths == null || paths.Length == 0) return;
      string path = paths[0];
      e.Handled = true;
      // A payload ZIP or payload folder goes to card 3; everything else is a game source.
      string name = IOPath.GetFileName(path.TrimEnd('\\', '/'));
      bool looksLikePayload =
        name.StartsWith("EOT-PC-Payload", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Project2099-Payload", StringComparison.OrdinalIgnoreCase) ||
        (Directory.Exists(path) && File.Exists(IOPath.Combine(path, "payload-manifest.json"))) ||
        (Directory.Exists(path) && File.Exists(IOPath.Combine(path, "payload", "payload-manifest.json")));
      if (looksLikePayload) await ResolvePayload(path, true);
      else if (Directory.Exists(path) && !File.Exists(IOPath.Combine(path, "Default.xex")) && DirectoryIsEmpty(path)) {
        destinationPath = path; RenderPage();               // an empty folder dropped in = install here
      } else await Probe(path);
    }

    static bool DirectoryIsEmpty(string path) {
      try { return !Directory.EnumerateFileSystemEntries(path).Any(); } catch { return false; }
    }

    void OnKeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Escape) {
        if (page == Page.Installing) CancelInstall();
        else if (backButton.Visibility == Visibility.Visible) Back();
        else Close();
        e.Handled = true;
      } else if (e.Key == Key.Enter && nextButton.Visibility == Visibility.Visible && nextButton.IsEnabled) {
        nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true;
      }
    }

    // ------------------------------------------------------------------- text

    string L(string ru, string en, string de, string fr, string it, string es) {
      if (selectedLanguage == 12) return ru;
      if (selectedLanguage == 2) return de ?? en;
      if (selectedLanguage == 3) return fr ?? en;
      if (selectedLanguage == 4) return it ?? en;
      if (selectedLanguage == 5) return es ?? en;
      return en;
    }

    string Phase(string value) {
      string clean = value ?? "";
      if (clean.StartsWith("Проверка источника ", StringComparison.Ordinal))
        return L("Проверка источника", "Checking the source", "Quelle wird geprüft", "Vérification de la source",
          "Verifica della sorgente", "Comprobando el origen") + clean.Substring("Проверка источника".Length);
      if (clean == "Локальный payload") return L(clean, "Local files", "Lokale Dateien", "Fichiers locaux", "File locali", "Archivos locales");
      if (clean == "Payload уже загружен") return L(clean, "Files already downloaded", "Dateien bereits geladen",
        "Fichiers déjà téléchargés", "File già scaricati", "Archivos ya descargados");
      if (clean == "Скачивание PC Edition") return L(clean, "Downloading the PC Edition", "PC Edition wird geladen",
        "Téléchargement de la PC Edition", "Download della PC Edition", "Descargando la PC Edition");
      if (clean == "Проверка архива") return L(clean, "Verifying the archive", "Archiv wird geprüft",
        "Vérification de l’archive", "Verifica dell’archivio", "Verificando el archivo");
      if (clean == "Распаковка payload") return L(clean, "Extracting the files", "Dateien werden entpackt",
        "Extraction des fichiers", "Estrazione dei file", "Extrayendo los archivos");
      if (clean == "PC Edition") return clean;
      if (clean == "Распаковка источника") return L(clean, "Extracting the source", "Quelle wird entpackt",
        "Extraction de la source", "Estrazione della sorgente", "Extrayendo el origen");
      if (clean == "Original data") return L("Исходные данные", clean, "Originaldaten", "Données d’origine", "Dati originali", "Datos originales");
      if (clean == "Russian data") return L("Русские данные", clean, "Russische Daten", "Données russes", "Dati russi", "Datos rusos");
      if (clean == "Готово") return L(clean, "Complete", "Fertig", "Terminé", "Completato", "Completado");
      return clean;
    }

    string Nav(string key) {
      if (key == "next") return L("ДАЛЕЕ", "NEXT", "WEITER", "SUIVANT", "AVANTI", "SIGUIENTE");
      if (key == "back") return L("НАЗАД", "BACK", "ZURÜCK", "RETOUR", "INDIETRO", "ATRÁS");
      if (key == "install") return L("УСТАНОВИТЬ", "INSTALL", "INSTALLIEREN", "INSTALLER", "INSTALLA", "INSTALAR");
      if (key == "cancel") return L("ОТМЕНА", "CANCEL", "ABBRECHEN", "ANNULER", "ANNULLA", "CANCELAR");
      if (key == "close") return L("ЗАКРЫТЬ", "CLOSE", "SCHLIESSEN", "FERMER", "CHIUDI", "CERRAR");
      return L("ВЫХОД", "QUIT", "BEENDEN", "QUITTER", "ESCI", "SALIR");
    }

    static string FormatBytes(long value) {
      if (value < 0) return "—";
      string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" }; double size = value; int unit = 0;
      while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
      return size.ToString(unit == 0 ? "0" : "0.00") + " " + units[unit];
    }

    static void Fade(UIElement element) {
      element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
    }
  }
}
