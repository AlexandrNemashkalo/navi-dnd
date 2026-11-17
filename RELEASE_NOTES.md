# NaviDnD 2.2.11

- При HTTP 429 игра и установщик повторяют запрос до трёх раз с ожиданием по Retry-After (до пяти минут за попытку); без заголовка — через 30, 60 и 120 секунд. Обновление можно отменить.
- Исправлено применение патчей: изменённый или отсутствующий файл не требует скачивать полную игру, если патч содержит его полную замену или удаляет его.
- Файлы, которые первый патч оставляет нетронутыми, по-прежнему проверяются. Новые файлы проверяются по SHA256 до установки; при ошибке возвращаются исходные локальные файлы.
- Исправление включено в игру и установщик Windows, а также в обновление macOS.

Сохранения и настройки сохраняются. Уже установленный старый загрузчик продолжает использовать прежнюю проверку до обновления. Запуск на настоящем Mac не проверялся.

## English

- The game and installer retry HTTP 429 up to three times, respecting Retry-After (up to five minutes per attempt), or waiting 30, 60 and 120 seconds without it. Updates remain cancellable.
- Patches can repair modified or missing files when a complete replacement is included, or the file is removed, without downloading the entire game.
- Files retained by the first patch are still verified. New payloads are checked against SHA256 before installation; rollback restores the original local files.
- Includes the Windows game and installer, and the macOS updater.

Saves and settings are retained. Older installed updaters keep their previous validation until upgraded. Native macOS execution has not been verified.
