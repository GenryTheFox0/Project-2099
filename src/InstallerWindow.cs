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
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using IOPath = System.IO.Path;

namespace EotInstaller {
  public sealed class InstallerWindow : Window {
    enum Page { Language, Welcome, Components, Source, Destination, Ready, Installing, Done, Failed }

    readonly InstallerCore core = new InstallerCore();
    readonly PayloadProvider payloadProvider = new PayloadProvider();
    readonly bool previewMode;
    readonly Grid logicalRoot = new Grid();
    readonly Grid contentPanel = new Grid();
    readonly StackPanel pageContent = new StackPanel();
    readonly TextBlock pageTitle = new TextBlock();
    readonly TextBlock pageKicker = new TextBlock();
    readonly TextBlock footerStatus = new TextBlock();
    readonly Button backButton;
    readonly Button nextButton;
    readonly Button closeButton;
    readonly Image miguelImage;
    readonly Image peterImage;
    ProgressBar progressBar;
    TextBlock progressText;
    TextBlock progressDetails;
    TextBlock sourceStatus;
    TextBlock componentStatus;
    ProgressBar componentProgress;
    TextBlock destinationStatus;
    Page page = Page.Language;
    int selectedLanguage = 12;
    string sourcePath;
    string payloadPath;
    string destinationPath;
    SourceProbe probe;
    CancellationTokenSource cancellation;
    Stopwatch installClock;

    static readonly Dictionary<int, Dictionary<string, string>> TextCatalog =
      new Dictionary<int, Dictionary<string, string>> {
        { 2, new Dictionary<string, string> {
          { "TWO ERAS. ONE DESTINY.", "ZWEI ZEITALTER. EIN SCHICKSAL." },
          { "Welcome to the Spider-Man: Edge of Time — PC Edition installer. It builds the complete port from your Xbox 360 copy and applies the accepted PC fixes.", "Willkommen beim Installer von Spider-Man: Edge of Time — PC Edition. Er erstellt den vollständigen Port aus deiner Xbox-360-Kopie und wendet die geprüften PC-Korrekturen an." },
          { "No ISO is included or downloaded. Provide a supported XDVDFS ISO, GOD/00007000 or an extracted folder containing Default.xex and Data.", "Es wird kein ISO mitgeliefert oder heruntergeladen. Verwende ein unterstütztes XDVDFS-ISO, einen GOD-Container oder einen entpackten Ordner mit Default.xex und Data." },
          { "Russian and original languages, mouse controls, B/E and Y/MMB prompts, audio fixes, fonts and Launcher are installed together.", "Russisch und Originalsprachen, Maussteuerung, B/E- und Y/MMB-Hinweise, Audiokorrekturen, Schriften und Launcher werden gemeinsam installiert." },
          { "The installer downloads only the runtime, Launcher, fixes, fonts and translation deltas from GitHub. Game assets come from your ISO/GOD.", "Der Installer lädt nur Runtime, Launcher, Korrekturen, Schriften und Übersetzungs-Deltas von GitHub. Die Spieldaten stammen aus deinem ISO/GOD." },
          { "Payload has not been verified yet.", "Das Payload wurde noch nicht geprüft." },
          { "Payload is verified and ready.", "Das Payload ist geprüft und bereit." },
          { "DOWNLOAD / VERIFY", "HERUNTERLADEN / PRÜFEN" },
          { "GAME SOURCE", "SPIELQUELLE" },
          { "Select a Spider-Man: Edge of Time USA/Europe ISO, ZIP, GOD or an outer folder. The installer finds the game root automatically.", "Wähle ein USA/Europa-ISO, ZIP, einen GOD-Container oder einen äußeren Spielordner. Der Installer findet den Spielstamm automatisch." },
          { "SELECT ISO / ZIP", "ISO / ZIP WÄHLEN" }, { "SELECT FOLDER", "ORDNER WÄHLEN" },
          { "No source selected.", "Keine Quelle ausgewählt." },
          { "Verified: {0}\nType: {1}\nRevision: {2}", "Geprüft: {0}\nTyp: {1}\nRevision: {2}" },
          { "PC EDITION FOLDER", "PC-EDITION-ORDNER" },
          { "Choose an empty folder. The installer itself is not copied; the result contains Launcher.exe and SpiderManEOT.exe.", "Wähle einen leeren Ordner. Der Installer selbst wird nicht kopiert; das Ergebnis enthält Launcher.exe und SpiderManEOT.exe." },
          { "CHANGE FOLDER", "ORDNER ÄNDERN" }, { "READY TO BUILD", "BEREIT ZUR ERSTELLUNG" },
          { "The source is verified. PC Edition is built in a staging folder and appears at the destination only after full verification.", "Die Quelle ist geprüft. Die PC Edition wird in einem temporären Ordner erstellt und erscheint erst nach vollständiger Prüfung am Ziel." },
          { "ISO/folder: {0}\nDestination: {1}\nSource files: {2}", "ISO/Ordner: {0}\nZiel: {1}\nQuelldateien: {2}" },
          { "Press INSTALL. On an SSD, most time is spent reading the ISO and applying Russian deltas.", "Drücke INSTALLIEREN. Auf einer SSD wird die meiste Zeit zum Lesen der Quelle und Anwenden der Sprach-Patches benötigt." },
          { "BUILDING PC EDITION", "PC EDITION WIRD ERSTELLT" }, { "Preparing…", "Vorbereitung…" },
          { "Do not close the window while files are written. Cancel removes only the temporary staging folder.", "Schließe das Fenster nicht während des Schreibens. Abbrechen entfernt nur den temporären Ordner." },
          { "PC EDITION IS READY", "PC EDITION IST BEREIT" },
          { "Installation and verification are complete. Start through Launcher.exe; all PC fixes are already included.", "Installation und Prüfung sind abgeschlossen. Starte über Launcher.exe; alle PC-Korrekturen sind bereits enthalten." },
          { "START LAUNCHER", "LAUNCHER STARTEN" }, { "INSTALLATION STOPPED", "INSTALLATION ANGEHALTEN" },
          { "BACK TO SOURCE", "ZURÜCK ZUR QUELLE" }, { "Unknown error.", "Unbekannter Fehler." },
          { "The final folder was not changed. Temporary files were removed.", "Der Zielordner wurde nicht verändert. Temporäre Dateien wurden entfernt." },
          { "Select Xbox 360 ISO or ZIP", "Xbox-360-ISO oder ZIP auswählen" }, { "Select the game folder, its outer folder or GOD/00007000", "Spielordner, äußeren Ordner oder GOD/00007000 auswählen" },
          { "Select an empty installation folder", "Leeren Installationsordner auswählen" }, { "Checking revision…", "Revision wird geprüft…" },
          { "Source rejected:\n", "Quelle abgelehnt:\n" }, { "Installation was cancelled.", "Installation wurde abgebrochen." },
          { "Payload verified and ready.\n", "Payload geprüft und bereit.\n" }, { "Could not obtain payload:\n", "Payload konnte nicht geladen werden:\n" }
        } },
        { 3, new Dictionary<string, string> {
          { "TWO ERAS. ONE DESTINY.", "DEUX ÉPOQUES. UN DESTIN." },
          { "Welcome to the Spider-Man: Edge of Time — PC Edition installer. It builds the complete port from your Xbox 360 copy and applies the accepted PC fixes.", "Bienvenue dans l’installateur de Spider-Man: Edge of Time — PC Edition. Il construit le port complet depuis votre copie Xbox 360 et applique les correctifs PC validés." },
          { "No ISO is included or downloaded. Provide a supported XDVDFS ISO, GOD/00007000 or an extracted folder containing Default.xex and Data.", "Aucune image ISO n’est incluse ou téléchargée. Fournissez une image XDVDFS compatible, un conteneur GOD ou un dossier extrait contenant Default.xex et Data." },
          { "Russian and original languages, mouse controls, B/E and Y/MMB prompts, audio fixes, fonts and Launcher are installed together.", "Le russe et les langues d’origine, la souris, les indications B/E et Y/MMB, les correctifs audio, les polices et le Launcher sont installés ensemble." },
          { "The installer downloads only the runtime, Launcher, fixes, fonts and translation deltas from GitHub. Game assets come from your ISO/GOD.", "L’installateur télécharge uniquement le runtime, le Launcher, les correctifs, les polices et les deltas de traduction depuis GitHub. Les ressources du jeu proviennent de votre ISO/GOD." },
          { "Payload has not been verified yet.", "Le payload n’a pas encore été vérifié." }, { "Payload is verified and ready.", "Le payload est vérifié et prêt." },
          { "DOWNLOAD / VERIFY", "TÉLÉCHARGER / VÉRIFIER" }, { "GAME SOURCE", "SOURCE DU JEU" },
          { "Select a Spider-Man: Edge of Time USA/Europe ISO, ZIP, GOD or an outer folder. The installer finds the game root automatically.", "Sélectionnez une ISO USA/Europe, un ZIP, un conteneur GOD ou un dossier extérieur. L’installateur trouve automatiquement la racine du jeu." },
          { "SELECT ISO / ZIP", "CHOISIR ISO / ZIP" }, { "SELECT FOLDER", "CHOISIR LE DOSSIER" }, { "No source selected.", "Aucune source sélectionnée." },
          { "Verified: {0}\nType: {1}\nRevision: {2}", "Vérifié : {0}\nType : {1}\nRévision : {2}" }, { "PC EDITION FOLDER", "DOSSIER PC EDITION" },
          { "Choose an empty folder. The installer itself is not copied; the result contains Launcher.exe and SpiderManEOT.exe.", "Choisissez un dossier vide. L’installateur n’y sera pas copié ; le résultat contiendra Launcher.exe et SpiderManEOT.exe." },
          { "CHANGE FOLDER", "CHANGER DE DOSSIER" }, { "READY TO BUILD", "PRÊT À CONSTRUIRE" },
          { "The source is verified. PC Edition is built in a staging folder and appears at the destination only after full verification.", "La source est vérifiée. La PC Edition est construite dans un dossier temporaire et n’apparaît à destination qu’après vérification complète." },
          { "ISO/folder: {0}\nDestination: {1}\nSource files: {2}", "ISO/dossier : {0}\nDestination : {1}\nFichiers source : {2}" },
          { "Press INSTALL. On an SSD, most time is spent reading the ISO and applying Russian deltas.", "Appuyez sur INSTALLER. Sur un SSD, l’essentiel du temps sert à lire la source et à appliquer les correctifs linguistiques." },
          { "BUILDING PC EDITION", "CONSTRUCTION DE LA PC EDITION" }, { "Preparing…", "Préparation…" },
          { "Do not close the window while files are written. Cancel removes only the temporary staging folder.", "Ne fermez pas la fenêtre pendant l’écriture. Annuler supprime uniquement le dossier temporaire." },
          { "PC EDITION IS READY", "PC EDITION PRÊTE" },
          { "Installation and verification are complete. Start through Launcher.exe; all PC fixes are already included.", "L’installation et la vérification sont terminées. Lancez Launcher.exe ; tous les correctifs PC sont déjà inclus." },
          { "START LAUNCHER", "LANCER LE LAUNCHER" }, { "INSTALLATION STOPPED", "INSTALLATION ARRÊTÉE" }, { "BACK TO SOURCE", "RETOUR À LA SOURCE" },
          { "Unknown error.", "Erreur inconnue." }, { "The final folder was not changed. Temporary files were removed.", "Le dossier final n’a pas été modifié. Les fichiers temporaires ont été supprimés." },
          { "Select Xbox 360 ISO or ZIP", "Sélectionner une ISO Xbox 360 ou un ZIP" }, { "Select the game folder, its outer folder or GOD/00007000", "Sélectionner le dossier du jeu, son dossier extérieur ou GOD/00007000" },
          { "Select an empty installation folder", "Sélectionner un dossier d’installation vide" }, { "Checking revision…", "Vérification de la révision…" },
          { "Source rejected:\n", "Source refusée :\n" }, { "Installation was cancelled.", "L’installation a été annulée." },
          { "Payload verified and ready.\n", "Payload vérifié et prêt.\n" }, { "Could not obtain payload:\n", "Impossible d’obtenir le payload :\n" }
        } },
        { 4, new Dictionary<string, string> {
          { "TWO ERAS. ONE DESTINY.", "DUE EPOCHE. UN SOLO DESTINO." },
          { "Welcome to the Spider-Man: Edge of Time — PC Edition installer. It builds the complete port from your Xbox 360 copy and applies the accepted PC fixes.", "Benvenuto nell’installer di Spider-Man: Edge of Time — PC Edition. Crea il port completo dalla tua copia Xbox 360 e applica le correzioni PC verificate." },
          { "No ISO is included or downloaded. Provide a supported XDVDFS ISO, GOD/00007000 or an extracted folder containing Default.xex and Data.", "Nessuna ISO è inclusa o scaricata. Fornisci una ISO XDVDFS supportata, un contenitore GOD o una cartella estratta con Default.xex e Data." },
          { "Russian and original languages, mouse controls, B/E and Y/MMB prompts, audio fixes, fonts and Launcher are installed together.", "Russo e lingue originali, controlli del mouse, prompt B/E e Y/MMB, correzioni audio, font e Launcher vengono installati insieme." },
          { "The installer downloads only the runtime, Launcher, fixes, fonts and translation deltas from GitHub. Game assets come from your ISO/GOD.", "L’installer scarica da GitHub solo runtime, Launcher, correzioni, font e delta di traduzione. Le risorse del gioco provengono dalla tua ISO/GOD." },
          { "Payload has not been verified yet.", "Il payload non è ancora stato verificato." }, { "Payload is verified and ready.", "Il payload è verificato e pronto." },
          { "DOWNLOAD / VERIFY", "SCARICA / VERIFICA" }, { "GAME SOURCE", "SORGENTE DEL GIOCO" },
          { "Select a Spider-Man: Edge of Time USA/Europe ISO, ZIP, GOD or an outer folder. The installer finds the game root automatically.", "Seleziona una ISO USA/Europa, un ZIP, un contenitore GOD o una cartella esterna. L’installer trova automaticamente la radice del gioco." },
          { "SELECT ISO / ZIP", "SELEZIONA ISO / ZIP" }, { "SELECT FOLDER", "SELEZIONA CARTELLA" }, { "No source selected.", "Nessuna sorgente selezionata." },
          { "Verified: {0}\nType: {1}\nRevision: {2}", "Verificato: {0}\nTipo: {1}\nRevisione: {2}" }, { "PC EDITION FOLDER", "CARTELLA PC EDITION" },
          { "Choose an empty folder. The installer itself is not copied; the result contains Launcher.exe and SpiderManEOT.exe.", "Scegli una cartella vuota. L’installer non viene copiato; il risultato contiene Launcher.exe e SpiderManEOT.exe." },
          { "CHANGE FOLDER", "CAMBIA CARTELLA" }, { "READY TO BUILD", "PRONTO ALLA CREAZIONE" },
          { "The source is verified. PC Edition is built in a staging folder and appears at the destination only after full verification.", "La sorgente è verificata. La PC Edition viene creata in una cartella temporanea e appare nella destinazione solo dopo la verifica completa." },
          { "ISO/folder: {0}\nDestination: {1}\nSource files: {2}", "ISO/cartella: {0}\nDestinazione: {1}\nFile sorgente: {2}" },
          { "Press INSTALL. On an SSD, most time is spent reading the ISO and applying Russian deltas.", "Premi INSTALLA. Su un SSD, la maggior parte del tempo serve a leggere la sorgente e applicare le patch linguistiche." },
          { "BUILDING PC EDITION", "CREAZIONE PC EDITION" }, { "Preparing…", "Preparazione…" },
          { "Do not close the window while files are written. Cancel removes only the temporary staging folder.", "Non chiudere la finestra durante la scrittura. Annulla rimuove soltanto la cartella temporanea." },
          { "PC EDITION IS READY", "PC EDITION PRONTA" },
          { "Installation and verification are complete. Start through Launcher.exe; all PC fixes are already included.", "Installazione e verifica completate. Avvia Launcher.exe; tutte le correzioni PC sono già incluse." },
          { "START LAUNCHER", "AVVIA LAUNCHER" }, { "INSTALLATION STOPPED", "INSTALLAZIONE INTERROTTA" }, { "BACK TO SOURCE", "TORNA ALLA SORGENTE" },
          { "Unknown error.", "Errore sconosciuto." }, { "The final folder was not changed. Temporary files were removed.", "La cartella finale non è stata modificata. I file temporanei sono stati rimossi." },
          { "Select Xbox 360 ISO or ZIP", "Seleziona ISO Xbox 360 o ZIP" }, { "Select the game folder, its outer folder or GOD/00007000", "Seleziona la cartella del gioco, quella esterna o GOD/00007000" },
          { "Select an empty installation folder", "Seleziona una cartella d’installazione vuota" }, { "Checking revision…", "Verifica della revisione…" },
          { "Source rejected:\n", "Sorgente rifiutata:\n" }, { "Installation was cancelled.", "L’installazione è stata annullata." },
          { "Payload verified and ready.\n", "Payload verificato e pronto.\n" }, { "Could not obtain payload:\n", "Impossibile ottenere il payload:\n" }
        } },
        { 5, new Dictionary<string, string> {
          { "TWO ERAS. ONE DESTINY.", "DOS ÉPOCAS. UN DESTINO." },
          { "Welcome to the Spider-Man: Edge of Time — PC Edition installer. It builds the complete port from your Xbox 360 copy and applies the accepted PC fixes.", "Bienvenido al instalador de Spider-Man: Edge of Time — PC Edition. Crea el port completo desde tu copia de Xbox 360 y aplica las correcciones de PC verificadas." },
          { "No ISO is included or downloaded. Provide a supported XDVDFS ISO, GOD/00007000 or an extracted folder containing Default.xex and Data.", "No se incluye ni se descarga ninguna ISO. Usa una ISO XDVDFS compatible, un contenedor GOD o una carpeta extraída con Default.xex y Data." },
          { "Russian and original languages, mouse controls, B/E and Y/MMB prompts, audio fixes, fonts and Launcher are installed together.", "El ruso y los idiomas originales, el ratón, las indicaciones B/E y Y/MMB, las correcciones de audio, las fuentes y el Launcher se instalan juntos." },
          { "The installer downloads only the runtime, Launcher, fixes, fonts and translation deltas from GitHub. Game assets come from your ISO/GOD.", "El instalador descarga de GitHub solo el runtime, Launcher, correcciones, fuentes y deltas de traducción. Los recursos del juego proceden de tu ISO/GOD." },
          { "Payload has not been verified yet.", "El payload aún no se ha verificado." }, { "Payload is verified and ready.", "El payload está verificado y listo." },
          { "DOWNLOAD / VERIFY", "DESCARGAR / VERIFICAR" }, { "GAME SOURCE", "ORIGEN DEL JUEGO" },
          { "Select a Spider-Man: Edge of Time USA/Europe ISO, ZIP, GOD or an outer folder. The installer finds the game root automatically.", "Selecciona una ISO USA/Europa, un ZIP, un contenedor GOD o una carpeta exterior. El instalador encuentra automáticamente la raíz del juego." },
          { "SELECT ISO / ZIP", "SELECCIONAR ISO / ZIP" }, { "SELECT FOLDER", "SELECCIONAR CARPETA" }, { "No source selected.", "No se ha seleccionado ningún origen." },
          { "Verified: {0}\nType: {1}\nRevision: {2}", "Verificado: {0}\nTipo: {1}\nRevisión: {2}" }, { "PC EDITION FOLDER", "CARPETA DE PC EDITION" },
          { "Choose an empty folder. The installer itself is not copied; the result contains Launcher.exe and SpiderManEOT.exe.", "Elige una carpeta vacía. El instalador no se copia; el resultado contiene Launcher.exe y SpiderManEOT.exe." },
          { "CHANGE FOLDER", "CAMBIAR CARPETA" }, { "READY TO BUILD", "LISTO PARA CREAR" },
          { "The source is verified. PC Edition is built in a staging folder and appears at the destination only after full verification.", "El origen está verificado. PC Edition se crea en una carpeta temporal y solo aparece en el destino tras la verificación completa." },
          { "ISO/folder: {0}\nDestination: {1}\nSource files: {2}", "ISO/carpeta: {0}\nDestino: {1}\nArchivos de origen: {2}" },
          { "Press INSTALL. On an SSD, most time is spent reading the ISO and applying Russian deltas.", "Pulsa INSTALAR. En un SSD, la mayor parte del tiempo se dedica a leer el origen y aplicar los parches de idioma." },
          { "BUILDING PC EDITION", "CREANDO PC EDITION" }, { "Preparing…", "Preparando…" },
          { "Do not close the window while files are written. Cancel removes only the temporary staging folder.", "No cierres la ventana mientras se escriben archivos. Cancelar solo elimina la carpeta temporal." },
          { "PC EDITION IS READY", "PC EDITION ESTÁ LISTA" },
          { "Installation and verification are complete. Start through Launcher.exe; all PC fixes are already included.", "La instalación y la verificación han terminado. Inicia Launcher.exe; todas las correcciones de PC ya están incluidas." },
          { "START LAUNCHER", "INICIAR LAUNCHER" }, { "INSTALLATION STOPPED", "INSTALACIÓN DETENIDA" }, { "BACK TO SOURCE", "VOLVER AL ORIGEN" },
          { "Unknown error.", "Error desconocido." }, { "The final folder was not changed. Temporary files were removed.", "La carpeta final no se modificó. Se eliminaron los archivos temporales." },
          { "Select Xbox 360 ISO or ZIP", "Seleccionar ISO de Xbox 360 o ZIP" }, { "Select the game folder, its outer folder or GOD/00007000", "Seleccionar la carpeta del juego, la carpeta exterior o GOD/00007000" },
          { "Select an empty installation folder", "Seleccionar una carpeta de instalación vacía" }, { "Checking revision…", "Comprobando la revisión…" },
          { "Source rejected:\n", "Origen rechazado:\n" }, { "Installation was cancelled.", "La instalación fue cancelada." },
          { "Payload verified and ready.\n", "Payload verificado y listo.\n" }, { "Could not obtain payload:\n", "No se pudo obtener el payload:\n" }
        } }
      };

    public InstallerWindow(bool preview) {
      previewMode = preview;
      Title = "Spider-Man: Edge of Time — PC Edition Installer";
      Width = 1280; Height = 720; MinWidth = 960; MinHeight = 540;
      WindowStartupLocation = WindowStartupLocation.CenterScreen;
      WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
      Background = Brushes.Black;
      SnapsToDevicePixels = true;

      var viewbox = new Viewbox { Stretch = Stretch.Uniform };
      logicalRoot.Width = 1280; logicalRoot.Height = 720;
      viewbox.Child = logicalRoot; Content = viewbox;
      BuildBackdrop();

      peterImage = Character("EOT.peter.png", 276, 104, 540, 540, 0.68);
      miguelImage = Character("EOT.miguel.png", -4, 60, 430, 650, 0.97);
      logicalRoot.Children.Add(peterImage);
      logicalRoot.Children.Add(miguelImage);

      BuildHeader();
      BuildPanel();
      backButton = AccentButton("НАЗАД", false);
      nextButton = AccentButton("ДАЛЕЕ", true);
      closeButton = AccentButton("ВЫХОД", false);
      BuildFooter();

      backButton.Click += delegate { Back(); };
      nextButton.Click += async delegate { await Next(); };
      closeButton.Click += delegate { if (page == Page.Installing) CancelInstall(); else Close(); };
      KeyDown += OnKeyDown;
      Closed += delegate { if (cancellation != null) cancellation.Cancel(); };
      destinationPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Spider-Man Edge of Time PC Edition");
      RenderPage();
      Loaded += delegate { Fade(logicalRoot); };
    }

    public void PrepareStaticCapture() {
      logicalRoot.BeginAnimation(UIElement.OpacityProperty, null);
      pageContent.BeginAnimation(UIElement.OpacityProperty, null);
      logicalRoot.Opacity = 1; pageContent.Opacity = 1;
    }

    public void PreparePageCapture(int language, string pageName) {
      if (!new[] { 12, 1, 2, 3, 4, 5 }.Contains(language)) throw new ArgumentOutOfRangeException("language");
      selectedLanguage = language;
      string name = (pageName ?? "language").ToLowerInvariant();
      if (name == "language") page = Page.Language;
      else if (name == "welcome") page = Page.Welcome;
      else if (name == "components") page = Page.Components;
      else if (name == "source") page = Page.Source;
      else if (name == "destination") page = Page.Destination;
      else if (name == "ready") page = Page.Ready;
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
        if (progressText != null) progressText.Text = "Распаковка источника  //  " + value.ToString("0.0") + "%\nData/Streams.dat";
        if (progressDetails != null) progressDetails.Text = "5.21 ГБ / 11.92 ГБ     612 МБ/с     осталось ~11 сек";
      };
      update(47.3);
      if (animate) {
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        timer.Tick += delegate {
          double value = (DateTime.UtcNow - started).TotalSeconds * 8.5 % 100;
          update(value);
        };
        timer.Start();
      }
    }

    static Brush Solid(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }

    void BuildBackdrop() {
      logicalRoot.Background = new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString("#020409"),
        (Color)ColorConverter.ConvertFromString("#071827"), 0);

      var glow = new Rectangle { IsHitTestVisible = false };
      glow.Fill = new RadialGradientBrush {
        Center = new Point(0.35, 0.48), GradientOrigin = new Point(0.35, 0.48), RadiusX = 0.66, RadiusY = 0.92,
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

      var canvas = new Canvas { IsHitTestVisible = false, Opacity = 0.38 };
      for (int x = 0; x <= 1280; x += 32) canvas.Children.Add(new Line {
        X1 = x, X2 = x, Y1 = 95, Y2 = 650, Stroke = Solid(x < 510 ? "#183A6888" : "#1320BFCF"), StrokeThickness = 1
      });
      for (int y = 110; y <= 650; y += 24) canvas.Children.Add(new Line {
        X1 = 0, X2 = 1280, Y1 = y, Y2 = y, Stroke = Solid("#1327B8D8"), StrokeThickness = 1
      });
      for (int i = 0; i < 13; i++) {
        double x = 360 + i * 24;
        canvas.Children.Add(new Line { X1 = x, Y1 = 70, X2 = x + 150 + i * 7, Y2 = 680,
          Stroke = Solid(i % 2 == 0 ? "#5564DFFF" : "#55FF365C"), StrokeThickness = i % 3 == 0 ? 3 : 1 });
      }
      canvas.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.22, 0.48,
        TimeSpan.FromSeconds(1.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
      logicalRoot.Children.Add(canvas);

      var scan = new Rectangle { IsHitTestVisible = false, Opacity = 0.13 };
      var drawing = new GeometryDrawing(null, new Pen(Solid("#BBD8FFFF"), 1),
        new LineGeometry(new Point(0, 0), new Point(0, 1)));
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
      image.Effect = new System.Windows.Media.Effects.DropShadowEffect {
        BlurRadius = 28, ShadowDepth = 0, Opacity = 0.72,
        Color = resource.IndexOf("miguel", StringComparison.OrdinalIgnoreCase) >= 0 ?
          (Color)ColorConverter.ConvertFromString("#228CFF") : (Color)ColorConverter.ConvertFromString("#FF264A")
      };
      var drift = new TranslateTransform(); image.RenderTransform = drift;
      double seconds = resource.IndexOf("miguel", StringComparison.OrdinalIgnoreCase) >= 0 ? 3.4 : 4.2;
      drift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 8,
        TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
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
      var title = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(42, 16, 0, 0) };
      title.Children.Add(new TextBlock { Text = "SPIDER-MAN: EDGE OF TIME", Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI Semibold"), FontSize = 29, FontWeight = FontWeights.Bold });
      title.Children.Add(new TextBlock { Text = "PC EDITION  //  INSTALLER", Foreground = Solid("#65DFFF"),
        FontFamily = new FontFamily("Consolas"), FontSize = 15, Margin = new Thickness(2, 3, 0, 0) });
      header.Children.Add(title);
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
      contentPanel.HorizontalAlignment = HorizontalAlignment.Right;
      contentPanel.VerticalAlignment = VerticalAlignment.Stretch;
      contentPanel.Width = 720;
      contentPanel.Margin = new Thickness(0, 116, 38, 92);
      var border = new Border { Background = Solid("#E508111C"), BorderBrush = Solid("#804BD8FF"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(32, 25, 32, 24) };
      border.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 32, ShadowDepth = 0,
        Opacity = 0.80, Color = Colors.Black };
      var stack = new StackPanel();
      pageKicker.FontFamily = new FontFamily("Consolas"); pageKicker.FontSize = 12;
      pageKicker.Foreground = Solid("#FF5CD8FF");
      pageTitle.FontFamily = new FontFamily("Segoe UI Semibold"); pageTitle.FontWeight = FontWeights.SemiBold;
      pageTitle.FontSize = 34; pageTitle.Foreground = Brushes.White; pageTitle.Margin = new Thickness(0, 6, 0, 20);
      stack.Children.Add(pageKicker); stack.Children.Add(pageTitle); stack.Children.Add(pageContent);
      border.Child = stack; contentPanel.Children.Add(border); logicalRoot.Children.Add(contentPanel);
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

    Button AccentButton(string text, bool primary) {
      var button = new Button { Content = text, Height = 38, MinWidth = 126, Margin = new Thickness(9, 0, 0, 0),
        Padding = new Thickness(18, 0, 18, 0), FontFamily = new FontFamily("Segoe UI Semibold"),
        FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brushes.White, Cursor = Cursors.Hand, Focusable = false,
        Background = Solid(primary ? "#D41878A2" : "#A914202D"),
        BorderBrush = Solid(primary ? "#FF65DEFF" : "#776F889E"), BorderThickness = new Thickness(1) };
      button.Style = ButtonStyle(primary, false);
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
        Foreground = Solid("#E6EAF3F8"), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45,
        Margin = new Thickness(0, 0, 0, 14) };
    }

    TextBlock Label(string text) {
      return new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 12,
        Foreground = Solid("#FF62D9FF"), Margin = new Thickness(0, 7, 0, 6) };
    }

    Border InfoBox(UIElement child) {
      return new Border { Child = child, Background = Solid("#B50C1C29"), BorderBrush = Solid("#4057D9FF"),
        BorderThickness = new Thickness(1), Padding = new Thickness(14), Margin = new Thickness(0, 8, 0, 12) };
    }

    void RenderPage() {
      pageContent.Children.Clear();
      nextButton.IsEnabled = true;
      pageKicker.Text = "GENRY EOT INSTALL SYSTEM  //  " + ((int)page + 1).ToString("00");
      backButton.Content = Nav("back");
      backButton.Visibility = page == Page.Language || page == Page.Installing || page == Page.Done ? Visibility.Collapsed : Visibility.Visible;
      nextButton.Visibility = page == Page.Installing || page == Page.Done || page == Page.Failed ? Visibility.Collapsed : Visibility.Visible;
      closeButton.Content = page == Page.Installing ? Nav("cancel") : Nav("quit");
      footerStatus.Text = previewMode ? "UI PREVIEW  //  GAME DATA IS NOT INCLUDED" :
        "TITLE ID 415608B2  //  " + payloadProvider.ChannelVersion + "  //  XBOX 360 SOURCE REQUIRED";
      peterImage.Opacity = page == Page.Source || page == Page.Ready ? 0.66 : 0.42;
      miguelImage.Opacity = page == Page.Done ? 1.0 : 0.95;

      if (page == Page.Language) ShowLanguage();
      else if (page == Page.Welcome) ShowWelcome();
      else if (page == Page.Components) ShowComponents();
      else if (page == Page.Source) ShowSource();
      else if (page == Page.Destination) ShowDestination();
      else if (page == Page.Ready) ShowReady();
      else if (page == Page.Installing) ShowInstalling();
      else if (page == Page.Done) ShowDone();
      else ShowFailed();
      Fade(pageContent);
    }

    void ShowLanguage() {
      pageTitle.Text = L("ВЫБЕРИТЕ ЯЗЫК", "SELECT LANGUAGE", "SPRACHE WÄHLEN", "CHOISISSEZ LA LANGUE", "SELEZIONA LA LINGUA", "SELECCIONA EL IDIOMA");
      pageContent.Children.Add(Body(L(
        "Выбранный язык используется мастером и станет языком по умолчанию. Все шесть языков всё равно устанавливаются вместе.",
        "The selected language is used by the installer and becomes the default. All six languages are installed together.",
        "Die gewählte Sprache wird im Installer und als Standardsprache verwendet. Alle sechs Sprachen werden installiert.",
        "La langue choisie sera utilisée par l’installateur et par défaut. Les six langues seront installées.",
        "La lingua scelta verrà usata dall’installer e come predefinita. Verranno installate tutte e sei le lingue.",
        "El idioma elegido se usará en el instalador y como predeterminado. Se instalarán los seis idiomas."), 16));
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

    void ShowWelcome() {
      pageTitle.Text = L("ДВЕ ЭПОХИ. ОДНА СУДЬБА.", "TWO ERAS. ONE DESTINY.");
      pageContent.Children.Add(Body(L(
        "Добро пожаловать в установщик Spider-Man: Edge of Time — PC Edition. Он соберёт готовый порт из вашей копии Xbox 360 и добавит принятые PC-фиксы.",
        "Welcome to the Spider-Man: Edge of Time — PC Edition installer. It builds the complete port from your Xbox 360 copy and applies the accepted PC fixes."), 18));
      pageContent.Children.Add(InfoBox(Body(L(
        "ISO не входит в комплект и ниоткуда не скачивается. Подойдёт поддерживаемый XDVDFS ISO, GOD/00007000 либо папка с Default.xex и Data.",
        "No ISO is included or downloaded. Provide a supported XDVDFS ISO, GOD/00007000 or an extracted folder containing Default.xex and Data."), 14)));
      pageContent.Children.Add(Body(L("Установятся русский перевод, оригинальные языки, управление мышью, B/E и Y/MMB, аудиофиксы, шрифты и Launcher.",
        "Russian and original languages, mouse controls, B/E and Y/MMB prompts, audio fixes, fonts and Launcher are installed together."), 15));
      nextButton.Content = Nav("next");
    }

    void ShowComponents() {
      pageTitle.Text = L("ФАЙЛЫ PC EDITION", "PC EDITION FILES", "PC-EDITION-DATEIEN", "FICHIERS PC EDITION", "FILE PC EDITION", "ARCHIVOS PC EDITION");
      pageContent.Children.Add(Body(L(
        "Инсталлер скачает с GitHub только runtime, Launcher, исправления, шрифты и дельты перевода. Игровые ресурсы берутся из выбранного вами ISO/GOD.",
        "The installer downloads only the runtime, Launcher, fixes, fonts and translation deltas from GitHub. Game assets come from your ISO/GOD."), 16));
      componentStatus = Body(payloadPath == null ? L("Проверка payload ещё не завершена.", "Payload has not been verified yet.") :
        L("Payload найден и готов к установке.", "Payload is verified and ready."), 14);
      pageContent.Children.Add(InfoBox(componentStatus));
      componentProgress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 16, Value = payloadPath == null ? 0 : 100,
        Foreground = Solid("#FF32CBFF"), Background = Solid("#FF0C1A25"), Margin = new Thickness(0, 4, 0, 15) };
      componentProgress.Style = ProgressStyle();
      pageContent.Children.Add(componentProgress);
      var download = AccentButton(L("СКАЧАТЬ / ПРОВЕРИТЬ", "DOWNLOAD / VERIFY"), true);
      download.Click += async delegate { await EnsurePayload(); }; pageContent.Children.Add(download);
      nextButton.Content = Nav("next"); nextButton.IsEnabled = payloadPath != null;
    }

    void ShowSource() {
      pageTitle.Text = L("ИСТОЧНИК ИГРЫ", "GAME SOURCE");
      pageContent.Children.Add(Body(L("Укажите USA/Europe ISO, ZIP, GOD либо внешнюю папку игры. Инсталлер сам найдёт корень с Default.xex.",
        "Select a Spider-Man: Edge of Time USA/Europe ISO, ZIP, GOD or an outer folder. The installer finds the game root automatically."), 17));
      var row = new StackPanel { Orientation = Orientation.Horizontal };
      var iso = AccentButton(L("ВЫБРАТЬ ISO / ZIP", "SELECT ISO / ZIP"), true);
      var folder = AccentButton(L("ВЫБРАТЬ ПАПКУ", "SELECT FOLDER"), false);
      iso.Click += async delegate { await PickIso(); }; folder.Click += async delegate { await PickFolder(); };
      row.Children.Add(iso); row.Children.Add(folder); pageContent.Children.Add(row);
      sourceStatus = Body(probe == null ? L("Источник ещё не выбран.", "No source selected.") :
        String.Format(L("Проверено: {0}\nТип: {1}\nРевизия: {2}", "Verified: {0}\nType: {1}\nRevision: {2}"),
          probe.DisplayName, probe.Kind, probe.Region), 14);
      pageContent.Children.Add(InfoBox(sourceStatus));
      nextButton.Content = Nav("next"); nextButton.IsEnabled = probe != null;
    }

    void ShowDestination() {
      pageTitle.Text = L("ПАПКА PC EDITION", "PC EDITION FOLDER");
      pageContent.Children.Add(Body(L("Выберите пустую папку. Инсталлер туда не копируется — внутри останутся Launcher.exe и SpiderManEOT.exe.",
        "Choose an empty folder. The installer itself is not copied; the result contains Launcher.exe and SpiderManEOT.exe."), 17));
      var choose = AccentButton(L("ИЗМЕНИТЬ ПАПКУ", "CHANGE FOLDER"), true);
      choose.Click += delegate { PickDestination(); }; pageContent.Children.Add(choose);
      destinationStatus = Body(destinationPath, 14); pageContent.Children.Add(InfoBox(destinationStatus));
      nextButton.Content = Nav("next"); nextButton.IsEnabled = !String.IsNullOrWhiteSpace(destinationPath);
    }

    void ShowReady() {
      pageTitle.Text = L("ГОТОВО К СБОРКЕ", "READY TO BUILD");
      pageContent.Children.Add(Body(L("Источник проверен. PC Edition будет собрана во временной папке и появится в выбранном месте только после полной проверки.",
        "The source is verified. PC Edition is built in a staging folder and appears at the destination only after full verification."), 17));
      pageContent.Children.Add(InfoBox(Body(String.Format(L("ISO/папка: {0}\nУстановка: {1}\nФайлов источника: {2}",
        "ISO/folder: {0}\nDestination: {1}\nSource files: {2}"), sourcePath, destinationPath, core.Manifest.Files.Count), 14)));
      pageContent.Children.Add(Body(L("Нажмите «УСТАНОВИТЬ». На SSD основное время займёт чтение ISO и применение русских дельт.",
        "Press INSTALL. On an SSD, most time is spent reading the ISO and applying Russian deltas."), 14));
      nextButton.Content = Nav("install"); nextButton.IsEnabled = true;
    }

    void ShowInstalling() {
      pageTitle.Text = L("СБОРКА PC EDITION", "BUILDING PC EDITION");
      progressText = Body(L("Подготовка…", "Preparing…"), 15);
      progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 18, Value = 0,
        Foreground = Solid("#FF32CBFF"), Background = Solid("#FF0C1A25"), Margin = new Thickness(0, 10, 0, 18) };
      progressBar.Style = ProgressStyle();
      progressDetails = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13,
        Foreground = Solid("#FF6DDCFF"), Margin = new Thickness(0, 0, 0, 12) };
      pageContent.Children.Add(progressText); pageContent.Children.Add(progressBar); pageContent.Children.Add(progressDetails);
      pageContent.Children.Add(InfoBox(Body(L("Не закрывайте окно во время записи. Отмена удаляет только временную staging-папку.",
        "Do not close the window while files are written. Cancel removes only the temporary staging folder."), 14)));
    }

    void ShowDone() {
      pageTitle.Text = L("PC EDITION ГОТОВА", "PC EDITION IS READY");
      pageContent.Children.Add(Body(L("Установка и проверка завершены. Запускайте игру через Launcher.exe — все PC-фиксы уже находятся в сборке.",
        "Installation and verification are complete. Start through Launcher.exe; all PC fixes are already included."), 18));
      var launch = AccentButton(L("ЗАПУСТИТЬ LAUNCHER", "START LAUNCHER"), true);
      launch.Click += delegate { string file = IOPath.Combine(destinationPath, "Launcher.exe");
        Process.Start(new ProcessStartInfo(file) { WorkingDirectory = destinationPath, UseShellExecute = true }); Close(); };
      pageContent.Children.Add(launch);
      closeButton.Content = Nav("close");
    }

    void ShowFailed() {
      pageTitle.Text = L("УСТАНОВКА ОСТАНОВЛЕНА", "INSTALLATION STOPPED");
      nextButton.Visibility = Visibility.Visible; nextButton.Content = L("НАЗАД К ИСТОЧНИКУ", "BACK TO SOURCE");
      pageContent.Children.Add(InfoBox(Body(footerStatus.Tag as string ?? L("Неизвестная ошибка.", "Unknown error."), 14)));
      pageContent.Children.Add(Body(L("Готовая папка не изменена. Временные файлы удалены.",
        "The final folder was not changed. Temporary files were removed."), 14));
    }

    async Task PickIso() {
      var dialog = new Microsoft.Win32.OpenFileDialog { Title = L("Выберите Xbox 360 ISO или ZIP", "Select Xbox 360 ISO or ZIP"),
        Filter = "Xbox 360 source (*.iso;*.zip)|*.iso;*.zip|Xbox 360 ISO (*.iso)|*.iso|ZIP archive (*.zip)|*.zip|All files (*.*)|*.*", CheckFileExists = true, Multiselect = false };
      if (dialog.ShowDialog(this) == true) await Probe(dialog.FileName);
    }

    async Task PickFolder() {
      using (var dialog = new Forms.FolderBrowserDialog { Description = L("Выберите папку игры, её внешнюю папку либо GOD/00007000", "Select the game folder, its outer folder or GOD/00007000"),
        ShowNewFolderButton = false }) if (dialog.ShowDialog() == Forms.DialogResult.OK) await Probe(dialog.SelectedPath);
    }

    void PickDestination() {
      using (var dialog = new Forms.FolderBrowserDialog { Description = L("Выберите пустую папку установки", "Select an empty installation folder"),
        SelectedPath = Directory.Exists(destinationPath) ? destinationPath : IOPath.GetDirectoryName(destinationPath), ShowNewFolderButton = true })
        if (dialog.ShowDialog() == Forms.DialogResult.OK) { destinationPath = dialog.SelectedPath; RenderPage(); }
    }

    async Task Probe(string path) {
      sourcePath = path; probe = null; sourceStatus.Text = L("Проверяю ревизию…", "Checking revision…");
      try {
        var token = new CancellationTokenSource();
        probe = await core.ProbeAsync(path, p => Dispatcher.BeginInvoke(new Action(delegate {
          sourceStatus.Text = Phase(p.Phase) + "\n" + p.CurrentFile + "\n" + (p.Ratio * 100).ToString("0") + "%";
        })), token.Token);
        RenderPage();
      } catch (Exception error) {
        sourceStatus.Text = L("Источник отклонён:\n", "Source rejected:\n") + error.Message;
        nextButton.IsEnabled = false;
      }
    }

    async Task Next() {
      if (page == Page.Language) page = Page.Welcome;
      else if (page == Page.Welcome) { page = Page.Components; RenderPage(); await EnsurePayload(); return; }
      else if (page == Page.Components && payloadPath != null) page = Page.Source;
      else if (page == Page.Source && probe != null) page = Page.Destination;
      else if (page == Page.Destination) page = Page.Ready;
      else if (page == Page.Ready) { await BeginInstall(); return; }
      else if (page == Page.Failed) page = Page.Source;
      RenderPage();
    }

    void Back() {
      if (page == Page.Welcome) page = Page.Language;
      else if (page == Page.Components) page = Page.Welcome;
      else if (page == Page.Source) page = Page.Components;
      else if (page == Page.Destination) page = Page.Source;
      else if (page == Page.Ready) page = Page.Destination;
      RenderPage();
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
        footerStatus.Tag = L("Установка отменена пользователем.", "Installation was cancelled."); page = Page.Failed;
      } catch (Exception error) {
        footerStatus.Tag = error.Message; page = Page.Failed;
      } finally { if (installClock != null) installClock.Stop(); cancellation.Dispose(); cancellation = null; RenderPage(); }
    }

    async Task EnsurePayload() {
      if (page != Page.Components || payloadPath != null) return;
      nextButton.IsEnabled = false;
      var token = new CancellationTokenSource();
      try {
        payloadPath = await payloadProvider.ResolveAsync(p => Dispatcher.BeginInvoke(new Action(delegate {
          if (componentProgress != null) componentProgress.Value = p.Ratio * 100;
          if (componentStatus != null) componentStatus.Text = Phase(p.Phase) + "  //  " + (p.Ratio * 100).ToString("0.0") + "%\n" + p.CurrentFile;
        })), token.Token);
        if (componentStatus != null) componentStatus.Text = L("Payload проверен и готов.\n", "Payload verified and ready.\n") + payloadPath;
        if (componentProgress != null) componentProgress.Value = 100;
        nextButton.IsEnabled = true;
      } catch (Exception error) {
        if (componentStatus != null) componentStatus.Text = L("Не удалось получить payload:\n", "Could not obtain payload:\n") + error.Message;
      } finally { token.Dispose(); }
    }

    void CancelInstall() { if (cancellation != null) cancellation.Cancel(); }

    void OnKeyDown(object sender, KeyEventArgs e) {
      if (e.Key == Key.Escape) { if (page == Page.Installing) CancelInstall(); else if (backButton.Visibility == Visibility.Visible) Back(); else Close(); e.Handled = true; }
      else if (e.Key == Key.Enter && nextButton.Visibility == Visibility.Visible && nextButton.IsEnabled) { nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
    }

    string L(string ru, string en, string de = null, string fr = null, string it = null, string es = null) {
      if (selectedLanguage == 12) return ru;
      Dictionary<string, string> catalog;
      if (selectedLanguage == 2) return de ?? (TextCatalog.TryGetValue(2, out catalog) && catalog.ContainsKey(en) ? catalog[en] : en);
      if (selectedLanguage == 3) return fr ?? (TextCatalog.TryGetValue(3, out catalog) && catalog.ContainsKey(en) ? catalog[en] : en);
      if (selectedLanguage == 4) return it ?? (TextCatalog.TryGetValue(4, out catalog) && catalog.ContainsKey(en) ? catalog[en] : en);
      if (selectedLanguage == 5) return es ?? (TextCatalog.TryGetValue(5, out catalog) && catalog.ContainsKey(en) ? catalog[en] : en);
      return en;
    }

    string Phase(string value) {
      string clean = value;
      if (clean.StartsWith("Проверка источника ", StringComparison.Ordinal))
        return L("Проверка источника", "Checking source", "Quelle wird geprüft", "Vérification de la source", "Verifica della sorgente", "Comprobando el origen") + clean.Substring("Проверка источника".Length);
      if (clean == "Локальный payload") return L(clean, "Local payload", "Lokales Payload", "Payload local", "Payload locale", "Payload local");
      if (clean == "Payload уже загружен") return L(clean, "Payload already downloaded", "Payload bereits geladen", "Payload déjà téléchargé", "Payload già scaricato", "Payload ya descargado");
      if (clean == "Скачивание PC Edition") return L(clean, "Downloading PC Edition", "PC Edition wird geladen", "Téléchargement de la PC Edition", "Download della PC Edition", "Descargando PC Edition");
      if (clean == "Распаковка payload") return L(clean, "Extracting payload", "Payload wird entpackt", "Extraction du payload", "Estrazione del payload", "Extrayendo el payload");
      if (clean == "PC Edition") return clean;
      if (clean == "Распаковка источника") return L(clean, "Extracting source", "Quelle wird entpackt", "Extraction de la source", "Estrazione della sorgente", "Extrayendo el origen");
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
      string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" }; double size = Math.Max(0, value); int unit = 0;
      while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
      return size.ToString(unit == 0 ? "0" : "0.00") + " " + units[unit];
    }

    static void Fade(UIElement element) {
      element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
    }
  }
}
