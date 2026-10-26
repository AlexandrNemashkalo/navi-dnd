# NaviDnD 2.2.8

- В macOS-сборку включён собственный Alacritty с поддержкой смены указателя: игра может показывать руку над кнопками и интерактивными объектами и возвращать обычную стрелку.
- Указатель ограничен мышиным режимом игры; сброс терминала и отключение функции очищают выбранную форму.
- Собственная сборка терминала проверяется по исходному коммиту, патчу, архитектуре и SHA256. Тесты протокола выполняются в CI.

Патчи для Windows и macOS (Apple Silicon). Сохранения и настройки сохраняются. Нативное отображение указателя в этой версии требует проверки на Mac.

## English

- The macOS package includes a custom Alacritty build with mouse pointer support: the game can show a hand over buttons and interactive objects and restore the default arrow.
- Application pointers are limited to mouse-reporting mode and cleared by terminal reset or disabling the feature.
- The custom terminal is verified against its source commit, patch, architecture, and SHA256. Protocol tests run in CI.

Patches for Windows and macOS (Apple Silicon). Saves and settings are retained. Native pointer appearance in this version still requires validation on a Mac.
