# Project 2099 — V2 BETA 4.1: installer hotfix

## Русский

Исправлен установщик после сообщения **Unexpected source size**. Старое сообщение было слишком общим: «source» могло означать не только образ игры, но и скачанные компоненты PC Edition. Теперь ошибка показывает конкретный файл, откуда он читается, ожидаемый размер и реально прочитанное количество байт.

- Неполный кэш PC Edition больше не принимается только из-за оставшегося маркера готовности. Установщик проверяет наличие и размеры файлов и восстанавливает повреждённый кэш из проверенного ZIP либо заново загружает пакет.
- Добавлена проверка размера каждого распакованного файла ZIP. Неполная папка, выбранная вручную, отклоняется с объяснением, какой файл нужно восстановить.
- Из payload убраны ещё **13 служебных файлов** относительно Beta 4: старые TXT/MD-заметки об обновлениях, схемы для разработчиков, тестовый EXE и отчёты. Лицензии, инструкции игрока и необходимые файлы обновления сохранены.
- **Игровой рантайм, русский перевод и пять исходных языков не менялись.** Проверки хешей и запрет установки поверх непустой папки сохранены.

Обычная установка: скачайте **EOTInstaller-v2.0.0-beta.4.1.exe** — нужный пакет он скачает сам. Для офлайн-установки рядом нужен **EOT-PC-Payload-v2.0.0-beta.4.1.zip**. Не смешивайте EXE и ZIP разных выпусков.

Если Beta 4 уже установлена и работает, переустанавливать игру, переносить сейвы или скачивать этот хотфикс ради FPS не нужно: это исправление установщика, не обновление рантайма.

Проверено: 26 регрессионных проверок чтения и восстановления кэша, проверки диагностики ISO, защита цепочек патчей и преобразования всех пяти известных ревизий в согласованные Original/Russian-пакеты. Полные установки из доступных USA/Europe ISO (полного и компактного) и SazanOFF GOD прошли успешно; результаты сверены с эталонными наборами по 293 файла Original и Russian. Исходная ошибка на этих неповреждённых образах не воспроизвелась; совпадение названия раздачи не доказывает совпадение её байтов.

Это не обещание поддержки любого переделанного или обрезанного ISO. Если ошибка повторится, пришлите **полный новый текст ошибки** и название исходника в [Discord](https://discord.gg/2Yy45gJap3): теперь сообщение показывает проблемный файл, а не заставляет гадать.

## English

Installer-only hotfix following an **Unexpected source size** report. That old message could refer to either the game source or the downloaded PC Edition payload; failures now identify the file, its source, and expected versus actual byte counts.

- Detect missing/truncated files in a previously ready payload cache and restore it from a verified ZIP or download.
- Check each extracted ZIP entry's length and reject incomplete manually selected payload folders with a specific explanation.
- Remove 13 additional developer/test/report files from the installed package. Keep runtime update support, player documentation and required license notices.
- Keep the Beta 4 runtime and language patches unchanged. Hash checks and the empty-destination safeguard remain enabled.

Download **EOTInstaller-v2.0.0-beta.4.1.exe** for online installation; it downloads its matching payload automatically. Offline installs need the matching **EOT-PC-Payload-v2.0.0-beta.4.1.zip**. An already working Beta 4 installation does not need reinstalling for this hotfix.

Full installations from the available full-size and compact USA/Europe ISOs and SazanOFF GOD completed successfully, with their Original and Russian outputs checked against the canonical 293-file inventories. Cache/stream regressions, ISO diagnostics and patch routes for all five known source revisions passed. This does not establish compatibility with every modified dump. If installation fails, send the complete new error to [Discord](https://discord.gg/2Yy45gJap3).
