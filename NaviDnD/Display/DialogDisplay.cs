using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using NaviDnD.Helpers;
using System.Text.Json.Nodes;

namespace NaviDnD;

public class DialogDisplay
{
    private class DisplayLine
    {
        public string Prefix { get; set; }
        public List<int> PrefixColor { get; set; }
        public string Text { get; set; }
        public bool IsSystem { get; set; }
        public bool Highlight { get; set; }           // not a system line - highlighted (DialogHighlighter)
        public List<int>? BaseColor { get; set; }     // author's shade for regular text (hero/NPC); DM has null
        public int BracketDepthAtStart { get; set; }  // is [...] opened from the previous line of the same message
    }

    private DialogHighlighter? _highlighter;
    private string? _highlighterKey;

    // Names from the map and colors as on the map; The highlighter is rebuilt only when the set of names changes.
    private void RefreshHighlighter()
    {
        var names = (_setting.Map.Entities ?? []).Where(e => e.Deleted != true).Cast<CellEntity>()
            .Concat((_setting.Map.Objects ?? []).Where(o => o.Deleted != true))
            .Select(e => (name: e.Name, color: ColorHelper.Saturate(e.Color)))
            .Append((name: _setting.Hero?.Name ?? "", color: _setting.Hero?.Color ?? _display.MainForeground))
            .Where(n => !string.IsNullOrWhiteSpace(n.name) && n.color is { Count: 3 })
            .ToList();

        // Symbols only for creatures (for objects it is “(F)”/“[C]” - they are not written in the text).
        var symbols = (_setting.Map.Entities ?? []).Where(e => e.Deleted != true)
            .Select(e => (symbol: e.Symbol, color: ColorHelper.Saturate(e.Color)))
            .Append((symbol: _setting.Hero?.Symbol ?? "", color: _setting.Hero?.Color ?? _display.MainForeground))
            .Where(s => !string.IsNullOrWhiteSpace(s.symbol) && s.color is { Count: 3 })
            .ToList();

        string key = string.Join("|", names.Concat(symbols).Select(n => $"{n.Item1}:{string.Join(",", n.Item2)}"));
        if (key == _highlighterKey && _highlighter != null) return;

        _highlighterKey = key;
        _highlighter = new DialogHighlighter(names, new DialogHighlighter.Palette(
            Mechanics: ColorHelper.Darker(_display.MainForeground, 0.6),
            Bad: [225, 95, 85],
            Good: [120, 205, 120]), symbols);
    }

    private readonly WorldState _setting;
    private readonly DisplayConfig _display;
    private readonly Storage _storage;

    // Cache for dialog rerender on scroll
    private List<DisplayLine> _cachedAllLines;
    private List<int> _cachedMessageSizes;
    private List<(int, int)> _cachedPages;
    private int _cachedMaxPage;
    private int _dialogBlockStartTop; // Start of title line
    private BorderDrawer _cachedBorderDrawer;
    private bool _dialogContentDrawn;
    private readonly ConsoleRegionBuffer _textRegion = new();
    private readonly ConsoleRegionBuffer _imageRegion = new();
    private Action? _footerDraw;

    // Animation state
    private string[]? _animationRightLines;
    private int _outcomeRow = -1;
    private List<int>? _outcomeColor;
    private int _modHighlightRow = -1;
    private List<int>? _modHighlightColor;
    private int _diceHighlightStart = -1;
    private int _diceHighlightEnd = -1;
    private List<int>? _diceHighlightColor;
    private int _diceHighlightOccurrence; // with a pair of dice - index (0/1) of the highlighted number

    // Arrow hover state for visual highlight
    private bool _upArrowHovered;
    private bool _downArrowHovered;
    private bool _noteLeftArrowHovered;
    private bool _noteRightArrowHovered;

    // Streaming state flag
    private bool _isStreaming = false;
    private bool _animationInProgress = false;

    // Streaming state
    private readonly List<DialogMessage> _streamingMessages = [];
    private DialogMessage? _streamingMessage;
    private int _charsSinceRerender;
    private const int RerenderEvery = 1;
    public AnimationLoop Animations { get; } = new();
    private IDisposable? _spinnerAnimation;
    private int _spinnerFrame = 0;
    private bool _spinnerFooterVisible;
    private const int SpinnerIntervalMs = 100;

    public DialogDisplay(
        WorldState setting,
        DisplayConfig display,
        Storage storage
    )
    {
        _setting = setting;
        _display = display;
        _storage = storage;
    }

    public string Draw()
    {
        _dialogContentDrawn = false;
        var borderDrawer = new BorderDrawer(_setting, _display);
        _cachedBorderDrawer = borderDrawer;

        _dialogBlockStartTop = Term.CursorTop;

        RebuildCache();
        RenderDialogBlock();

        if (HasSplit)
            borderDrawer.DrawSeparatorWith2Parts('┴', LeftWidth, RightPanelWidth);
        else
            borderDrawer.DrawSeparator();
        Term.CursorVisible = true;

        var inputBox = new InputBox(_setting, _display);
        var input = inputBox.GetInput();

        while (input == "Up" || input == "Down")
        {
            if (input == "Up" && TryScrollUp()) Sound.PlayClick();
            else if (input == "Down" && TryScrollDown()) Sound.PlayClick();
            input = inputBox.GetInput();
        }

        if (string.IsNullOrWhiteSpace(input))   // empty input (and only spaces) is not sent to the GM
            return null;

        return input;
    }

    // Resets streaming state and immediately redraws the bottom area with a spinner.
    public void PrepareStreaming()
    {
        _streamingMessage = null;
        _streamingMessages.Clear();
        _charsSinceRerender = 0;
        _spinnerFrame = 0;
        Term.CursorVisible = false;
        _isStreaming = true;
        _spinnerAnimation?.Dispose();
        _spinnerAnimation = Animations.Start([new(TimeSpan.FromMilliseconds(SpinnerIntervalMs), () =>
        {
            _spinnerFrame++;
            RedrawStreamingFooter();
        })], repeat: true);
        _display.MapHoverEnabled = true;
        _display.StreamingTabTitle = null;

        if (_cachedBorderDrawer == null)
        {
            _cachedBorderDrawer = new BorderDrawer(_setting, _display);
            _dialogBlockStartTop = Term.CursorTop;
        }
        RebuildCache();
        RerenderDialogBlock(DrawSpinner);
    }

    // Called when the stream detects a new history entry with the given author.
    // Does NOT rerender — AppendStreamChunk will trigger the first rerender once text arrives.
    public void NewStreamingMessage(string author)
    {
        _display.DialogScrollOffset = 0; // jump to last page before text starts printing
        _streamingMessage = new DialogMessage { Author = author, Text = "" };
        _streamingMessages.Add(_streamingMessage);
        _charsSinceRerender = 0;
    }

    // Appends a chunk to the current streaming placeholder and rerenders.
    public void AppendStreamChunk(string text)
    {
        if (_streamingMessage == null)
            NewStreamingMessage("DM");

        _streamingMessage!.Text += text;
        _charsSinceRerender += text.Length;

        if (_charsSinceRerender >= RerenderEvery)
        {
            _charsSinceRerender = 0;
            PollDuringStreaming();
            RebuildCache();
            RerenderDialogBlock(DrawSpinner);
        }
    }

    public void TickSpinner()
    {
        if (_cachedBorderDrawer != null)
        {
            PollDuringStreaming();
            Animations.Tick();
        }
    }

    // Fast mouse poll without dialog rerender — called at ~16ms intervals between spinner ticks.
    public void PollOnly()
    {
        if (_cachedBorderDrawer == null) return;
        PollDuringStreaming();
        Animations.Tick();
    }

    private void PollAndHandleTabSwitch()
    {
        _display.PollAction?.Invoke();
        if (_display.PendingCommand != null)
        {
            var cmd = _display.PendingCommand;
            _display.PendingCommand = null;
            SwitchTab(cmd);
        }
    }

    // Waits for the given duration while polling mouse/tabs at ~16ms intervals.
    private async Task PollingDelay(int ms)
    {
        await Animations.DelayAsync(TimeSpan.FromMilliseconds(ms), PollDuringStreaming);
    }

    // Like PollingDelay, but up to an ABSOLUTE point in time, and not for a fixed duration - the time of the
    // render itself (RebuildCache/RerenderDialogBlock) between steps is NOT free, and with fixed delays for
    // each step, the amount of actually elapsed time accumulates and outgrows the budget. Counting from one
    // common reference point, each next step itself “shortens” its pause by the time that the render has
    // already consumed - the amount is not runs away from the budget.
    private async Task DelayUntil(DateTime target)
    {
        int ms = (int)Math.Max(0, (target - DateTime.UtcNow).TotalMilliseconds);
        if (ms > 0) await PollingDelay(ms);
        else { _display.PollAction?.Invoke(); if (_display.PendingCommand != null) { var cmd = _display.PendingCommand; _display.PendingCommand = null; SwitchTab(cmd); } }
    }

    // Removes all streaming placeholders (real messages come from ApplyUpdateWorldState).
    public void FinalizeStreaming()
    {
        _spinnerAnimation?.Dispose();
        _spinnerAnimation = null;
        _isStreaming = false;
        _animationInProgress = false;
        _display.MapHoverEnabled = true;
        _display.StreamingTabTitle = null;
        _streamingMessages.Clear();
        _streamingMessage = null;
        Term.CursorVisible = true;
    }

    private int InnerWidth => _display.InnerWidth(_display.ViewCols(_setting.Map));
    private int RightPanelWidth => _display.DialogRightPanelWidth;
    private bool HasSplit => RightPanelWidth > 0;
    private int LeftWidth => InnerWidth - 1 - RightPanelWidth;
    private int DialogTextWidth => HasSplit ? LeftWidth : InnerWidth;

    private void RebuildCache()
    {
        var allLines = new List<DisplayLine>();
        var messageSizes = new List<int>();
        // -1: left indent before line text (Console.Write(" ") in DrawDialogLine).
        int innerWidth = DialogTextWidth - 1;

        var allMessages = _setting.History?.Concat(_streamingMessages) ?? _streamingMessages;

        // New message (including without AI - “The path is blocked” when stepping into a wall) → last page,
        // otherwise, after scrolling up the dialogue, the player did not see the text that appeared.
        int messageCount = (_setting.History?.Count ?? 0) + _streamingMessages.Count;
        if (messageCount > _display.DialogSeenMessageCount && _display.DialogSeenMessageCount >= 0)
            _display.DialogScrollOffset = 0;
        _display.DialogSeenMessageCount = messageCount;
        foreach (var entry in allMessages)
        {
            string author = string.IsNullOrEmpty(entry.Author) ? "DM" : entry.Author;
            string text = entry.Text ?? "";

            List<int> authorColor = null;
            bool isSystem = author.Equals("System", StringComparison.OrdinalIgnoreCase);

            if (!isSystem)
            {
                if (author.Equals(_setting.Hero?.Name, StringComparison.OrdinalIgnoreCase))
                    authorColor = _setting.Hero?.Color;
                else
                    authorColor = _storage.GetColorByName(author) ?? _display.SystemCommandHistory;
            }

            string prefix = isSystem ? null : author + ": ";
            int prefixLength = prefix?.Length ?? 0;

            var wrappedLines = TextWrapper.WrapText(text, innerWidth, prefixLength > 0 ? innerWidth - prefixLength : null);
            if (wrappedLines.Count == 0)
            {
                if (isSystem) continue;
                wrappedLines = [""]; // show "Author: " line even for empty streaming placeholder
            }

            int messageLineCount = wrappedLines.Count;
            bool highlight = !isSystem;
            // Hero's remarks/NPC - regular text with a tint of the author's color: you can see whose line is
            // whose. DM is the main voice, his text remains the default color.
            List<int>? baseColor = !isSystem && authorColor != null && !author.Equals("DM", StringComparison.OrdinalIgnoreCase)
                ? ColorHelper.MixWith(_display.MainForeground, ColorHelper.Saturate(authorColor), 0.35)
                : null;
            int depth = 0;
            for (int i = 0; i < wrappedLines.Count; i++)
            {
                allLines.Add(new DisplayLine
                {
                    Prefix = i == 0 ? prefix : null,
                    PrefixColor = i == 0 && !isSystem ? authorColor : null,
                    Text = wrappedLines[i],
                    IsSystem = isSystem,
                    Highlight = highlight,
                    BaseColor = baseColor,
                    BracketDepthAtStart = depth
                });
                foreach (char c in wrappedLines[i])
                {
                    if (c == '[') depth++;
                    else if (c == ']' && depth > 0) depth--;
                }
            }

            messageSizes.Add(messageLineCount);
        }

        _cachedAllLines = allLines;
        _cachedMessageSizes = messageSizes;
        RefreshHighlighter();
        var pages = CalculatePages(messageSizes, _display.MaxHistoryLines);
        _cachedPages = pages;
        // Scrolling - by lines (DialogScrollOffset - lines from the end of the story): wheel - 2 lines, arrows
        // ▲ ▼ - by page. Window - last MaxHistoryLines lines before offset.
        _cachedMaxPage = Math.Max(0, allLines.Count - _display.MaxHistoryLines);
    }

    private static string TitleBase => L.T("DIALOG ");

    // The text “current/total” between the arrows is one place so that the formula here and in ArrowPositions
    // (positions ▲/▼ depend on the length of this text) do not move apart.
    private string DialogPageText()
    {
        int page = Math.Max(1, _display.MaxHistoryLines);
        int total = Math.Max(1, (int)Math.Ceiling((_cachedAllLines?.Count ?? 0) / (double)page));
        int fromEnd = Math.Min(_display.DialogScrollOffset, _cachedMaxPage);
        int current = Math.Clamp(total - (int)Math.Round(fromEnd / (double)page), 1, total);
        return $"{current}/{total}";
    }

    private void RenderDialogBlock()
    {
        int fromEnd = _display.DialogScrollOffset = Math.Clamp(_display.DialogScrollOffset, 0, _cachedMaxPage);
        int lineStart = Math.Max(0, _cachedAllLines.Count - _display.MaxHistoryLines - fromEnd);
        int lineCount = Math.Min(_display.MaxHistoryLines, _cachedAllLines.Count - lineStart);
        var visibleLines = _cachedAllLines.GetRange(lineStart, lineCount);
        bool hasAbove = fromEnd < _cachedMaxPage && !_isStreaming;
        bool hasBelow = fromEnd > 0 && !_isStreaming;

        var mainFg = _display.MainForeground;
        var arrowHighlight = ColorHelper.Pale(mainFg, 1);
        var arrowInactive = ColorHelper.Darker(mainFg, 0.5);
        bool inputDisabled = _display.InputDisabled;
        var upColor   = inputDisabled || !hasAbove ? arrowInactive : (_upArrowHovered   ? arrowHighlight : mainFg);
        var downColor = inputDisabled || !hasBelow ? arrowInactive : (_downArrowHovered ? arrowHighlight : mainFg);

        string pageText = DialogPageText();
        int titleContentLength = TitleBase.Length + 4 + pageText.Length;
        _cachedBorderDrawer.DrawTitleLine(() =>
        {
            Console.Write(TitleBase);
            ColorHelper.WriteColored("▲", upColor);
            Console.Write(" ");
            Console.Write(pageText);
            Console.Write(" ");
            ColorHelper.WriteColored("▼", downColor);
        }, titleContentLength);

        if (HasSplit)
            _cachedBorderDrawer.DrawSeparatorWith2Parts('┬', LeftWidth, RightPanelWidth);
        else
            _cachedBorderDrawer.DrawSeparator();

        var rightLines = BuildRightPanelLines();
        if (!_dialogContentDrawn)
        {
            // Establish borders once. Subsequent frames only touch the changed panel interiors.
            for (int row = 0; row < _display.MaxHistoryLines; row++)
            {
                if (row < visibleLines.Count) DrawDialogLine(visibleLines[row], rightLines[row]);
                else DrawEmptyDialogLine(rightLines[row]);
            }
            _textRegion.Invalidate();
            _imageRegion.Invalidate();
        }
        var textRows = Enumerable.Range(0, _display.MaxHistoryLines)
            .Select(row => CapturePanelContent(() =>
            {
                if (row < visibleLines.Count) DrawDialogText(visibleLines[row]);
            })).ToArray();
        string patch = _textRegion.Update(DisplayConfig.LeftMargin + 1, _dialogBlockStartTop + 2,
            DialogTextWidth, textRows, BackgroundAnsi);
        if (HasSplit) patch += BuildImagePatch(rightLines);
        if (_dialogContentDrawn && patch.Length > 0) Console.Write(AnsiColorRuns.Compact(patch));
        _dialogContentDrawn = true;
        ColorHelper.SetBackgroundColor(_display.MainBackground);
        ColorHelper.SetForegroundColor(_display.MainForeground);
        Term.SetCursorPosition(0, _dialogBlockStartTop + 2 + _display.MaxHistoryLines);
    }

    private string BackgroundAnsi => $"\x1b[48;2;{_display.MainBackground[0]};{_display.MainBackground[1]};{_display.MainBackground[2]}m";

    // Panel callbacks only write text/colors; capture does not query or move the console cursor.
    private string CapturePanelContent(Action draw)
    {
        var output = Console.Out;
        using var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            ColorHelper.SetBackgroundColor(_display.MainBackground);
            ColorHelper.SetForegroundColor(_display.MainForeground);
            draw();
            return buffer.ToString();
        }
        finally { Console.SetOut(output); }
    }

    private string BuildImagePatch(Action[] rows) => _imageRegion.Update(
        DisplayConfig.LeftMargin + 2 + LeftWidth, _dialogBlockStartTop + 2, RightPanelWidth,
        rows.Select(CapturePanelContent).ToArray(), BackgroundAnsi);


    private Action[] BuildRightPanelLines()
    {
        var lines = new Action[_display.MaxHistoryLines];
        for (int i = 0; i < lines.Length; i++) lines[i] = static () => { };

        if (!HasSplit) return lines;

        string[] contentLines;
        bool isAnimation;
        if (_animationInProgress && _animationRightLines != null)
        {
            contentLines = _animationRightLines;
            isAnimation = true;
        }
        else if (_display.SelectedImageLines is { Length: > 0 })
        {
            _animationRightLines = null;
            _outcomeRow = -1;
            _outcomeColor = null;
            _modHighlightRow = -1;
            _modHighlightColor = null;
            _diceHighlightStart = -1;
            _diceHighlightEnd = -1;
            _diceHighlightColor = null;
            contentLines = _display.SelectedImageLines;
            isAnimation = false;
        }
        else if (_animationRightLines != null)
        {
            contentLines = _animationRightLines;
            isAnimation = true;
        }
        else
        {
            contentLines = GetDefaultRightPanelLines();
            isAnimation = false;
        }
        bool isHoverImage = _display.SelectedImageLines is { Length: > 0 };
        var color = !isAnimation ? (isHoverImage ? _display.SelectedImageColor : _display.DefaultImageColor) : null;
        bool colorTitleOnly = isHoverImage && _display.SelectedImageColorTitleOnly;
        var segments = !isAnimation && isHoverImage && ReferenceEquals(_display.SelectedImageSegmentsFor, _display.SelectedImageLines)
            ? _display.SelectedImageSegments : null;

        int vertOffset = Math.Max(0, (_display.MaxHistoryLines - contentLines.Length) / 2);
        int count = Math.Min(contentLines.Length, _display.MaxHistoryLines - vertOffset);
        var bg = _display.MainBackground;

        for (int i = 0; i < count; i++)
        {
            string text = contentLines[i] ?? "";
            if (text.Length > RightPanelWidth) text = text[..RightPanelWidth];
            int leftPad = Math.Max(0, (RightPanelWidth - text.Length) / 2);
            string paddedText = leftPad > 0 ? new string(' ', leftPad) + text : text;

            int absRow = vertOffset + i;
            bool isIndicatorRow = !isAnimation && i == contentLines.Length - 1
                && text.Length >= 2 && text[0] == '◄' && text[^1] == '►';
            if (isIndicatorRow)
            {
                var mainFg = _display.MainForeground;
                var arrowHighlight = ColorHelper.Pale(mainFg, 1);
                var arrowInactive = ColorHelper.Darker(mainFg, 0.5);
                bool canGoLeft  = _display.NotePage > 0;
                bool canGoRight = _display.NotePage < _display.NotePageCount - 1;
                var leftColor  = _display.InputDisabled || !canGoLeft  ? arrowInactive : (_noteLeftArrowHovered  ? arrowHighlight : mainFg);
                var rightColor = _display.InputDisabled || !canGoRight ? arrowInactive : (_noteRightArrowHovered ? arrowHighlight : mainFg);
                string middle = text[1..^1];
                int capturedLeftPad = leftPad;
                lines[absRow] = () =>
                {
                    if (capturedLeftPad > 0) Console.Write(new string(' ', capturedLeftPad));
                    ColorHelper.WriteColored(text[..1], leftColor, bg);
                    ColorHelper.WriteColored(middle, mainFg, bg);
                    ColorHelper.WriteColored(text[^1..], rightColor, bg);
                };
            }
            else if (isAnimation && _outcomeColor != null && absRow == _outcomeRow)
            {
                var c = _outcomeColor;
                lines[absRow] = () => ColorHelper.WriteColored(paddedText, c, bg);
            }
            else if (isAnimation && _modHighlightColor != null && absRow == _modHighlightRow)
            {
                var c = _modHighlightColor;
                lines[absRow] = () => ColorHelper.WriteColored(paddedText, c, bg);
            }
            else if (isAnimation && _diceHighlightColor != null
                && absRow >= _diceHighlightStart && absRow < _diceHighlightEnd
                && TryFindDigitRun(text, _diceHighlightOccurrence, out int digitStart, out int digitLen))
            {
                var mainFg = _display.MainForeground;
                var hi = _diceHighlightColor;
                string before = text[..digitStart];
                string digits = text.Substring(digitStart, digitLen);
                string after = text[(digitStart + digitLen)..];
                int capturedLeftPad = leftPad;
                lines[absRow] = () =>
                {
                    if (capturedLeftPad > 0) Console.Write(new string(' ', capturedLeftPad));
                    ColorHelper.WriteColored(before, mainFg, bg);
                    ColorHelper.WriteColored(digits, hi, bg);
                    ColorHelper.WriteColored(after, mainFg, bg);
                };
            }
            else if (!isAnimation && PixelArt.Colors(text) != null)
            {
                // Color sprite (Dungeon Crawl tile) - in its own colors, not in the color of the creature.
                int capturedLeftPad = leftPad;
                lines[absRow] = () =>
                {
                    if (capturedLeftPad > 0) Console.Write(new string(' ', capturedLeftPad));
                    PixelArt.Write(text, bg);
                };
            }
            else if (segments != null && i < segments.Length && segments[i] is { } lineSegs)
            {
                int capturedLeftPad = leftPad;
                var mainFg = _display.MainForeground;
                lines[absRow] = () =>
                {
                    if (capturedLeftPad > 0) Console.Write(new string(' ', capturedLeftPad));
                    foreach (var (segText, segColor) in lineSegs)
                        ColorHelper.WriteColored(segText, segColor ?? mainFg, bg);
                };
            }
            else if (color != null && (!colorTitleOnly || i == 0))
            {
                var captureColor = color;
                lines[absRow] = () => ColorHelper.WriteColored(paddedText, captureColor, bg);
            }
            else
            {
                lines[absRow] = () => Console.Write(paddedText);
            }
        }

        return lines;
    }

    private string[] GetDefaultRightPanelLines()
    {
        if (_display.SelectedImageLines is { Length: > 0 })
            return _display.SelectedImageLines;
        if (_display.DefaultImageLines is { Length: > 0 })
            return _display.DefaultImageLines;
        return DiceArt.D20.Split('\n');
    }

    public void RerenderRightPanel()
    {
        if (!HasSplit || _cachedBorderDrawer == null || !_dialogContentDrawn || _animationInProgress) return;
        int left = Term.CursorLeft, top = Term.CursorTop;
        bool visible = Term.CursorVisible;
        try
        {
            string patch = BuildImagePatch(BuildRightPanelLines());
            if (patch.Length == 0) return;
            Term.CursorVisible = false;
            Console.Write(AnsiColorRuns.Compact(patch));
        }
        finally
        {
            ColorHelper.SetBackgroundColor(_display.MainBackground);
            ColorHelper.SetForegroundColor(_display.MainForeground);
            Term.SetCursorPosition(left, top);
            Term.CursorVisible = visible;
        }
    }

    /// Screen positions of ▲ and ▼ in the title row, and whether each is currently active.
    public (int upX, int downX, int titleY, bool canScrollUp, bool canScrollDown) ArrowPositions
    {
        get
        {
            int innerWidth = _display.InnerWidth(_display.ViewCols(_setting.Map));
            string pageText = DialogPageText();
            int titleContentLength = TitleBase.Length + 4 + pageText.Length;
            int padding = (innerWidth - titleContentLength) / 2;
            // ▲ right after TitleBase, ▼ after "▲ {pageText} "
            int titleStart = DisplayConfig.LeftMargin + 1;
            int upX   = titleStart + padding + TitleBase.Length;
            int downX = titleStart + padding + TitleBase.Length + 3 + pageText.Length;
            bool canUp   = _cachedAllLines != null && _display.DialogScrollOffset < _cachedMaxPage && !_isStreaming;
            bool canDown = _cachedAllLines != null && _display.DialogScrollOffset > 0 && !_isStreaming;
            return (upX, downX, _dialogBlockStartTop, canUp, canDown);
        }
    }

    // The text of the note page indicator is one place so that the formula here and in NotePageArrowPositions
    // (centralization ◄/► by line) do not diverge.
    internal static string NotePageIndicatorText(int page, int count) => $"◄ {page + 1}/{count} ►";

    /// Screen positions of ◄ and ► for note pagination in the right (image) panel — only valid when
    /// the selected inventory item has readable text spanning more than one page. (-1,-1,-1) otherwise.
    /// Formula mirrors BuildRightPanelLines' own centering (vertOffset/leftPad) and BorderDrawer.
    /// DrawContentLine2Columns' write order (margin + border + LeftWidth + divider = right column start).
    public (int leftX, int rightX, int arrowY) NotePageArrowPositions
    {
        get
        {
            if (!HasSplit || _display.NotePageCount <= 1 || _display.SelectedImageLines is not { Length: > 0 } lines)
                return (-1, -1, -1);

            string indicator = NotePageIndicatorText(_display.NotePage, _display.NotePageCount);
            int vertOffset = Math.Max(0, (_display.MaxHistoryLines - lines.Length) / 2);
            int arrowRow = _dialogBlockStartTop + 2 + vertOffset + (lines.Length - 1);

            int rightPanelStartX = DisplayConfig.LeftMargin + 2 + LeftWidth;
            int leftPad = Math.Max(0, (RightPanelWidth - indicator.Length) / 2);
            int leftArrowX = rightPanelStartX + leftPad;
            int rightArrowX = rightPanelStartX + leftPad + indicator.Length - 1;
            return (leftArrowX, rightArrowX, arrowRow);
        }
    }

    public void SetArrowHover(bool upHovered, bool downHovered)
    {
        if (_upArrowHovered == upHovered && _downArrowHovered == downHovered) return;
        _upArrowHovered = upHovered;
        _downArrowHovered = downHovered;
        if (_cachedBorderDrawer != null && _cachedAllLines != null)
            RerenderDialogBlock();
    }

    public void SetNoteArrowHover(bool leftHovered, bool rightHovered)
    {
        if (_noteLeftArrowHovered == leftHovered && _noteRightArrowHovered == rightHovered) return;
        _noteLeftArrowHovered = leftHovered;
        _noteRightArrowHovered = rightHovered;
        if (_cachedBorderDrawer != null && _cachedAllLines != null)
            RerenderDialogBlock();
    }

    // Arrows ▲ ▼ and keys - to page; wheel - ScrollLines by lines.
    public bool TryScrollUp() => ScrollLines(_display.MaxHistoryLines);
    public bool TryScrollDown() => ScrollLines(-_display.MaxHistoryLines);

    // Shift by lines rows: > 0 - to earlier ones, < 0 - to new ones. false - there is nowhere to move.
    public bool ScrollLines(int lines)
    {
        if (_isStreaming || _cachedAllLines == null) return false;
        int next = Math.Clamp(_display.DialogScrollOffset + lines, 0, _cachedMaxPage);
        if (next == _display.DialogScrollOffset) return false;
        _display.DialogScrollOffset = next;
        _upArrowHovered = false;
        _downArrowHovered = false;
        RerenderDialogBlock();
        return true;
    }

    private async Task StreamHistoryEntries(IEnumerable<RequestHistoryEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Patch.HasValue && entry.Patch.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var patchNode = JsonNode.Parse(entry.Patch.Value.GetRawText())?.AsObject();
                if (patchNode != null)
                {
                    await AnimateMovePathsInPatch(patchNode);
                    _storage.ApplyUpdateWorldState(patchNode.ToJsonString());
                    // A streamed patch updates the visible tab; drawing the map alone leaves its old mouse handler active.
                    _display.RedrawCurrentContent?.Invoke();
                }
            }

            if (string.IsNullOrEmpty(entry.Text)) continue;
            entry.Text = entry.Text.Replace("\r", "").Replace("\n", "");
            if (entry.Text.Length == 0) continue;
            string? speechText = Speech.ValidateSsml(entry.Text);
            entry.Text = Speech.DisplayText(entry.Text);
            using var speechMessage = Speech.BeginMessage(entry.Text, entry.Author, speechText);

            _setting.History ??= [];
            _setting.History.Add(new DialogMessage { Author = entry.Author ?? "DM", Text = "", SpeechText = speechText });
            var msg = _setting.History[^1];
            _display.DialogScrollOffset = 0;

            bool skipText = false;
            await Animations.PlayAsync(entry.Text.Select(c => new AnimationLoop.Frame(
                TimeSpan.FromMilliseconds(40), () =>
                {
                    if (skipText) return;
                    msg.Text += c;
                    Sound.PlayTyping(c);
                    RebuildCache();
                    RerenderDialogBlock(DrawSpinner);
                })), () =>
                {
                    PollAndHandleTabSwitch();
                    var key = ConsoleMouseReader.TryReadKeyDown();
                    if (key is ConsoleKey.Enter or ConsoleKey.Spacebar) skipText = true;
                    else if (key.HasValue && key.Value >= ConsoleKey.F1 && key.Value <= ConsoleKey.F10)
                        SwitchTab(key.Value.ToString());
                }, stop: () => skipText, catchUp: false);
            speechMessage.Dispose();
            msg.Text = entry.Text;
            RebuildCache();
            RerenderDialogBlock(DrawSpinner);
            await PollingDelay(250);
        }
    }

    private async Task AnimateMovePathsInPatch(JsonObject patch)
    {
        if (patch["map"]?["entities"] is not JsonArray entities) return;
        foreach (var entityNode in entities.OfType<JsonObject>())
        {
            if (entityNode["movePath"] is JsonArray movePath
                && entityNode["id"] is JsonValue idVal && idVal.TryGetValue<int>(out int id)
                && _setting.Map.Entities != null && id >= 0 && id < _setting.Map.Entities.Count)
            {
                var entity = _setting.Map.Entities[id];
                foreach (var stepNode in movePath)
                {
                    if (stepNode is not JsonArray step) continue;
                    var pos = step.Select(n => n?.GetValue<int>() ?? 0).ToList();
                    if (pos.Count >= 2)
                    {
                        entity.Position = pos;
                        _display.RedrawCurrentContent?.Invoke();
                        await PollingDelay(500);
                    }
                }
            }
            entityNode.Remove("movePath");
        }
    }

    public async Task<int> PlayDiceRollRequest(RollRequest request)
    {
        if (_cachedBorderDrawer == null) return 1;
        if (_cachedPages == null) RebuildCache();

        bool isPair = request.Advantage || request.Disadvantage;
        var rng = new Random();

        await StreamHistoryEntries(request.History);
        while (ConsoleMouseReader.TryReadKeyDown().HasValue) { }

        // Show static dice + start countdown
        _animationRightLines = BuildRollPanel(request,
            isPair ? DiceArt.GetD20PairLines(20, 20) : DiceArt.GetD20RollLines(),
            rolled: false);
        _animationInProgress = true;
        RebuildCache();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        bool playerTriggered = false;
        int lastTimerSecond = -1;

        while (DateTime.UtcNow < deadline)
        {
            Term.CursorVisible = false;
            int secondsLeft = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);

            // Poll mouse: tab switches, map hover, cursor shape.
            // _animationRightLines != null keeps dice image in right panel regardless of SelectedImageLines.
            _display.PollAction?.Invoke();
            if (_display.PendingCommand != null)
            {
                var cmd = _display.PendingCommand;
                _display.PendingCommand = null;
                SwitchTab(cmd);
                lastTimerSecond = -1; // force redraw after tab switch
            }

            // Redraw timer only when the second counter changes — not every poll tick.
            if (secondsLeft != lastTimerSecond)
            {
                lastTimerSecond = secondsLeft;
                RerenderDialogBlock(MakeTimerAction(secondsLeft));
            }

            var pressedKey = ConsoleMouseReader.TryReadKeyDown();
            if (pressedKey.HasValue)
            {
                if (pressedKey == ConsoleKey.F1) { SwitchTab("F1"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F2) { SwitchTab("F2"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F3) { SwitchTab("F3"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F4) { SwitchTab("F4"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F5) { SwitchTab("F5"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F6) { SwitchTab("F6"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F7) { SwitchTab("F7"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F8) { SwitchTab("F8"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F9) { SwitchTab("F9"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.F10 && _display.JournalShown) { SwitchTab("F10"); lastTimerSecond = -1; continue; }
                if (pressedKey == ConsoleKey.Spacebar || pressedKey == ConsoleKey.Enter)
                {
                    playerTriggered = true;
                    break;
                }
                // all other keys are ignored
            }
            await Animations.DelayAsync(TimeSpan.FromMilliseconds(16));
        }

        // Determine rolls. The timer has expired - a roll for the player, fair: previously, 1 was counted
        // (critical failure), and a press that did not reach (Enter, while the GM was still typing) turned into
        // a failure.
        int roll1, roll2, usedRoll;
        roll1 = rng.Next(1, 21);
        roll2 = isPair ? rng.Next(1, 21) : roll1;
        usedRoll = request.Advantage ? Math.Max(roll1, roll2)
                 : request.Disadvantage ? Math.Min(roll1, roll2)
                 : roll1;
        _ = playerTriggered;

        request.Roll1 = roll1;
        request.Roll2 = isPair ? roll2 : null;
        request.Critical = !request.Difficulty.HasValue ? null
                         : usedRoll == 20 ? "critical_success"
                         : usedRoll == 1  ? "critical_failure"
                         : null;

        var soundStarted = Sound.PlayDiceRoll();
        while (!soundStarted.IsCompleted) await PollingDelay(8);

        // Settle animation
        (int delay, float noise)[] frames =
        [
            (60, 0.9f),(60, 0.9f),(60, 0.9f),(60, 0.9f),(60, 0.9f),
            (60, 0.9f),(60, 0.9f),(60, 0.9f),(60, 0.9f),(60, 0.9f),
            (60, 0.9f),(60, 0.9f),(60, 0.9f),(60, 0.9f),(60, 0.9f),
            (60, 0.9f), (60, 0.85f), (80, 0.7f), (100, 0.5f),
            (130, 0.3f), (160, 0.1f), (60, 0.0f),
        ];

        await Animations.PlayAsync(frames.Select(frame => new AnimationLoop.Frame(
            TimeSpan.FromMilliseconds(frame.delay), () =>
        {
            var noise = frame.noise;
            int d1 = noise > 0 ? rng.Next(1, 21) : roll1;
            int d2 = noise > 0 ? rng.Next(1, 21) : roll2;
            _animationRightLines = BuildRollPanel(request,
                isPair ? (noise > 0 ? DiceArt.GetNoisyD20PairLines(d1, d2, noise, rng) : DiceArt.GetD20PairLines(roll1, roll2))
                       : (noise > 0 ? DiceArt.GetNoisyD20RollLines(d1, noise, rng) : DiceArt.GetD20RollLines(usedRoll)),
                rolled: false);
            RebuildCache();
            RerenderDialogBlock(MakeTimerAction(-1));
        })), PollDuringStreaming, catchUp: false);

        // The dice has been spun - first the raw value without modifiers. With advantage/disadvantage,
        // modifiers apply only to the die USED (roll1 on a equal/ normal roll); the second die remains as it
        // fell - it itself is not modified according to the rules.
        bool usedIsFirst = !isPair || (request.Advantage ? roll1 >= roll2 : roll1 <= roll2);
        string[] DiceArtFor(int total) => isPair
            ? DiceArt.GetD20PairLines(usedIsFirst ? total : roll1, usedIsFirst ? roll2 : total)
            : DiceArt.GetD20RollLines(total);

        // Natural 1/20 - the number on the die is not changed by modifiers (crit is determined by the raw
        // roll), but the modifiers are still highlighted synchronously with the number. It is only relevant
        // when there is difficulty (crit makes sense against DC/AC) - for example, with initiative (Difficulty
        // == null) nat. 1/20 does not mean anything, the modifier should be applied as usual.
        bool isCrit = request.Difficulty.HasValue && usedRoll is 1 or 20;
        int DisplayValue(int total) => isCrit ? usedRoll : total;

        int runningTotal = usedRoll;
        _animationRightLines = BuildRollPanel(request, DiceArtFor(runningTotal), rolled: false);
        RebuildCache();
        RerenderDialogBlock(DrawSpinner);
        await PollingDelay(500);

        // Modifiers are added one at a time: highlight → add to the value on the die. The total for all
        // modifiers is exactly 2.8 seconds, equally divided between them: the more modifiers, the shorter the
        // delay for each (and not longer). The countdown is from ONE common starting point (DelayUntil with
        // absolute time), and not from the sum of fixed pauses: the rendering time between steps is not free
        // and would otherwise accumulate on top of the budget.
        if (request.Modifiers.Count > 0)
        {
            const int totalMs = 2800;
            var modsStart = DateTime.UtcNow;
            int perModifierMs = totalMs / request.Modifiers.Count;
            for (int mi = 0; mi < request.Modifiers.Count; mi++)
            {
                long baseMs = (long)perModifierMs * mi;

                // ON: modifier and the number on the die are highlighted strictly simultaneously.
                _animationRightLines = BuildRollPanel(request, DiceArtFor(DisplayValue(runningTotal)), rolled: false, highlightModifierIndex: mi, highlightDiceOccurrence: usedIsFirst ? 0 : 1);
                RebuildCache();
                RerenderDialogBlock(DrawSpinner);
                await DelayUntil(modsStart.AddMilliseconds(baseMs + perModifierMs / 3));

                // Apply the modifier - the number on the die is updated, the highlight remains.
                runningTotal += request.Modifiers[mi].Value;
                _animationRightLines = BuildRollPanel(request, DiceArtFor(DisplayValue(runningTotal)), rolled: false, highlightModifierIndex: mi, highlightDiceOccurrence: usedIsFirst ? 0 : 1);
                RebuildCache();
                RerenderDialogBlock(DrawSpinner);
                await DelayUntil(modsStart.AddMilliseconds(baseMs + 2 * perModifierMs / 3));

                // OFF: pause without highlighting before the next modifier.
                _animationRightLines = BuildRollPanel(request, DiceArtFor(DisplayValue(runningTotal)), rolled: false);
                RebuildCache();
                RerenderDialogBlock(DrawSpinner);
                await DelayUntil(modsStart.AddMilliseconds(baseMs + perModifierMs));
            }
        }

        // All modifiers are applied - show the result (success/failure/crit).
        _animationRightLines = BuildRollPanel(request, DiceArtFor(DisplayValue(runningTotal)), rolled: true, usedRoll);
        RebuildCache();
        RerenderDialogBlock(DrawSpinner);
        await PollingDelay(1500);

        // Animation done; keep panel frozen until player selects/hovers something.
        _animationInProgress = false;
        return usedRoll;
    }

    private Action MakeTimerAction(int secondsLeft) => () =>
    {
        _spinnerFooterVisible = secondsLeft < 0;
        if (HasSplit)
            _cachedBorderDrawer.DrawSeparatorWith2Parts('┴', LeftWidth, RightPanelWidth);
        else
            _cachedBorderDrawer.DrawSeparator();

        if (secondsLeft < 0)
        {
            string frame = Spinner.Frames[_spinnerFrame % Spinner.Frames.Length];
            _cachedBorderDrawer.DrawContentLine(() =>
                ColorHelper.WriteColored($" {frame} " + L.T("Rolling..."), _display.SystemCommandHistory));
        }
        else
        {
            string line = secondsLeft == 0 ? " " + L.T("Auto-roll (1)")
                : " " + L.F("[Space/Enter] roll ⏱ {0,2}s", secondsLeft);
            _cachedBorderDrawer.DrawContentLine(() => Console.Write(line));
        }

        _cachedBorderDrawer.DrawBottomBorder();
    };

    private string[] BuildRollPanel(RollRequest request, string[] diceLines, bool rolled, int? usedRoll = null, int? highlightModifierIndex = null, int highlightDiceOccurrence = 0)
    {
        bool isPair = request.Advantage || request.Disadvantage;
        int w = RightPanelWidth;

        var header = new List<string>();
        if (request.Difficulty.HasValue)
            header.Add(Fit("  " + L.F("DC: {0}", request.Difficulty), w));
        if (isPair)
        {
            string label = "  " + (request.Advantage ? L.T("ADVANTAGE") : L.T("DISADVANTAGE"));
            header.Add(Fit(label, w));
        }

        int diceEnd = diceLines.Length;
        while (diceEnd > 0 && string.IsNullOrEmpty(diceLines[diceEnd - 1])) diceEnd--;
        var dice = diceLines[..diceEnd];

        var footer = new List<string>();
        if (rolled && usedRoll.HasValue)
        {
            int modSum = request.Modifiers.Sum(m => m.Value);
            string? outcome = request.Critical == "critical_success" ? "  " + L.T("CRITICAL SUCCESS!")
                            : request.Critical == "critical_failure" ? "  " + L.T("CRITICAL FAILURE!")
                            : request.Difficulty.HasValue
                                ? "  " + ((usedRoll.Value + modSum) >= request.Difficulty.Value ? L.T("SUCCESS") : L.T("FAILURE"))
                                : null;
            if (outcome != null)
                footer.Add(Fit(outcome, w));
        }
        int? highlightFooterIndex = null;
        if (request.Modifiers.Count > 0)
        {
            footer.Add("");
            int idx = 0;
            foreach (var mod in request.Modifiers.Take(3))
            {
                string sign = mod.Value >= 0 ? "+" : "";
                if (idx == highlightModifierIndex) highlightFooterIndex = footer.Count;
                footer.Add(Fit($"  {sign}{mod.Value} {mod.Name}", w));
                idx++;
            }
        }

        // Row 0 and last row always empty; content occupies rows 1..MaxHistoryLines-2
        int contentStart = 1;
        int contentEnd = _display.MaxHistoryLines - 1;
        int contentSlots = contentEnd - contentStart;

        var panel = new string[_display.MaxHistoryLines];
        for (int i = 0; i < panel.Length; i++) panel[i] = "";

        // Three independent groups: header is pressed to the top (contentStart, grows down), footer is pressed
        // to the bottom (contentEnd-1, grows up), the cube is always strictly in the center - neither the size
        // header nor the size footer moves the position of the cube in any way.
        int diceStart = contentStart + Math.Max(0, (contentSlots - dice.Length) / 2);

        int row = contentStart;
        foreach (var h in header) { if (row < contentEnd) panel[row++] = h; }

        row = diceStart;
        foreach (var d in dice) { if (row < contentEnd) panel[row++] = Fit(d, w); }

        if (highlightModifierIndex.HasValue)
        {
            _diceHighlightStart = diceStart;
            _diceHighlightEnd = Math.Min(contentEnd, diceStart + dice.Length);
            _diceHighlightColor = ColorHelper.Pale(_display.MainForeground, 1);
            _diceHighlightOccurrence = highlightDiceOccurrence;
        }
        else
        {
            _diceHighlightStart = -1;
            _diceHighlightEnd = -1;
            _diceHighlightColor = null;
            _diceHighlightOccurrence = 0;
        }

        _outcomeRow = -1;
        _outcomeColor = null;
        _modHighlightRow = -1;
        _modHighlightColor = null;
        row = Math.Max(contentStart, contentEnd - footer.Count);
        for (int fi = 0; fi < footer.Count; fi++)
        {
            string f = footer[fi];
            if (row < contentEnd)
            {
                bool isOutcome = rolled && fi == 0;
                if (isOutcome)
                {
                    _outcomeRow = row;
                    bool isSuccess = f.Contains(L.T("SUCCESS"), StringComparison.OrdinalIgnoreCase);
                    _outcomeColor = isSuccess ? [80, 220, 80] : [220, 80, 80];
                }
                if (fi == highlightFooterIndex)
                {
                    _modHighlightRow = row;
                    _modHighlightColor = ColorHelper.Pale(_display.MainForeground, 1);
                }
                panel[row++] = f;
            }
        }

        return panel;
    }

    private static string Fit(string s, int width) =>
        s.Length > width ? s[..width] : s;

    // N- is a continuous run of numbers in a line - where DiceArt.GetD20*Lines substituted the number of the
    // cube inside the braille picture (occurrence=0 is the first/only die, 1 is the second in a pair).
    private static bool TryFindDigitRun(string text, int occurrence, out int start, out int length)
    {
        int found = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsDigit(text[i])) continue;
            int j = i;
            while (j < text.Length && char.IsDigit(text[j])) j++;
            if (found == occurrence)
            {
                start = i;
                length = j - i;
                return true;
            }
            found++;
            i = j - 1;
        }
        start = 0;
        length = 0;
        return false;
    }

    public async Task PlayDiceRollAnimation(int result, int diceMax = 20)
    {
        if (!HasSplit || _cachedBorderDrawer == null) return;
        if (_cachedPages == null) RebuildCache();

        var rng = new Random();

        // (frameDelayMs, noiseIntensity) — fast chaos → slow settling
        (int delay, float intensity)[] frames =
        [
            (60, 0.9f), (60, 0.9f), (60, 0.9f), (60, 0.9f),
            (60, 0.9f), (60, 0.9f), (60, 0.85f), (60, 0.85f),
            (60, 0.8f), (60, 0.8f), (80, 0.7f), (100, 0.6f),
            (120, 0.45f), (140, 0.3f), (170, 0.15f), (200, 0.05f),
            (60, 0.0f),
        ];

        _animationInProgress = true;
        var soundStarted = Sound.PlayDiceRoll();
        while (!soundStarted.IsCompleted) await PollingDelay(8);
        await Animations.PlayAsync(frames.Select(frame => new AnimationLoop.Frame(
            TimeSpan.FromMilliseconds(frame.delay), () =>
        {
            var intensity = frame.intensity;
            int displayNumber = intensity > 0 ? rng.Next(1, diceMax + 1) : result;
            _animationRightLines = intensity > 0
                ? DiceArt.GetNoisyD20Lines(displayNumber, intensity, rng)
                : DiceArt.GetD20Lines(result);
            RerenderDialogBlock();
        })), PollDuringStreaming, catchUp: false);

        // Return to normal
        await PollingDelay(2000);
        _animationInProgress = false;
        _animationRightLines = null;
        RerenderDialogBlock();
    }

    private void DrawDialogText(DisplayLine line)
    {
        Console.Write(" ");
        if (line.IsSystem)
        {
            string fullLine = (line.Prefix ?? "") + line.Text;
            ColorHelper.WriteColored(fullLine, line.PrefixColor ?? _display.SystemCommandHistory);
        }
        else
        {
            if (!string.IsNullOrEmpty(line.Prefix))
            {
                if (line.PrefixColor != null)
                    ColorHelper.WriteColored(line.Prefix, line.PrefixColor);
                else
                    Console.Write(line.Prefix);
            }
            if (line.Highlight && _highlighter != null)
            {
                int depth = line.BracketDepthAtStart;
                foreach (var (text, color) in _highlighter.Segments(line.Text, ref depth))
                {
                    var fg = color ?? line.BaseColor;
                    if (fg == null) Console.Write(text);
                    else ColorHelper.WriteColored(text, fg);
                }
            }
            else if (line.BaseColor != null)
                ColorHelper.WriteColored(line.Text, line.BaseColor);
            else
                Console.Write(line.Text);
        }
    }

    private void DrawDialogLine(DisplayLine line, Action rightContent)
    {
        if (HasSplit)
            _cachedBorderDrawer.DrawContentLine2Columns(() => DrawDialogText(line), rightContent, LeftWidth, RightPanelWidth);
        else
            _cachedBorderDrawer.DrawContentLine(() => DrawDialogText(line));
    }

    private void DrawEmptyDialogLine(Action rightContent)
    {
        if (HasSplit)
            _cachedBorderDrawer.DrawContentLine2Columns(() => { }, rightContent, LeftWidth, RightPanelWidth);
        else
            _cachedBorderDrawer.DrawContentLine(() => { });
    }

    private void RerenderDialogBlock(Action? afterDraw = null)
    {
        if (_cachedBorderDrawer == null) return;
        if (afterDraw != null)
        {
            _footerDraw = afterDraw;
            _spinnerFooterVisible = afterDraw == (Action)DrawSpinner;
        }
        int savedCursorLeft = Term.CursorLeft;
        int savedCursorTop = Term.CursorTop;
        bool savedCursorVisible = Term.CursorVisible;

        Term.CursorVisible = false;
        // Ensure main colors are active so WriteColored saves/restores the correct base color
        ColorHelper.SetBackgroundColor(_display.MainBackground);
        ColorHelper.SetForegroundColor(_display.MainForeground);
        Term.SetCursorPosition(0, _dialogBlockStartTop);
        RenderDialogBlock();
        afterDraw?.Invoke(); // cursor is right below the dialog block

        Term.SetCursorPosition(savedCursorLeft, savedCursorTop);
        Term.CursorVisible = savedCursorVisible;
    }

    private bool SwitchTab(string tabKey)
    {
        // Pressed F- key - switch once, repetitions do not redraw the screen.
        if (Enum.TryParse<ConsoleKey>(tabKey, out var fKey) && InputBox.IsFKeyRepeat(fKey)) return false;
        if (_display.TabSwitchProvider == null) return false;
        var (draw, title) = _display.TabSwitchProvider(tabKey);
        if (draw == null || title == null) return false;
        ConsoleMouseReader.CancelDrag();
        MouseUiHelper.CancelPreparedPicture(_display);
        _display.MapHoverEnabled = tabKey is "F1" or "F2";
        _display.StreamingTabTitle = title;
        _dialogContentDrawn = false;
        if (tabKey is not ("F1" or "F2")) { _display.HoveredCell = null; _display.HoveredDoor = null; }
        Term.CursorVisible = false;
        Term.SetCursorPosition(0, 0);
        _cachedBorderDrawer.DrawTopBorder();
        _cachedBorderDrawer.DrawContentLine(() => MouseUiHelper.WriteColoredTitle(title, _display));
        int contentTop = Term.CursorTop;
        draw();
        _dialogBlockStartTop = Term.CursorTop;

        // Set RedrawCurrentContent for the new tab (F6/F7 use F3's pure draw - character card - to avoid
        // re-applying state) F6–F9 — character sub-tabs (or journal tabs, if there is a journal on the screen):
        // clean rendering — of their screen.
        bool journalSub = _display.JournalShown && tabKey is "F6" or "F7" or "F8" or "F9" or "F10";
        var pureDrawKey = tabKey is "F6" or "F7" or "F8" or "F9" or "F10" ? (journalSub ? "F3" : "F4") : tabKey;
        var (pureDraw, _) = _display.TabSwitchProvider(pureDrawKey);
        if (pureDraw != null)
        {
            var ctop = contentTop; var cd = pureDraw;
            _display.RedrawCurrentContent = () =>
            {
                int sl = Term.CursorLeft, st = Term.CursorTop;
                Term.CursorVisible = false;
                Term.SetCursorPosition(0, ctop);
                cd();
                Term.SetCursorPosition(sl, st);
            };
        }

        // Update poll action for the new tab context
        Func<object, Action?>? factory = tabKey switch
        {
            "F1" or "F2" => _display.MapPollActionFactory,
            "F3" => _display.JournalPollActionFactory,
            "F6" or "F7" or "F8" or "F9" or "F10" when journalSub => _display.JournalPollActionFactory,
            "F4" or "F6" or "F7" or "F8" or "F9" => _display.CharacterPollActionFactory,
            "F5" => _display.AbilitiesPollActionFactory,
            _ => null
        };
        if (factory != null) _display.PollAction = factory(this);

        // Restore per-tab Tab handlers
        if (tabKey is "F1")
        {
            _display.OnTabKey = _display.MapOnTabKey;
            _display.OnShiftTabKey = _display.MapOnShiftTabKey;
        }
        else if (tabKey is "F3" || journalSub)
        {
            _display.OnTabKey = _display.JournalOnTabKey;
            _display.OnShiftTabKey = _display.JournalOnShiftTabKey;
        }
        else if (tabKey is "F4" or "F6" or "F7" or "F8" or "F9")
        {
            _display.OnTabKey = _display.CharacterOnTabKey;
            _display.OnShiftTabKey = _display.CharacterOnShiftTabKey;
        }
        else if (tabKey is "F5")
        {
            _display.OnTabKey = null;
            _display.OnShiftTabKey = null;
        }

        RerenderDialogBlock(_footerDraw);
        return true;
    }

    private void PollDuringStreaming()
    {
        PollAndHandleTabSwitch();

        // Drain keyboard events so they don't block DrainMouseEvents (which stops at the first non-mouse
        // record). KeyAvailable check makes this non-blocking. claude.exe uses RedirectStandardInput=true, so
        // it has its own pipe stdin — no conflict. Not Console.KeyAvailable: it throws mouse events out of the
        // queue (clicks were lost).
        if (ConsoleMouseReader.TryReadKey() is not { } key) return;
        switch (key.Key)
        {
            case ConsoleKey.F1: SwitchTab("F1"); break;
            case ConsoleKey.F2: SwitchTab("F2"); break;
            case ConsoleKey.F3: SwitchTab("F3"); break;
                    case ConsoleKey.F4: SwitchTab("F4"); break;
            case ConsoleKey.F5: SwitchTab("F5"); break;
            case ConsoleKey.F6: SwitchTab("F6"); break;
            case ConsoleKey.F7: SwitchTab("F7"); break;
            case ConsoleKey.F8: SwitchTab("F8"); break;
            case ConsoleKey.F9: SwitchTab("F9"); break;
            case ConsoleKey.F10 when _display.JournalShown: SwitchTab("F10"); break;
            case ConsoleKey.Tab:
            {
                var tabAction = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                    ? _display.OnShiftTabKey
                    : _display.OnTabKey;
                if (tabAction != null) { tabAction(); _display.RedrawCurrentContent?.Invoke(); }
                break;
            }
        }
    }

    private void RedrawStreamingFooter()
    {
        if (_cachedBorderDrawer == null || !_isStreaming || !_spinnerFooterVisible) return;
        int left = Term.CursorLeft, top = Term.CursorTop;
        bool visible = Term.CursorVisible;
        try
        {
            Term.CursorVisible = false;
            Term.SetCursorPosition(0, _dialogBlockStartTop + 2 + _display.MaxHistoryLines);
            DrawSpinner();
        }
        finally
        {
            Term.SetCursorPosition(left, top);
            Term.CursorVisible = visible;
        }
    }

    private void DrawSpinner()
    {
        string frame = Spinner.Frames[_spinnerFrame % Spinner.Frames.Length];
        if (HasSplit)
            _cachedBorderDrawer.DrawSeparatorWith2Parts('┴', LeftWidth, RightPanelWidth);
        else
            _cachedBorderDrawer.DrawSeparator();
        _cachedBorderDrawer.DrawContentLine(() =>
            ColorHelper.WriteColored($" {frame} " + L.T("Waiting..."), _display.SystemCommandHistory));
        _cachedBorderDrawer.DrawBottomBorder();
    }

    public async Task<string?> PlayAskPlayerRequest(AskPlayerRequest request)
    {
        if (_cachedBorderDrawer == null) return null;
        if (_cachedPages == null) RebuildCache();

        await StreamHistoryEntries(request.History);
        WindowControls.NotifyIfMinimized();
        while (ConsoleMouseReader.TryReadKeyDown().HasValue) { }

        var deadline = DateTime.UtcNow.AddSeconds(50);

        bool hasOptions = request.Options is { Length: > 0 };
        string? answer = hasOptions
            ? await PlayOptionsSelection(request.Options!.Select(o => o.Replace("\r", "").Replace("\n", "")).ToArray(), deadline)
            : await PlayFreeTextInput(deadline);

        if (answer != null)
        {
            _setting.History ??= [];
            _setting.History.Add(new DialogMessage { Author = _setting.Hero?.Name ?? "Герой", Text = answer });

            // We show the player’s response immediately, rather than waiting for the next redraw from the
            // outside (which will occur only when the entire request - SendAction/FixHeroForNewGame - is
            // completed, that is, after the AI’s response). Same trick as PrepareStreaming() for the initial
            // player action: rebuild the cache and redraw the dialogue block immediately.
            _display.DialogScrollOffset = 0;
            RebuildCache();
            RerenderDialogBlock(DrawSpinner);
        }

        return answer;
    }

    // select_target: selecting creatures/cells with the mouse on the map. Illumination - via
    // _display.TargetSelection (MapObjectsProvider.ApplyTargetTint), the preview of the area is recalculated
    // when changing the cell under the cursor.
    public async Task<TargetSelectionResult> PlayTargetSelectionRequest(TargetSelectionRequest request)
    {
        if (_cachedBorderDrawer == null) return new TargetSelectionResult { Cancelled = true };
        if (_cachedPages == null) RebuildCache();

        await StreamHistoryEntries(request.History);
        WindowControls.NotifyIfMinimized();
        while (ConsoleMouseReader.TryReadKeyDown().HasValue) { }

        bool onMap = _display.StreamingTabTitle == null ? _display.ActiveTabKey == "F1" : _display.MapHoverEnabled;
        if (!onMap) SwitchTab("F1");
        _display.MapLevel = MapLevel.Location; // target selection - only on the location map

        var ws = _setting;
        var heroPos = ws.Hero?.Position;
        // Like MapObjectsProvider.IsCellVisible: in a room or in an open area - only what the hero sees
        // (previously in the area it was possible to aim through the forest); outside the rooms on old maps -
        // that's it.
        bool limited = heroPos is { Count: >= 2 } && (MovementCalculator.GetRoom(ws, heroPos[0], heroPos[1]) != null
                                                      || ws.Map.TerrainAt(heroPos[0], heroPos[1]) is { Indoor: false });
        bool IsVisible(int c, int r) => !limited || _storage.VisibleCells.Contains((c, r));
        bool IsExplored(int c, int r) => _storage.ExploredCells.Contains((c, r));

        var view = new TargetSelectionView
        {
            Origin = TargetGeometry.IsDirectional(request.Shape) && heroPos is { Count: >= 2 } ? (heroPos[0], heroPos[1]) : null
        };
        view.Selectable.UnionWith(TargetGeometry.SelectableCells(ws, request, IsVisible, IsExplored));
        _display.TargetSelection = view;
        _display.HoveredCell = null;
        _display.OnMapRedraw?.Invoke();

        var picks = new List<(int col, int row)>();
        (int col, int row)? hovered = null;
        HashSet<(int, int)> CellsOf((int col, int row) p) => request.Shape != null
            ? TargetGeometry.AreaCells(ws, p.col, p.row, request.Shape, request.SizeFt)
            : [p];

        void Redraw(IEnumerable<(int, int)> cells) => _display.OnMapCellsRedraw?.Invoke(cells.Distinct().ToList());

        void UpdateSelected()
        {
            var old = view.Selected.ToList();
            view.Selected.Clear();
            foreach (var p in picks) view.Selected.UnionWith(CellsOf(p));
            Redraw(old.Concat(view.Selected));
        }

        void UpdatePreview()
        {
            var old = view.Preview;
            view.Preview = hovered.HasValue ? CellsOf(hovered.Value) : [];
            Redraw(old.Concat(view.Preview));
        }

        TargetSelectionResult BuildResult() => new()
        {
            Targets = picks.Select(p =>
            {
                var t = TargetGeometry.Describe(ws, p.col, p.row);
                if (request.Shape != null)
                {
                    var area = CellsOf(p);
                    t.AreaCells = area.OrderBy(c => c.Item1).ThenBy(c => c.Item2).Select(c => new List<int> { c.Item1, c.Item2 }).ToList();
                    t.InArea = TargetGeometry.CreaturesIn(ws, area);
                    t.ObjectsInArea = TargetGeometry.ObjectsIn(ws, area);
                }
                return t;
            }).ToList()
        };

        var deadline = DateTime.UtcNow.AddSeconds(50);
        TargetSelectionResult? result = null;
        int lastSeconds = -1, lastPicks = -1;

        while (result == null)
        {
            int secondsLeft = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
            if (secondsLeft != lastSeconds || picks.Count != lastPicks)
            {
                lastSeconds = secondsLeft;
                lastPicks = picks.Count;
                RebuildCache();
                RerenderDialogBlock(MakeTargetSelectionAction(picks.Count, request.Count, secondsLeft));
            }
            if (DateTime.UtcNow >= deadline) { result = new TargetSelectionResult { TimedOut = true }; break; }

            var (mousePos, clickPos, _) = ConsoleMouseReader.DrainMouseEvents();
            if (mousePos.HasValue)
            {
                var cell = _display.ScreenCellToWorld(ConsoleMouseReader.ScreenToMapCell(mousePos.Value.x, mousePos.Value.y,
                    _display.MapDrawTop, _display.ViewCols(ws.Map), _display.ViewRows(ws.Map), _display.CellWidth));
                var target = cell.HasValue && view.Selectable.Contains(cell.Value) ? cell : null;
                ConsoleMouseReader.SetCursorShape(target != null);
                if (target != hovered) { hovered = target; UpdatePreview(); }
            }

            if (clickPos.HasValue)
            {
                var cell = _display.ScreenCellToWorld(ConsoleMouseReader.ScreenToMapCell(clickPos.Value.x, clickPos.Value.y,
                    _display.MapDrawTop, _display.ViewCols(ws.Map), _display.ViewRows(ws.Map), _display.CellWidth));
                if (cell.HasValue && view.Selectable.Contains(cell.Value)
                    && (request.AllowRepeat || !picks.Contains(cell.Value)))
                {
                    Sound.PlayClick();
                    picks.Add(cell.Value);
                    UpdateSelected();
                    if (picks.Count >= request.Count) result = BuildResult();
                }
            }

            if (result == null && ConsoleMouseReader.TryReadKey() is { } key)
            {
                switch (key.Key)
                {
                    case ConsoleKey.Enter when picks.Count > 0:
                        result = BuildResult();
                        break;
                    case ConsoleKey.Escape:
                        result = new TargetSelectionResult { Cancelled = true };
                        break;
                    case ConsoleKey.Backspace when picks.Count > 0:
                        picks.RemoveAt(picks.Count - 1);
                        UpdateSelected();
                        break;
                }
            }

            await Animations.DelayAsync(TimeSpan.FromMilliseconds(16));
        }

        _display.TargetSelection = null;
        ConsoleMouseReader.SetCursorShape(false);
        _display.OnMapRedraw?.Invoke();

        _setting.History ??= [];
        _setting.History.Add(new DialogMessage
        {
            Author = _setting.Hero?.Name ?? "Герой",
            Text = DescribeTargetSelection(result)
        });
        _display.DialogScrollOffset = 0;
        RebuildCache();
        RerenderDialogBlock(DrawSpinner);

        return result;
    }

    private static string DescribeTargetSelection(TargetSelectionResult result)
    {
        if (result.TimedOut) return L.W("No target selected (time ran out)");
        if (result.Cancelled) return L.W("Target selection cancelled");
        return L.W("Target: ") + string.Join(", ", result.Targets.Select(t =>
        {
            string label = t.Name != null ? $"{t.Name} [{t.Position[0]},{t.Position[1]}]" : $"[{t.Position[0]},{t.Position[1]}]";
            var inArea = (t.InArea ?? []).Concat(t.ObjectsInArea ?? []).Select(a => a.Name).ToList();
            if (inArea.Count > 0) label += " " + L.WF("(in the area: {0})", string.Join(", ", inArea));
            return label;
        }));
    }

    private Action MakeTargetSelectionAction(int picked, int total, int secondsLeft) => () =>
    {
        if (HasSplit)
            _cachedBorderDrawer.DrawSeparatorWith2Parts('┴', LeftWidth, RightPanelWidth);
        else
            _cachedBorderDrawer.DrawSeparator();

        _cachedBorderDrawer.DrawContentLine(() =>
        {
            string counter = total > 1 ? $" {picked}/{total} · " + L.T("Enter — done") : "";
            Console.Write(" " + L.F("Pick on the map ({0,2}s)", secondsLeft) + counter + " · " + L.T("Bksp — back · Esc — cancel"));
        });

        _cachedBorderDrawer.DrawBottomBorder();
    };

    private async Task<string?> PlayOptionsSelection(string[] options, DateTime deadline)
    {
        int selectedIdx = 0;
        int? hoveredIdx = null;
        var optionXPositions = new int[options.Length];

        while (true)
        {
            int optionsRowY = _dialogBlockStartTop + _display.MaxHistoryLines + 3;
            int secondsLeft = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);
            if (secondsLeft < 0) secondsLeft = 0;

            RebuildCache();
            RerenderDialogBlock(MakeOptionsAction(options, selectedIdx, hoveredIdx, secondsLeft, optionXPositions));

            if (DateTime.UtcNow >= deadline)
                return null;

            var (mousePos, clickPos, _) = ConsoleMouseReader.DrainMouseEvents();

            if (mousePos.HasValue)
            {
                hoveredIdx = mousePos.Value.y == optionsRowY
                    ? GetOptionIdxAt(mousePos.Value.x, optionXPositions, options)
                    : null;
            }

            if (clickPos.HasValue && clickPos.Value.y == optionsRowY)
            {
                int clicked = GetOptionIdxAt(clickPos.Value.x, optionXPositions, options);
                if (clicked >= 0) return options[clicked];
            }

            if (ConsoleMouseReader.TryReadKey() is { } key)
            {
                switch (key.Key)
                {
                    case ConsoleKey.F1: SwitchTab("F1"); break;
                    case ConsoleKey.F2: SwitchTab("F2"); break;
                    case ConsoleKey.F3: SwitchTab("F3"); break;
                    case ConsoleKey.F4: SwitchTab("F4"); break;
                    case ConsoleKey.F5: SwitchTab("F5"); break;
                    case ConsoleKey.F6: SwitchTab("F6"); break;
                    case ConsoleKey.F7: SwitchTab("F7"); break;
                    case ConsoleKey.F8: SwitchTab("F8"); break;
                    case ConsoleKey.F9: SwitchTab("F9"); break;
                    case ConsoleKey.F10 when _display.JournalShown: SwitchTab("F10"); break;
                    case ConsoleKey.LeftArrow:
                        selectedIdx = (selectedIdx - 1 + options.Length) % options.Length;
                        hoveredIdx = null;
                        break;
                    case ConsoleKey.RightArrow:
                        selectedIdx = (selectedIdx + 1) % options.Length;
                        hoveredIdx = null;
                        break;
                    case ConsoleKey.Enter:
                        return options[selectedIdx];
                }
            }

            // DrainMouseEvents above consumed option-area events; PollAction here
            // handles time-based hover commits and cursor shape with an empty queue.
            _display.PollAction?.Invoke();
            if (_display.PendingCommand != null)
            {
                var cmd = _display.PendingCommand;
                _display.PendingCommand = null;
                SwitchTab(cmd);
            }

            await Animations.DelayAsync(TimeSpan.FromMilliseconds(16));
        }
    }

    private Action MakeOptionsAction(string[] options, int selectedIdx, int? hoveredIdx, int secondsLeft, int[] outPositions) => () =>
    {
        if (HasSplit)
            _cachedBorderDrawer.DrawSeparatorWith2Parts('┴', LeftWidth, RightPanelWidth);
        else
            _cachedBorderDrawer.DrawSeparator();

        var highlightColor = ColorHelper.Pale(_display.MainForeground, 0.75);

        _cachedBorderDrawer.DrawContentLine(() =>
        {
            string prefix = secondsLeft > 0
                ? " " + L.F("Choose ({0,2}s): ", secondsLeft)
                : " " + L.T("Auto-choice: ");
            Console.Write(prefix);
            int curX = 1 + prefix.Length;

            for (int i = 0; i < options.Length; i++)
            {
                if (i > 0)
                {
                    Console.Write("      ");
                    curX += 6;
                }
                outPositions[i] = curX;

                // Mouse hover takes priority; if not hovering, keyboard selection is highlighted
                bool isHighlighted = hoveredIdx.HasValue
                    ? i == hoveredIdx.Value
                    : i == selectedIdx;

                if (isHighlighted)
                    ColorHelper.WriteColored(options[i], fgColor: highlightColor, bgColor: _display.MainBackground);
                else
                    Console.Write(options[i]);
                curX += options[i].Length;
            }
        });

        _cachedBorderDrawer.DrawBottomBorder();
    };

    private static int GetOptionIdxAt(short x, int[] positions, string[] options)
    {
        for (int i = 0; i < options.Length; i++)
            if (x >= positions[i] && x < positions[i] + options[i].Length)
                return i;
        return -1;
    }

    private async Task<string?> PlayFreeTextInput(DateTime deadline)
    {
        var sb = new System.Text.StringBuilder();
        int cursorPos = 0;
        int viewOffset = 0;
        int[] outCursorX = new int[1];

        while (true)
        {
            int inputRowY = _dialogBlockStartTop + _display.MaxHistoryLines + 3;
            int secondsLeft = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);
            if (secondsLeft < 0) secondsLeft = 0;

            string timerPart = secondsLeft > 0 ? L.F("({0,2}s)", secondsLeft) : L.T("(expired)");
            string prompt = " " + L.F("Input {0}: ", timerPart);
            int maxTextWidth = DialogTextWidth - prompt.Length;

            // Keep cursor inside the visible window with a small right margin
            const int scrollMargin = 3;
            if (cursorPos < viewOffset) viewOffset = cursorPos;
            if (cursorPos + scrollMargin > viewOffset + maxTextWidth) viewOffset = cursorPos + scrollMargin - maxTextWidth;

            string text = sb.ToString();
            int visibleLen = Math.Max(0, Math.Min(maxTextWidth, text.Length - viewOffset));
            string visible = visibleLen > 0 ? text.Substring(viewOffset, visibleLen) : "";

            RebuildCache();
            RerenderDialogBlock(MakeFreeTextAction(prompt, visible, cursorPos - viewOffset, outCursorX));
            Term.SetCursorPosition(outCursorX[0], inputRowY);
            Term.CursorVisible = true;

            if (DateTime.UtcNow >= deadline)
                return sb.Length > 0 ? sb.ToString() : null;

            _display.PollAction?.Invoke();
            if (_display.PendingCommand != null)
            {
                var cmd = _display.PendingCommand;
                _display.PendingCommand = null;
                SwitchTab(cmd);
            }

            if (ConsoleMouseReader.TryReadKey() is { } key)
            {
                switch (key.Key)
                {
                    case ConsoleKey.F1: SwitchTab("F1"); break;
                    case ConsoleKey.F2: SwitchTab("F2"); break;
                    case ConsoleKey.F3: SwitchTab("F3"); break;
                    case ConsoleKey.F4: SwitchTab("F4"); break;
                    case ConsoleKey.F5: SwitchTab("F5"); break;
                    case ConsoleKey.F6: SwitchTab("F6"); break;
                    case ConsoleKey.F7: SwitchTab("F7"); break;
                    case ConsoleKey.F8: SwitchTab("F8"); break;
                    case ConsoleKey.F9: SwitchTab("F9"); break;
                    case ConsoleKey.F10 when _display.JournalShown: SwitchTab("F10"); break;
                    case ConsoleKey.Enter:
                        Term.CursorVisible = false;
                        return sb.Length > 0 ? sb.ToString() : null;
                    case ConsoleKey.Backspace:
                        if (cursorPos > 0) { sb.Remove(cursorPos - 1, 1); cursorPos--; }
                        break;
                    case ConsoleKey.LeftArrow:
                        if (cursorPos > 0) cursorPos--;
                        break;
                    case ConsoleKey.RightArrow:
                        if (cursorPos < sb.Length) cursorPos++;
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            sb.Insert(cursorPos, key.KeyChar);
                            cursorPos++;
                        }
                        break;
                }
            }

            await Animations.DelayAsync(TimeSpan.FromMilliseconds(16));
        }
    }

    private Action MakeFreeTextAction(string prompt, string visible, int cursorInVisible, int[] outCursorX) => () =>
    {
        if (HasSplit)
            _cachedBorderDrawer.DrawSeparatorWith2Parts('┴', LeftWidth, RightPanelWidth);
        else
            _cachedBorderDrawer.DrawSeparator();

        outCursorX[0] = 1 + prompt.Length + cursorInVisible;

        _cachedBorderDrawer.DrawContentLine(() =>
        {
            Console.Write(prompt);
            Console.Write(visible);
            int maxTextWidth = DialogTextWidth - prompt.Length;
            int padding = maxTextWidth - visible.Length;
            if (padding > 0) Console.Write(new string(' ', padding));
        });

        _cachedBorderDrawer.DrawBottomBorder();
    };

    private static List<(int lineStart, int lineCount)> CalculatePages(List<int> groupSizes, int pageSize)
    {
        var pages = new List<(int, int)>();
        int lineStart = 0, i = 0;
        while (i < groupSizes.Count)
        {
            int pageLines = 0, pageStart = lineStart;
            while (i < groupSizes.Count && pageLines + groupSizes[i] <= pageSize)
            {
                pageLines += groupSizes[i]; lineStart += groupSizes[i]; i++;
            }
            if (pageLines == 0) // entry too large — force include alone
            {
                pageLines = groupSizes[i]; lineStart += groupSizes[i]; i++;
            }
            pages.Add((pageStart, pageLines));
        }
        if (pages.Count == 0) pages.Add((0, 0));
        return pages;
    }
}
