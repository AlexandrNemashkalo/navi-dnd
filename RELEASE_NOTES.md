# NaviDnD 2.2.9

- На macOS высота ячейки терминала теперь ровно вдвое больше ширины: рисунки сохраняют пропорции при смене размера шрифта и масштаба экрана.
- Точки рисунков отрисовывает сам терминал с равномерными промежутками внутри и между символами. Их геометрия больше не зависит от Menlo и подстановки шрифтов.
- Автоматический размер шрифта учитывает новую высоту строки. Смена указателя сохранена; отображение Windows не меняется.

Патчи для Windows и macOS (Apple Silicon). Сохранения и настройки сохраняются. Внешний вид на настоящем Mac требует проверки после обновления.

## English

- On macOS, terminal cells now have an exact 1:2 width-to-height ratio, preserving artwork proportions across font sizes and display scales.
- Braille artwork uses built-in dots with uniform spacing within and between characters, independent of Menlo and font fallback.
- Automatic font sizing accounts for the new line height. Mouse pointer support is retained; Windows rendering is unchanged.

Patches for Windows and macOS (Apple Silicon). Saves and settings are retained. Appearance on a physical Mac still needs validation after updating.
