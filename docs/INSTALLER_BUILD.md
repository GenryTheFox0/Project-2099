# Spider-Man: Edge of Time — PC Edition Installer

Отдельный Windows-мастер установки для PC Edition от GenryTheFox.

Репозиторий и публичная сборка инсталлера не содержат ISO и полные игровые
ресурсы. Пользователь выбирает собственный Xbox 360 ISO либо папку с уже
извлечённым содержимым диска. Инсталлер проверяет точную ревизию файлов,
извлекает ресурсы и поверх них собирает готовую PC Edition.

## Что устанавливается

- принятый PC-runtime и нативный host;
- отдельный `Launcher.exe` с настройками;
- русская локализация;
- PC-управление и динамические подсказки B/E и Y/MMB;
- исправления мыши и аудио;
- шрифты TEMPUS/SANSA;
- проверенный shader seed/cache;
- опциональная DLSS 5-папка из релизного payload;
- исходные языки из пользовательского ISO.

Сам `EOTInstaller.exe` внутрь готовой игры не копируется. В корне установленной
игры остаются обычные `Launcher.exe` и `SpiderManEOT.exe`.

## Поддерживаемый источник

Встроены два точных donor-манифеста: Xbox 360/EU retail и альтернативный
русский GOD/LIVE/XSF. Оба преобразуются в одинаковые финальные деревья
`Data/Original` и `Data/Russian`. Неизвестные или изменённые ревизии
отвергаются до записи файлов.

Поддерживаются:

1. XDVDFS `.iso`;
2. Xbox 360 GOD/SVOD-контейнер либо папка `415608B2/00007000`;
3. папка, в корне которой находится `Default.xex` и каталог `Data`.

## Сборка

```powershell
./build.ps1
```

Результат: `build/EOTInstaller.exe`.

## Подготовка локального тестового payload

```powershell
./prepare_payload.ps1 `
  -ReleaseRoot "X:/path/to/clean/Spider-Man Edge of Time PC V1" `
  -OriginalRoot "X:/path/to/Data/Original" `
  -RussianRoot "X:/path/to/Data/Russian" `
  -AlternateGodRoot "X:/path/to/extracted/alternate-russian-GOD"
```

Скрипт не кладёт ISO в payload. Он копирует только файлы PC Edition, создаёт
SHA-256-манифест исходного диска и формирует встроенные EOTP-блочные патчи для
отличающихся русских ресурсов. В установленной игре не требуется внешний
патчер или дополнительный EXE.

## Проверка без ISO

Для проверки интерфейса можно запустить:

```powershell
./build/EOTInstaller.exe --ui-preview
```

Для полного теста выберите настоящий ISO или распакованную папку. До успешной
проверки `Default.xex` и всего source-манифеста установка не начинается.

## GitHub Release

Публичный installer скачивает один version-pinned ZIP из Releases проекта
`GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition`, поддерживает докачку HTTP
Range, проверяет полный SHA-256 и использует проверенный локальный кэш при
повторном запуске. Пошаговая публикация описана в `PUBLISH_RU.md`.
