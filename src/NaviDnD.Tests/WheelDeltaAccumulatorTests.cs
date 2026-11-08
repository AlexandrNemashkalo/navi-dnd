using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class WheelDeltaAccumulatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void SmallTouchpadDeltasSurvivePolling(int direction)
    {
        var wheel = new WheelDeltaAccumulator();
        for (int i = 0; i < 3; i++) Assert.Equal(0, wheel.Add(direction * 30, i * 20));
        Assert.Equal(direction, wheel.Add(direction * 30, 60));
    }

    [Fact]
    public void MouseWheelKeepsItsOriginalSpeed()
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(1, wheel.Add(120, 0));
        Assert.Equal(-1, wheel.Add(-120, 10));
        Assert.Equal(3, wheel.Add(360, 20));
    }

    [Fact]
    public void FractionalRemainderIsPreserved()
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(1, wheel.Add(150, 0));
        Assert.Equal(1, wheel.Add(90, 20));
    }

    [Fact]
    public void ReversingDirectionCancelsUnfinishedMovement()
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(0, wheel.Add(90, 0));
        Assert.Equal(0, wheel.Add(-90, 20));
        Assert.Equal(-1, wheel.Add(-120, 40));
    }

    [Fact]
    public void SeparateGesturesDoNotReuseOldRemainder()
    {
        var wheel = new WheelDeltaAccumulator();
        Assert.Equal(0, wheel.Add(90, 0));
        Assert.Equal(0, wheel.Add(30, 301));
        Assert.Equal(1, wheel.Add(90, 320));
    }
}
