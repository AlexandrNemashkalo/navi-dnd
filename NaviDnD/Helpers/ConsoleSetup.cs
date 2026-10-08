using NaviDnD.Data;
using NaviDnD.Data.Models;
using System.Runtime.InteropServices;
using System.Text;

namespace NaviDnD;

internal static class ConsoleSetup
{
    private delegate IntPtr LowLevelKeyboardProcDelegate(int nCode, IntPtr wParam, IntPtr lParam);
    private static LowLevelKeyboardProcDelegate? _keyboardHookProc;
    private static IntPtr _keyboardHook;

    private const int WH_KEYBOARD_LL = 13;
    private const int VK_F11 = 0x7A;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_QUICK_EDIT = 0x0040;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const int GWL_STYLE = -16;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_SYSMENU = 0x00080000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint WM_SETICON = 0x0080;
    private const uint WM_CLOSE = 0x0010;
    private const IntPtr ICON_SMALL = (IntPtr)0;
    private const IntPtr ICON_BIG = (IntPtr)1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    const int SW_RESTORE = 9;

    // Только Windows 11 (build 22000+) — на более старых системах DwmSetWindowAttribute вернёт
    // ошибку по неизвестному атрибуту, что безвредно (см. try/catch в SetRoundedCorners).
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // DWMWA_CLOAK — недокументированный, но давно известный атрибут: убирает окно с композиции
    // DWM НАПРЯМУЮ, независимо от его "положения" (свёрнуто/обычное). Это позволяет расцепить
    // "перевести окно в обычное положение, чтобы resize сработал корректно" (SW_RESTORE) от
    // "показать окно пользователю" — окно можно развернуть, оставаясь физически невидимым,
    // применить SetConsoleConfig, и только потом снять cloak.
    private const int DWMWA_CLOAK = 13;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CONSOLE_FONT_INFOEX
    {
        public uint cbSize;
        public uint nFont;
        public short FontSizeX;
        public short FontSizeY;
        public uint FontFamily;
        public uint FontWeight;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX; public int ptY; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, ref CONSOLE_FONT_INFOEX info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, ref CONSOLE_FONT_INFOEX info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProcDelegate lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpmsg);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out WinRect rect);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    // BlockConsoleFullscreen выше снимает WS_SYSMENU/WS_CAPTION у окна консоли — у него нет ни
    // кнопки закрытия, ни системного меню. Обычный Environment.Exit(0) завершает процесс, но само
    // окно консоли иногда остаётся висеть — явный WM_CLOSE перед выходом гарантированно его закрывает.
    internal static void CloseConsoleAndExit()
    {
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
            SendMessage(consoleWindow, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Environment.Exit(0);
    }

    // Windows создаёт и сразу показывает окно консоли ДО того, как успевает выполниться хоть
    // одна строчка Main() — со стандартным размером/чёрным фоном. Прячем окно первой же строкой
    // и показываем только после того, как всё (стиль без рамки, размер под карту, цвета, шрифт)
    // уже настроено — иначе пользователь видит мигание "обычная консоль → игра".
    internal static void HideWindow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
            ShowWindow(consoleWindow, SW_HIDE);
    }

    internal static void ShowWindowReady()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != IntPtr.Zero)
            ShowWindow(consoleWindow, SW_SHOW);
    }

    internal static void SetConsoleConfig(WorldState settings)
    {
        Console.Title = "DnD Движок";
        Console.OutputEncoding = Encoding.UTF8;

        var display = new DisplayConfig();
        // рамка(2) + предохранитель от автопереноса справа(1) + DisplayConfig.LeftMargin слева.
        int width = display.InnerWidth(display.ViewCols(settings.Map)) + 3 + DisplayConfig.LeftMargin;
        // Карточка персонажа/способностей растягивается минимум до FixedMapContentHigh
        // (см. DisplayConfig.MaxHigh) — окно должно быть высоким настолько же, иначе
        // диалоговому окну и блоку картинки под карточкой не остаётся места.
        int cardHigh = Math.Max(display.ViewRows(settings.Map) * 2, DisplayConfig.FixedMapContentHigh);
        int height = cardHigh + display.MaxHistoryLines + 18;

        // EnableAnsiSupport снимает WS_CAPTION ДО этого места, но conhost пересчитывает пиксели под
        // N строк так, будто заголовок никуда не делся — внизу окна остаётся пустая полоса. Буфер
        // выше окна создавал скролл (недопустимо), а пиксельная подрезка через SetWindowPos задевала
        // ширину. -2 строки обрезали реальный контент (пропала верхняя строка) — откат на -1.
        int windowHeight = Math.Max(1, height - 1);

        // Фиксированный потолок размера окна — НЕ зависит от разрешения экрана. Раньше эту роль
        // выполнял только Console.LargestWindowWidth/Height (максимум, что помещается на текущий
        // монитор) — из-за этого на мониторе с более высоким разрешением окно становилось выше/шире,
        // чем на старом, хотя карта та же самая. Числа совпадают с Window Size в свойствах ярлыка.
        const int MaxWidth = 150;
        const int MaxHeight = 63;
        if (width > MaxWidth) width = MaxWidth;
        if (windowHeight > MaxHeight) windowHeight = MaxHeight;

        // Шрифт: в окне — 16; весь экран — по масштабу (авто — самый крупный, при котором окно влезает в монитор).
        // Сетка та же (строки/колонки), растёт клетка. Не влезло (ширина символа у шрифта не ровно половина
        // высоты) — на шаг меньше.
        short font = FontSizeFor(width, windowHeight);
        SetConsoleFontSize(font);
        while ((Console.LargestWindowWidth < width || Console.LargestWindowHeight < windowHeight)
               && FontSizes(FontFace).Where(x => x < font).DefaultIfEmpty(0).Max() is var smaller and > 0)
            SetConsoleFontSize(font = (short)smaller);   // не влезло — следующий размер этого шрифта

        // Console.LargestWindowWidth/Height — аварийный барьер ТОЛЬКО на случай, если даже
        // фиксированный потолок выше не помещается на экране (очень маленький монитор) — в обычных
        // условиях срабатывать не должен.
        if (width > Console.LargestWindowWidth)
            width = Console.LargestWindowWidth;
        if (windowHeight > Console.LargestWindowHeight)
            windowHeight = Console.LargestWindowHeight;

        // Ширину НЕ уменьшаем: "+3" в width (запас сверх border.InnerWidth) — не лишнее место,
        // а предохранитель от автопереноса на последней колонке консоли. -1 к ширине съедал этот
        // запас и вызывал перенос строк на самом краю (Windows console переносит текст, записанный
        // ровно в последнюю колонку буфера).
        Console.WindowWidth = width;
        Console.WindowHeight = windowHeight;
        Console.BufferWidth = Console.WindowWidth;
        Console.BufferHeight = Console.WindowHeight;
        TrimWindowToRows();
        NaviDnD.Helpers.FullscreenBackdrop.Recenter();   // весь экран — после смены размера окно снова по центру
    }

    // Окно консоли бывает чуть выше целого числа строк (заголовок снят, а conhost считает пиксели с ним) — под
    // нижней рамкой оставалась полоса в несколько пикселей, которую консоль красила текущим фоном (то вода с
    // карты мира, то другой цвет). Подрезаем высоту окна ровно до строк; ширину не трогаем (её подгонка ломала
    // перенос на последней колонке).
    private static void TrimWindowToRows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var hwnd = GetConsoleWindow();
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var client)) return;
            var font = new CONSOLE_FONT_INFOEX();
            font.cbSize = (uint)Marshal.SizeOf(font);
            if (!GetCurrentConsoleFontEx(GetStdHandle(STD_OUTPUT_HANDLE), false, ref font) || font.FontSizeY <= 0) return;
            int extra = (client.Bottom - client.Top) - Console.WindowHeight * font.FontSizeY;
            if (extra <= 0 || extra >= font.FontSizeY || !GetWindowRect(hwnd, out var win)) return;
            const uint SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, win.Right - win.Left, win.Bottom - win.Top - extra, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { /* не вышло — останется полоса, как раньше */ }
    }

    // Весь экран (настройка ЭКРАН) и размер шрифта в нём: 0 — авто, иначе размер (не влезает — сколько влезает).
    internal static bool Fullscreen;
    // Шрифт консоли (настройка «ТИП ШРИФТА») и какие можно выбрать.
    internal static readonly string[] FontFaces = ["Consolas", "DejaVu Sans Mono"];

    // Шрифты игры (папка Fonts: DejaVu Sans Mono) — подключаются на время работы игры, без установки в систему
    // (AddFontResourceEx без FR_PRIVATE: консоль рисует в другом процессе и должна их видеть), при выходе — снимаются.
    internal static void LoadBundledFonts()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            string dir = Path.Combine(AppConfig.ProjectRoot, "Fonts");
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "*.ttf");
            foreach (var f in files) AddFontResourceEx(f, 0, IntPtr.Zero);
            // Уведомляем о шрифтах без ожидания остальных приложений: таймаут HWND_BROADCAST
            // применяется к каждому окну отдельно и может задержать появление меню на секунды.
            PostMessage((IntPtr)0xffff, 0x001D /* WM_FONTCHANGE */, IntPtr.Zero, IntPtr.Zero);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { foreach (var f in files) RemoveFontResourceEx(f, 0, IntPtr.Zero); };
        }
        catch { /* не подключились — в списке останутся, консоль подставит свой шрифт */ }
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool RemoveFontResourceEx(string file, uint flags, IntPtr reserved);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    internal static string FontFace = "Consolas";
    internal static int FullscreenFontSize;
    private const short BaseFontSize = 16;

    // Размеры шрифта, на которых он смотрится ровно (клетка 1:2): Consolas — 14 и 16, DejaVu Sans Mono — 16–22.
    internal static int[] FontSizes(string face) => face switch
    {
        "Consolas" => [14, 16],
        "DejaVu Sans Mono" => [14, 16, 20, 22],
        _ => [16],
    };

    // В окне — 16; весь экран — выбранный размер шрифта или (авто) самый крупный из его размеров, при котором игра
    // влезает в монитор; не влезает и самый мелкий — самый мелкий.
    private static short FontSizeFor(int cols, int rows)
    {
        if (!Fullscreen) return BaseFontSize;
        var (mw, mh) = NaviDnD.Helpers.FullscreenBackdrop.MonitorSize();
        int fit = Math.Min(mh / Math.Max(1, rows), mw * 2 / Math.Max(1, cols));   // клетка — ровно 1:2
        var sizes = FontSizes(FontFace);
        var fitting = sizes.Where(s => s <= fit && (FullscreenFontSize <= 0 || s <= FullscreenFontSize)).ToList();
        return (short)(fitting.Count > 0 ? fitting.Max() : sizes.Min());
    }

    private static void SetConsoleFontSize(short size)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
        var info = new CONSOLE_FONT_INFOEX();
        info.cbSize = (uint)Marshal.SizeOf(info);
        GetCurrentConsoleFontEx(handle, false, ref info);
        // Ширина клетки — ровно половина высоты: у Consolas сама по себе она 1:2 только до 16 (24 → 11×24), и полублоки
        // карты, браиль, рамки на крупном шрифте сплющивались; явная ширина консолью соблюдается (24 → 12×24).
        info.FontSizeX = (short)(size / 2);
        info.FontSizeY = size;
        info.FaceName = FontFace;
        SetCurrentConsoleFontEx(handle, false, ref info);
    }

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // F11 (полноэкранный режим консоли ломает раскладку) глушим только в окне игры — не в других программах.
        if (nCode >= 0 && Marshal.ReadInt32(lParam) == VK_F11 && GetForegroundWindow() == GetConsoleWindow())
        {
            if (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0104) _ = Task.Run(Helpers.Speech.Stop);
            return (IntPtr)1;
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    internal static void BlockConsoleFullscreen()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        _keyboardHookProc = KeyboardHookCallback;
        var thread = new Thread(() =>
        {
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, GetModuleHandle(null), 0);
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            if (_keyboardHook != IntPtr.Zero)
                UnhookWindowsHookEx(_keyboardHook);
        });
        thread.IsBackground = true;
        thread.Start();
    }

    internal static void EnableAnsiSupport()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (GetConsoleMode(handle, out uint mode))
        {
            mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            SetConsoleMode(handle, mode);
        }

        var consoleWindow = GetConsoleWindow();
        int style = GetWindowLong(consoleWindow, GWL_STYLE);
        style &= ~(WS_THICKFRAME | WS_MAXIMIZEBOX | WS_CAPTION | WS_SYSMENU);
        SetWindowLong(consoleWindow, GWL_STYLE, style);
        // Стиль не применится визуально без SWP_FRAMECHANGED — окно не перерисует
        // неклиентскую область (заголовок/рамку) само по себе после SetWindowLong.
        SetWindowPos(consoleWindow, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

        SetConsoleIcon();
        SetRoundedCorners(consoleWindow);
    }

    // DWMWA_WINDOW_CORNER_PREFERENCE — доступен только начиная с Windows 11 (build 22000). На более
    // старых системах DwmSetWindowAttribute вернёт ошибку по неизвестному атрибуту — безвредно,
    // просто не скруглит углы.
    private static void SetRoundedCorners(IntPtr consoleWindow)
    {
        int preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(consoleWindow, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    internal static void DisableQuickEdit()
    {
        IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);
        GetConsoleMode(handle, out uint mode);
        mode &= ~ENABLE_QUICK_EDIT;
        SetConsoleMode(handle, mode);
    }

    internal static void SetConsoleIcon()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        IntPtr hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero) return;

        IntPtr hIcon = LoadImage(IntPtr.Zero, "dnd.ico", IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
        if (hIcon != IntPtr.Zero)
        {
            SendMessage(hWnd, WM_SETICON, ICON_SMALL, hIcon);
            SendMessage(hWnd, WM_SETICON, ICON_BIG, hIcon);
        }
        else
        {
            Console.WriteLine("Не удалось загрузить иконку. Проверьте путь и формат (требуется .ico).");
        }
    }


    internal static void ShowWindow()
    {
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero) return;

        //// Cloak ДО восстановления положения — сам переход "свёрнуто → обычное" остаётся физически
        //// невидимым, несмотря на то что технически окно уже в обычном положении и видимо для Windows.
        //int cloak = 1;
        //DwmSetWindowAttribute(hWnd, DWMWA_CLOAK, ref cloak, sizeof(int));
        ShowWindow(hWnd, SW_RESTORE);
    }

    // Снимает cloak, поставленный в ShowWindow() — вызывать ПОСЛЕ SetConsoleConfig(), когда
    // resize уже применён к окну в обычном положении и всё готово к показу пользователю.
    internal static void RevealWindow()
    {
        IntPtr hWnd = GetConsoleWindow();
        if (hWnd == IntPtr.Zero) return;

        int uncloak = 0;
        DwmSetWindowAttribute(hWnd, DWMWA_CLOAK, ref uncloak, sizeof(int));
    }
}
