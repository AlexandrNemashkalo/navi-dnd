namespace NaviDnD.Helpers;

// Precision touchpads can send wheel deltas smaller than one Windows wheel notch (120).
public sealed class WheelDeltaAccumulator
{
    private int _remainder;
    private long? _lastAt;

    public int Add(int delta, long now)
    {
        if (delta == 0) return 0;
        if (_lastAt is { } last && now - last > 300) _remainder = 0;
        _lastAt = now;
        _remainder += delta;
        int notches = _remainder / 120;
        _remainder %= 120;
        return notches;
    }
}
