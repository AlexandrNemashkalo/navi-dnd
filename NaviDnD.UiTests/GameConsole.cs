using System.Runtime.InteropServices;

namespace NaviDnD.UiTests;

// Временно подключается к консоли РЕАЛЬНОГО процесса игры, чтобы прочитать её буфер
// или отправить туда клавишу/клик мышью — так же, как это сделала бы Windows-консоль
// от настоящего пользователя (WriteConsoleInput — тот же API, каким игра сама читает
// ввод через ReadConsoleInput). Сразу после — отключается и возвращается в свою консоль.
internal static class GameConsole
{
    // ВАЖНО: не больше реальной ширины буфера консоли (=WindowWidth, см. ConsoleSetup.SetConsoleConfig).
    // ReadConsoleOutputCharacterW не останавливается на границе строки — запрос длиннее буфера
    // перетекает в начало СЛЕДУЮЩЕЙ строки, и тогда "найденная" колонка не имеет отношения
    // к реальному экрану (ровно так нашёлся баг с сэмплингом пикселя — искали "Рустем" за
    // пределами своей строки). RightBorderCol+1 — точная оправданная ширина рамки.
    public const int RightBorderCol = 148;
    public const int TitleReadWidth = RightBorderCol + 1;
    public const int ScanRowCount = 90;

    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);
    private const ushort KEY_EVENT_TYPE = 0x0001;
    private const ushort MOUSE_EVENT_TYPE = 0x0002;
    private const uint LEFT_BUTTON = 0x0001;
    private const uint MOUSE_MOVED = 0x0001; // dwEventFlags — совпадает по значению с LEFT_BUTTON, но это другое поле

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    // ВАЖНО: без явного CharSet.Unicode дефолт P/Invoke — CharSet.Ansi, что резолвит имя функции
    // в WriteConsoleInputA — тот же INPUT_RECORD, но с ANSI-семантикой символьных полей, из-за
    // чего wVirtualKeyCode=0 + произвольный UnicodeChar (наш SendText для кириллицы) превращается
    // в кракозябры при чтении через Console.ReadKey() на стороне игры (проверено на реальном
    // разломе: "ищу пот" → "8IC ?>B"). WriteConsoleInputW этого не делает.
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WriteConsoleInputW")]
    private static extern bool WriteConsoleInput(IntPtr hConsoleInput, INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    // INPUT_RECORD: EventType(2)+pad(2) + union(16). Union размечен под оба варианта —
    // KEY_EVENT_RECORD и MOUSE_EVENT_RECORD — так же, как в самом Windows Console API.
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;

        [FieldOffset(4)] public int bKeyDown;
        [FieldOffset(8)] public ushort wRepeatCount;
        [FieldOffset(10)] public ushort wVirtualKeyCode;
        [FieldOffset(12)] public ushort wVirtualScanCode;
        [FieldOffset(14)] public char UnicodeChar;
        [FieldOffset(16)] public uint dwControlKeyStateKey;

        [FieldOffset(4)] public short MouseX;
        [FieldOffset(6)] public short MouseY;
        [FieldOffset(8)] public uint dwButtonState;
        [FieldOffset(12)] public uint dwControlKeyStateMouse;
        [FieldOffset(16)] public uint dwEventFlags;
    }

    private static void WithGameConsole(int processId, Action action)
    {
        FreeConsole();
        if (!AttachConsole((uint)processId))
            throw new InvalidOperationException($"Не удалось подключиться к консоли процесса {processId}.");
        try { action(); }
        finally
        {
            FreeConsole();
            AttachConsole(ATTACH_PARENT_PROCESS);
        }
    }

    public static string ReadRow(int processId, int row, int length)
    {
        string result = "";
        WithGameConsole(processId, () => result = ConsoleBufferReader.ReadRow(row, length));
        return result;
    }

    public static ushort[] ReadRowAttributes(int processId, int row, int length)
    {
        ushort[] result = [];
        WithGameConsole(processId, () => result = ConsoleBufferReader.ReadRowAttributes(row, length));
        return result;
    }

    // Реальный отрендеренный цвет (скриншот + сэмплинг пикселя) — в обход легаси-атрибутов,
    // которые не отражают 24-битный ANSI, используемый игрой. Бросает исключение с точной
    // причиной при неудаче (см. ScreenCapture.SampleCellColor).
    public static (byte r, byte g, byte b) SampleCellColor(int processId, int col, int row, (byte r, byte g, byte b) background)
    {
        (byte, byte, byte) result = default;
        WithGameConsole(processId, () => result = ScreenCapture.SampleCellColor(col, row, background));
        return result;
    }

    public static List<string> ReadRows(int processId, int startRow, int rowCount, int length)
    {
        var rows = new List<string>();
        WithGameConsole(processId, () =>
        {
            for (int r = startRow; r < startRow + rowCount; r++)
            {
                try { rows.Add(ConsoleBufferReader.ReadRow(r, length)); }
                catch { rows.Add(""); }
            }
        });
        return rows;
    }

    // Переносит окно консоли игры в левый верхний угол экрана (без изменения размера) —
    // чтобы оно было предсказуемо видно целиком во время теста.
    public static void MoveToTopLeft(int processId)
    {
        WithGameConsole(processId, () =>
        {
            IntPtr hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
        });
    }

    // Отправляет KEY_DOWN — так же, как настоящее нажатие клавиши.
    public static void SendKey(int processId, ConsoleKey key)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            var record = new INPUT_RECORD
            {
                EventType = KEY_EVENT_TYPE,
                bKeyDown = 1,
                wRepeatCount = 1,
                wVirtualKeyCode = (ushort)key,
            };
            WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _);
        });
    }

    // Отправляет строку как последовательность KEY_DOWN с UnicodeChar (без wVirtualKeyCode) —
    // InputBox.GetInput() набирает текст по key.KeyChar (см. её default-ветку), а не по коду
    // клавиши, так что этого достаточно для ввода произвольного текста, включая кириллицу.
    public static void SendText(int processId, string text)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            foreach (char c in text)
            {
                var record = new INPUT_RECORD
                {
                    EventType = KEY_EVENT_TYPE,
                    bKeyDown = 1,
                    wRepeatCount = 1,
                    UnicodeChar = c,
                };
                WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _);
            }
        });
    }

    // Отправляет ДВИЖЕНИЕ мыши в координаты (col, row) без клика — то, на что реагирует
    // наведение (подсветка/курсор), через DrainMouseEvents().latestMove в самой игре.
    public static void SendMouseMove(int processId, int col, int row)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            var record = new INPUT_RECORD
            {
                EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)col,
                MouseY = (short)row,
                dwEventFlags = MOUSE_MOVED,
            };
            WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _);
        });
    }

    // Отправляет клик левой кнопкой мыши по координатам (col, row) — символьные координаты
    // консоли, 0-based, так же, как их видит DrainMouseEvents() в самой игре.
    public static void SendLeftClick(int processId, int col, int row)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            var record = new INPUT_RECORD
            {
                EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)col,
                MouseY = (short)row,
                dwButtonState = LEFT_BUTTON,
            };
            WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _);
        });
    }

    public static void SendMouseWheel(int processId, int col, int row, int notches)
    {
        WithGameConsole(processId, () =>
        {
            using var handle = ConsoleHandles.OpenInput();
            var record = new INPUT_RECORD { EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)col, MouseY = (short)row,
                dwButtonState = unchecked((uint)(notches * 120 << 16)), dwEventFlags = 0x0004 };
            Assert.True(WriteConsoleInput(handle.DangerousGetHandle(), [record], 1, out _));
        });
    }

    // One queued gesture catches bugs where polling consumes movement before starting the drag.
    public static void SendDrag(int processId, int fromCol, int fromRow, int toCol, int toRow)
    {
        WithGameConsole(processId, () =>
        {
            var events = new List<INPUT_RECORD>();
            events.Add(new INPUT_RECORD { EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)fromCol, MouseY = (short)fromRow, dwButtonState = LEFT_BUTTON });
            for (int step = 1; step <= 8; step++)
                events.Add(new INPUT_RECORD { EventType = MOUSE_EVENT_TYPE,
                    MouseX = (short)(fromCol + (toCol - fromCol) * step / 8),
                    MouseY = (short)(fromRow + (toRow - fromRow) * step / 8),
                    dwButtonState = LEFT_BUTTON, dwEventFlags = MOUSE_MOVED });
            events.Add(new INPUT_RECORD { EventType = MOUSE_EVENT_TYPE,
                MouseX = (short)toCol, MouseY = (short)toRow, dwButtonState = 0 });
            using var handle = ConsoleHandles.OpenInput();
            Assert.True(WriteConsoleInput(handle.DangerousGetHandle(), events.ToArray(), (uint)events.Count, out var written));
            Assert.Equal((uint)events.Count, written);
        });
    }

    public static bool WaitForRowContains(int processId, int row, int length, string expected, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            string line;
            try { line = ReadRow(processId, row, length); }
            catch { line = ""; }
            if (line.Contains(expected)) return true;
            Thread.Sleep(150);
        }
        return false;
    }

    // Тот же поиск, но по диапазону строк — для маркеров, которые рисуются не в заголовке
    // экрана (строка 1), а глубже внутри карточки (например, заголовок активной подвкладки).
    public static bool WaitForAnyRowContains(int processId, int startRow, int rowCount, int length, string expected, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            List<string> rows;
            try { rows = ReadRows(processId, startRow, rowCount, length); }
            catch { rows = []; }
            if (rows.Any(r => r.Contains(expected))) return true;
            Thread.Sleep(150);
        }
        return false;
    }

    // Как WaitForAnyRowContains, но с произвольным условием по ВСЕМ строкам сразу — нужно,
    // когда простое "содержит подстроку X" даёт ложное срабатывание на СТАРОМ, ещё не
    // перерисованном экране (например, "Длинный меч" может уже быть виден в обычном списке
    // инвентаря ДО того, как патч переместит его в слот экипировки — Contains("Длинный меч")
    // сработает мгновенно на устаревших данных, а этот метод — только когда УСЛОВИЕ выполнено).
    public static bool WaitForRowsMatch(int processId, int startRow, int rowCount, int length, Func<List<string>, bool> predicate, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            List<string> rows;
            try { rows = ReadRows(processId, startRow, rowCount, length); }
            catch { rows = []; }
            if (predicate(rows)) return true;
            Thread.Sleep(150);
        }
        return false;
    }

    // Как WaitForAnyRowContains, но для случаев с несколькими взаимоисключающими исходами
    // (например, результат броска кубика непредсказуем — "Успех"/"Провал"/крит). Возвращает
    // тот кандидат, который реально появился на экране, или null по таймауту.
    public static string? WaitForAnyRowContainsAny(int processId, int startRow, int rowCount, int length, IReadOnlyList<string> candidates, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            List<string> rows;
            try { rows = ReadRows(processId, startRow, rowCount, length); }
            catch { rows = []; }
            foreach (var candidate in candidates)
                if (rows.Any(r => r.Contains(candidate)))
                    return candidate;
            Thread.Sleep(150);
        }
        return null;
    }
}
