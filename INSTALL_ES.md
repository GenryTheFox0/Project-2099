# Spider-Man: Edge of Time — PC Edition: instalación

> **Dos épocas. Un destino.**

[English](INSTALL_EN.md) · [Русский](INSTALL_RU.md) · [Deutsch](INSTALL_DE.md) · [Français](INSTALL_FR.md) · [Italiano](INSTALL_IT.md) · [Español](INSTALL_ES.md)

## Qué hay que descargar

Para una instalación normal en línea solo necesitas:

- `EOTInstaller-v1.0.0-beta.3.exe`

El instalador descarga automáticamente `EOT-PC-Payload-v1.0.0-beta.3.zip`, reanuda una transferencia interrumpida y comprueba su tamaño y SHA-256 antes de extraerlo.

Los demás archivos de la publicación son:

- `EOT-PC-Payload-v1.0.0-beta.3.zip` — componentes y parches de PC Edition; no hace falta descargarlo manualmente para la instalación normal;
- `SHA256SUMS.txt` — sumas de comprobación del instalador y del payload;
- `release-manifest.json` — versión, tamaños, hashes y orígenes compatibles;
- `Source code` — código fuente del instalador, no una copia jugable del juego.

## Instalación normal

1. Descarga `EOTInstaller-v1.0.0-beta.3.exe` desde la [publicación oficial del proyecto](https://github.com/GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition/releases/tag/v1.0.0-beta.3).
2. Ejecuta el instalador.
3. Selecciona el idioma del instalador y del juego.
4. Espera mientras se descargan y verifican los componentes de PC Edition.
5. Selecciona tu propio origen compatible de Xbox 360:
   - una ISO XDVDFS retail USA/Europa;
   - un ZIP con el juego, incluso si contiene primero una carpeta `Spider-Man - Edge of Time (USA Europe)`;
   - una carpeta extraída con `Default.xex` y `Data`, o su carpeta principal exterior;
   - el GOD/LIVE/XSF ruso alternativo verificado mediante su carpeta `415608B2/00007000`.
6. Elige una carpeta de destino vacía, por ejemplo `D:\Games\Spider-Man Edge of Time PC Edition`.
7. Pulsa INSTALAR y espera la verificación final.
8. Inicia el juego instalado mediante `Launcher.exe`.

No selecciones la raíz de una unidad como `D:\` ni instales encima de una compilación antigua que ya tenga archivos. Una carpeta vacía evita mezclar versiones diferentes.

Después de una instalación correcta, la ISO, el GOD o la carpeta extraída de consola dejan de ser necesarios. PC Edition funciona desde el destino elegido y no depende de la letra de la unidad original.

## Instalación sin conexión

1. Descarga los dos archivos:
   - `EOTInstaller-v1.0.0-beta.3.exe`;
   - `EOT-PC-Payload-v1.0.0-beta.3.zip`.
2. Crea una carpeta separada para el instalador.
3. Extrae el ZIP junto al EXE con esta estructura:

```text
EOT GitHub Installer\
├── EOTInstaller-v1.0.0-beta.3.exe
└── payload\
    ├── payload-manifest.json
    ├── port\
    └── patches\
```

4. Ejecuta el EXE. El instalador detecta y verifica la carpeta local `payload` sin volver a descargar el archivo.

No cambies el nombre de `payload` ni saques sus archivos internos de la estructura indicada.

Los archivos ISO y ZIP se pueden seleccionar directamente. Al elegir una carpeta, el instalador busca la raíz del juego hasta dos niveles por debajo y también abre automáticamente el único ISO/ZIP que encuentre dentro.

## Orígenes compatibles

- `usa-europe-retail` — versión retail USA/Europa habitual de Xbox 360 en ISO/ZIP;
- `eu-retail` — antigua base multilingüe de PC Edition conservada por compatibilidad;
- `ru-god-alt` — versión rusa alternativa GOD/LIVE/XSF verificada.

Una extensión o una cabecera `LIVE`, `PIRS` o `CON` no es suficiente. El instalador verifica tamaños reales y hashes SHA-256. Las revisiones desconocidas, mezcladas o modificadas se rechazan para evitar una instalación rota.

## Archivos instalados y datos del usuario

La carpeta final contiene `Launcher.exe`, `SpiderManEOT.exe`, `rexruntime.dll`, `spider_man_edge_of_time.toml`, los dos árboles de datos de idioma y la carpeta `Support`. Usa `Launcher.exe` como entrada normal; no hace falta una montaña de archivos BAT.

Ajustes y partidas guardadas:

```text
%USERPROFILE%\Documents\spider_man_edge_of_time
```

Caché de descargas del instalador:

```text
%LOCALAPPDATA%\GenryTheFox\EOTInstaller
```

El juego no necesita esta caché después de una instalación correcta. Puedes conservarla para reinstalar más rápido o eliminarla para recuperar espacio.

## Controles

- teclado y ratón;
- movimiento con WASD;
- sensibilidad del ratón ajustable;
- mando XInput;
- iconos adaptados al dispositivo de entrada activo;
- QTE: **B ↔ E** y **Y ↔ MMB**;
- **MMB significa pulsar la rueda del ratón**, no desplazarla.

## SmartScreen y SHA-256

El instalador todavía no está firmado con un certificado comercial, por lo que Windows SmartScreen puede mostrar un aviso. Descárgalo únicamente desde la publicación oficial de GitHub y compruébalo si es necesario:

```powershell
Get-FileHash .\EOTInstaller-v1.0.0-beta.3.exe -Algorithm SHA256
```

Instalador: `C0462B2C31F841DF0323DE6226EA5CB4E451C85EBF4C29F77974887CF1F8E5FF`

Payload: `13B88F7F6B26D00D97290707008986D7E9A25ED91F2458D50BA631D654EA804D`

## Si algo falla

- Ejecuta de nuevo el instalador: una descarga `.part` interrumpida continúa mediante HTTP Range.
- Un payload dañado se rechaza si su tamaño o SHA-256 no coinciden.
- Si el error de descarga se repite, cierra el instalador, elimina `%LOCALAPPDATA%\GenryTheFox\EOTInstaller\downloads` y vuelve a intentarlo.
- Si se rechaza el origen, comprueba la región y la revisión.
- Para verificar una carpeta ya instalada:

```powershell
.\Launcher.exe --validate
.\SpiderManEOT.exe --verify-install
```

## Problemas conocidos de V1 Beta

- microtirones durante algunas transiciones;
- bloqueos fuertes ocasionales;
- texturas o renderizado blancos en algunas GPU Intel integradas;
- elementos blancos en algunas partes del menú de ajustes;
- posibles problemas de audio en equipos modestos;
- comportamiento dependiente del hardware en el paquete experimental DLSS/ReShade.

Si la opción gráfica experimental causa problemas, desactívala y utiliza el modo D3D12 normal.

En resumen: ejecuta el instalador, elige tu copia compatible de Xbox 360, indica una carpeta vacía, espera la verificación y abre `Launcher.exe`. Sin cien archivos BAT y sin depender del disco de otra persona.
