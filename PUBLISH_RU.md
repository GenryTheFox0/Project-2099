# Публикация Project 2099 V2 BETA 1 на GitHub

Тег релиза: **`v2.0.0-beta.1`**
Репозиторий: **`GenryTheFox/Spider-Man-Edge-of-Time-PC-Edition`**

Готовые файлы лежат в `artifacts/release-v2.0.0-beta.1/`:

| Файл | Размер | Что это |
| --- | --- | --- |
| `EOTInstaller-v2.0.0-beta.1.exe` | 2.6 МБ | установщик, в нём зашит канал выпуска |
| `EOT-PC-Payload-v2.0.0-beta.1.zip` | 1.22 ГБ | файлы PC Edition: рантайм, лаунчер, шрифты, дельты перевода |
| `SHA256SUMS.txt` | | контрольные суммы обоих файлов |
| `release-manifest.json` | | состав выпуска в машинном виде |

**В ZIP нет игры.** Там рантайм, лаунчер и двоичные дельты; образ Xbox 360 пользователь даёт свой.
Подробности — в [NO_GAME_DATA.md](NO_GAME_DATA.md).

## 1. Вход в GitHub (делаешь ты, не ИИ)

```bash
gh auth login
```

Выбери `GitHub.com` → `HTTPS` → `Login with a web browser` и вставь код в браузере.
Пароли и токены я не ввожу — это твой аккаунт.

## 2. Исходники в репозиторий

В Git уезжают только исходники. `payload/`, `build/`, `artifacts/`, `tests/` и приватные
QA-папки закрыты в `.gitignore`. Проверено: ни в рабочем дереве, ни в истории коммитов нет
ключей, токенов и личных путей.

```bash
cd D:/EOT_PC_FEATURES_20260905/EOTGitHubInstaller
git add -A
git commit -m "Project 2099 V2 BETA 1: new installer, offline payload, runtime performance work"
git push origin main
```

## 3. Релиз и файлы

```bash
gh release create v2.0.0-beta.1 "artifacts/release-v2.0.0-beta.1/EOTInstaller-v2.0.0-beta.1.exe" "artifacts/release-v2.0.0-beta.1/EOT-PC-Payload-v2.0.0-beta.1.zip" "artifacts/release-v2.0.0-beta.1/SHA256SUMS.txt" "artifacts/release-v2.0.0-beta.1/release-manifest.json" --title "Project 2099 - V2 BETA 1" --notes-file RELEASE_NOTES_RU.md
```

Имя файла payload менять нельзя: установщик ищет ровно
`EOT-PC-Payload-v2.0.0-beta.1.zip` под этим тегом.

## 4. Проверка после заливки

```bash
gh release view v2.0.0-beta.1 --json assets --jq '.assets[] | "\(.name) \(.size)"'
```

Размеры обязаны совпасть с `SHA256SUMS.txt`. Затем скачай установщик в пустую папку и запусти —
он должен сам вытянуть payload по ссылке из релиза.

## 5. Если репозиторий снова снесут

Установщик V2 больше не умирает вместе с GitHub:

- рядом с `EOTInstaller.exe` можно положить `EOT-PC-Payload-*.zip` — он возьмёт его без сети
  (проверено: полный ZIP 1.22 ГБ принят и распакован за 13 секунд, ни одного запроса в сеть);
- рядом можно положить `release-channel.json` с другими ссылками (`PayloadUrl` плюс список
  `Mirrors`) — пересобирать EXE не нужно;
- ZIP или распакованную папку `payload` можно указать кнопкой «УКАЗАТЬ ФАЙЛЫ» или просто
  перетащить в окно;
- если всё недоступно, установщик пишет, какой файл положить рядом и по каким адресам он
  стучался, вместо того чтобы молча упереться в 404, как это делала V1.

Зеркало (GitLab, Telegram-канал, файлообменник) достаточно прописать в
`manifests/release-channel.json` в поле `Mirrors` и пересобрать установщик, либо положить
`release-channel.json` рядом с уже собранным EXE.

## 6. Пересборка выпуска с нуля

```bash
python tools/stage_release_root.py "E:\SpiderManEOT_Port_GENRY\_CANDIDATE_V100_SETTINGS_MODS_20260919" "D:\EOT_PC_FEATURES_20260905\V2_RELEASE_ROOT_20260921"
powershell -File prepare_payload.ps1 -ReleaseRoot ... -OriginalRoot ... -RussianRoot ... -AlternateGodRoot ... -UsaEuropeRoot ...
powershell -File build_release.ps1 -Version v2.0.0-beta.1
```

`stage_release_root.py` сам выбрасывает из сборки игровые данные, моды, бэкапы, отчёты,
тестовые профили и личные настройки, приводит `spider_man_edge_of_time.toml` к «заводскому»
виду и вычищает пути вида `C:\Users\<имя>` из служебных файлов.
