using System.Diagnostics;

namespace NaviDnD.Helpers;

// Cooperative UI scheduler: callbacks run only from Tick, never on background threads.
public sealed class AnimationLoop(Func<TimeSpan>? clock = null)
{
    public readonly record struct Frame(TimeSpan Duration, Action Draw);
    private sealed class Track(Frame[] frames, TimeSpan start, bool repeat, bool catchUp)
    {
        public readonly Frame[] Frames = frames;
        public TimeSpan Next = start;
        public int Index;
        public readonly bool Repeat = repeat;
        public readonly bool CatchUp = catchUp;
        public bool Done;
    }
    private readonly Func<TimeSpan> _clock = clock ?? (() => TimeSpan.FromSeconds((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency));
    private readonly List<Track> _tracks = [];
    private bool _ticking;

    public IDisposable Start(IEnumerable<Frame> frames, bool repeat = false, bool catchUp = true)
    {
        var sequence = frames.ToArray();
        if (sequence.Length == 0 || sequence.Any(f => f.Duration <= TimeSpan.Zero))
            throw new ArgumentException("Animation frames must have positive durations.", nameof(frames));
        var track = new Track(sequence, _clock(), repeat, catchUp);
        _tracks.Add(track);
        return new Registration(track);
    }

    private sealed class Registration(Track track) : IDisposable
    {
        public bool Done => track.Done;
        public void Dispose() => track.Done = true;
    }

    public void Tick()
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            var now = _clock();
            foreach (var track in _tracks.ToArray())
            {
                // Execute every logical step; absolute deadlines avoid accumulating render time.
                while (!track.Done && now >= track.Next)
                {
                    if (track.Index == track.Frames.Length)
                    {
                        if (!track.Repeat) { track.Done = true; break; }
                        track.Index = 0;
                    }
                    var frame = track.Frames[track.Index++];
                    track.Next += frame.Duration;
                    frame.Draw();
                    if (!track.CatchUp)
                    {
                        // Text remains readable after a slow frame: no burst of overdue characters.
                        track.Next = _clock() + frame.Duration;
                        break;
                    }
                }
            }
            _tracks.RemoveAll(t => t.Done);
        }
        finally { _ticking = false; }
    }

    public async Task PlayAsync(IEnumerable<Frame> frames, Action? poll = null, CancellationToken cancellationToken = default, Func<bool>? stop = null, bool catchUp = true)
    {
        var sequence = frames.ToArray();
        if (sequence.Length == 0) return;
        using var registration = (Registration)Start(sequence, catchUp: catchUp);
        while (!registration.Done)
        {
            cancellationToken.ThrowIfCancellationRequested();
            poll?.Invoke();
            if (stop?.Invoke() == true) return;
            Tick();
            if (!registration.Done) await Task.Delay(8, cancellationToken);
        }
    }

    public async Task DelayAsync(TimeSpan duration, Action? poll = null)
    {
        var end = _clock() + duration;
        do
        {
            poll?.Invoke();
            Tick();
            var remaining = end - _clock();
            if (remaining <= TimeSpan.Zero) return;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(8) ? remaining : TimeSpan.FromMilliseconds(8));
        } while (true);
    }
}
