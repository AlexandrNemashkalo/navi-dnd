using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class AnimationLoopTests
{
    [Fact]
    public void TracksKeepIndependentRatesAndCatchUpWithoutDrift()
    {
        var now = TimeSpan.Zero;
        var loop = new AnimationLoop(() => now);
        int text = 0, spinner = 0;
        using var a = loop.Start([new(TimeSpan.FromMilliseconds(25), () => text++)], repeat: true);
        using var b = loop.Start([new(TimeSpan.FromMilliseconds(100), () => spinner++)], repeat: true);
        loop.Tick();
        Assert.Equal((1, 1), (text, spinner));
        now = TimeSpan.FromMilliseconds(99);
        loop.Tick();
        Assert.Equal((4, 1), (text, spinner));
        now = TimeSpan.FromMilliseconds(200);
        loop.Tick();
        Assert.Equal((9, 3), (text, spinner));
        a.Dispose();
        now = TimeSpan.FromMilliseconds(300);
        loop.Tick();
        Assert.Equal((9, 4), (text, spinner));
    }

    [Fact]
    public void VariableFramesKeepTheirDurationsAndFinishOnce()
    {
        var now = TimeSpan.Zero;
        var loop = new AnimationLoop(() => now);
        var drawn = new List<int>();
        using var track = loop.Start([
            new(TimeSpan.FromMilliseconds(60), () => drawn.Add(1)),
            new(TimeSpan.FromMilliseconds(160), () => drawn.Add(2)),
            new(TimeSpan.FromMilliseconds(200), () => drawn.Add(3))]);
        loop.Tick();
        now = TimeSpan.FromMilliseconds(219);
        loop.Tick();
        Assert.Equal(new[] { 1, 2 }, drawn);
        now = TimeSpan.FromMilliseconds(220);
        loop.Tick();
        Assert.Equal(new[] { 1, 2, 3 }, drawn);
        now = TimeSpan.FromSeconds(2);
        loop.Tick();
        loop.Tick();
        Assert.Equal(3, drawn.Count);
    }

    [Fact]
    public void ReentrantTicksDoNotDuplicateFrames()
    {
        var loop = new AnimationLoop(() => TimeSpan.Zero);
        int count = 0;
        using var track = loop.Start([new(TimeSpan.FromMilliseconds(25), () => { count++; loop.Tick(); })]);
        loop.Tick();
        Assert.Equal(1, count);
    }
    [Fact]
    public void TextDoesNotBurstAfterSlowRenderingAndDoesNotDelayOtherTracks()
    {
        var now = TimeSpan.Zero;
        var loop = new AnimationLoop(() => now);
        int text = 0, spinner = 0;
        using var a = loop.Start([new(TimeSpan.FromMilliseconds(40), () => text++)], repeat: true, catchUp: false);
        using var b = loop.Start([new(TimeSpan.FromMilliseconds(100), () => spinner++)], repeat: true);
        loop.Tick();
        now = TimeSpan.FromMilliseconds(500);
        loop.Tick();
        Assert.Equal(2, text);
        Assert.Equal(6, spinner);
        loop.Tick();
        Assert.Equal(2, text);
        now += TimeSpan.FromMilliseconds(39);
        loop.Tick();
        Assert.Equal(2, text);
        now += TimeSpan.FromMilliseconds(1);
        loop.Tick();
        Assert.Equal(3, text);
    }

    [Fact]
    public void ReadableFramesWaitTheirDurationAfterDrawing()
    {
        var now = TimeSpan.Zero;
        var loop = new AnimationLoop(() => now);
        int count = 0;
        using var track = loop.Start([new(TimeSpan.FromMilliseconds(40), () =>
        {
            count++;
            now += TimeSpan.FromMilliseconds(80);
        })], repeat: true, catchUp: false);
        loop.Tick();
        loop.Tick();
        Assert.Equal(1, count);
        now += TimeSpan.FromMilliseconds(40);
        loop.Tick();
        Assert.Equal(2, count);
    }
}
