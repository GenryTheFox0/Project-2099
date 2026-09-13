# Spider-Man: Edge of Time — PC Edition : installation

> **Deux époques. Un destin.**

[English](INSTALL_EN.md) · [Русский](INSTALL_RU.md) · [Deutsch](INSTALL_DE.md) · [Français](INSTALL_FR.md) · [Italiano](INSTALL_IT.md) · [Español](INSTALL_ES.md)

## Que faut-il télécharger ?

Pour une installation en ligne normale, téléchargez uniquement :

- `EOTInstaller-v1.0.0-beta.3.exe`

L'installateur télécharge automatiquement `EOT-PC-Payload-v1.0.0-beta.3.zip`, reprend un transfert interrompu et vérifie sa taille ainsi que son SHA-256 avant l'extraction.

Les autres fichiers de la release :

- `EOT-PC-Payload-v1.0.0-beta.3.zip` — composants et correctifs de la PC Edition ; son téléchargement manuel est inutile pour l'installation normale ;
- `SHA256SUMS.txt` — sommes de contrôle de l'installateur et du payload ;
- `release-manifest.json` — version, tailles, hachages et sources prises en charge ;
- `Source code` — code source de l'installateur, pas une copie jouable du jeu.

## Installation normale

1. Téléchargez `EOTInstaller-v1.0.0-beta.3.exe` depuis la [release officielle du projet](https://github.com/GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition/releases/tag/v1.0.0-beta.3).
2. Lancez l'installateur.
3. Choisissez la langue de l'assistant et du jeu.
4. Attendez le téléchargement et la vérification des composants de la PC Edition.
5. Sélectionnez votre propre source Xbox 360 compatible :
   - une ISO XDVDFS retail USA/Europe ;
   - un ZIP contenant le jeu, même avec un dossier extérieur `Spider-Man - Edge of Time (USA Europe)` ;
   - un dossier extrait contenant `Default.xex` et `Data`, ou son dossier parent extérieur ;
   - le GOD/LIVE/XSF russe alternatif testé via son dossier `415608B2/00007000`.
6. Choisissez un dossier d'installation vide, par exemple `D:\Games\Spider-Man Edge of Time PC Edition`.
7. Appuyez sur INSTALLER et attendez la vérification finale.
8. Lancez le jeu installé avec `Launcher.exe`.

Ne choisissez pas la racine d'un disque comme `D:\` et n'installez pas par-dessus une ancienne version non vide. Un dossier séparé évite de mélanger les fichiers de plusieurs versions.

Une fois l'installation terminée, l'ISO, le GOD ou le dossier console extrait n'est plus nécessaire. La PC Edition fonctionne depuis le dossier choisi et ne dépend pas de la lettre du disque source.

## Installation hors ligne

1. Téléchargez les deux fichiers :
   - `EOTInstaller-v1.0.0-beta.3.exe` ;
   - `EOT-PC-Payload-v1.0.0-beta.3.zip`.
2. Créez un dossier réservé à l'installateur.
3. Extrayez le ZIP à côté de l'EXE afin d'obtenir :

```text
EOT GitHub Installer\
├── EOTInstaller-v1.0.0-beta.3.exe
└── payload\
    ├── payload-manifest.json
    ├── port\
    └── patches\
```

4. Lancez l'EXE. L'installateur détecte et vérifie le dossier local `payload` sans retélécharger l'archive.

Ne renommez pas `payload` et ne déplacez pas ses fichiers internes hors de leur arborescence.

Les ISO et ZIP peuvent être choisis directement. Pour un dossier, l’installateur cherche la racine du jeu jusqu’à deux niveaux plus bas et ouvre automatiquement l’unique ISO/ZIP présent dans le dossier sélectionné.

## Sources prises en charge

- `usa-europe-retail` — version retail USA/Europe Xbox 360 courante en ISO/ZIP ;
- `eu-retail` — ancienne base multilingue de la PC Edition conservée pour compatibilité ;
- `ru-god-alt` — version russe alternative GOD/LIVE/XSF testée.

Une extension ou un en-tête `LIVE`, `PIRS` ou `CON` ne suffit pas. L'installateur vérifie les tailles réelles et les SHA-256. Les versions inconnues, mélangées ou modifiées sont refusées afin d'éviter une installation corrompue.

## Fichiers installés et données utilisateur

Le dossier final contient notamment `Launcher.exe`, `SpiderManEOT.exe`, `rexruntime.dll`, `spider_man_edge_of_time.toml`, les deux ensembles de données linguistiques et le dossier `Support`. Utilisez `Launcher.exe` pour démarrer normalement ; aucun tas de fichiers BAT n'est nécessaire.

Paramètres et sauvegardes :

```text
%USERPROFILE%\Documents\spider_man_edge_of_time
```

Cache de téléchargement :

```text
%LOCALAPPDATA%\GenryTheFox\EOTInstaller
```

Le jeu n'utilise plus ce cache après une installation réussie. Gardez-le pour une réinstallation rapide ou supprimez-le pour récupérer de l'espace.

## Commandes

- clavier et souris ;
- déplacement avec WASD ;
- sensibilité de la souris réglable ;
- manette XInput ;
- icônes adaptées au périphérique actif ;
- QTE : **B ↔ E** et **Y ↔ MMB** ;
- **MMB signifie cliquer sur la molette**, pas la faire défiler.

## SmartScreen et SHA-256

L'installateur n'est pas encore signé avec un certificat commercial. Windows SmartScreen peut donc afficher un avertissement. Téléchargez-le uniquement depuis cette release GitHub et vérifiez-le si nécessaire :

```powershell
Get-FileHash .\EOTInstaller-v1.0.0-beta.3.exe -Algorithm SHA256
```

Installateur : `C0462B2C31F841DF0323DE6226EA5CB4E451C85EBF4C29F77974887CF1F8E5FF`

Payload : `13B88F7F6B26D00D97290707008986D7E9A25ED91F2458D50BA631D654EA804D`

## En cas de problème

- Relancez l'installateur : un téléchargement `.part` interrompu reprend avec HTTP Range.
- Un payload endommagé est refusé si sa taille ou son SHA-256 diffère.
- Si l'erreur de téléchargement revient, fermez l'installateur, supprimez `%LOCALAPPDATA%\GenryTheFox\EOTInstaller\downloads`, puis recommencez.
- Si la source est refusée, vérifiez la région et la révision.
- Pour vérifier une installation existante :

```powershell
.\Launcher.exe --validate
.\SpiderManEOT.exe --verify-install
```

## Problèmes connus de la V1 Beta

- micro-saccades pendant certaines transitions ;
- blocages sévères occasionnels ;
- textures ou rendu blancs sur certains GPU Intel intégrés ;
- éléments blancs dans certaines parties des paramètres ;
- problèmes audio possibles sur les machines modestes ;
- comportement variable du paquet expérimental DLSS/ReShade selon le matériel.

Si l'option graphique expérimentale pose problème, désactivez-la et utilisez le mode D3D12 normal.

En bref : lancez l'installateur, choisissez votre copie Xbox 360 compatible, indiquez un dossier vide, attendez la vérification puis démarrez `Launcher.exe`. Sans cent fichiers BAT et sans dépendance au disque d'une autre personne.
