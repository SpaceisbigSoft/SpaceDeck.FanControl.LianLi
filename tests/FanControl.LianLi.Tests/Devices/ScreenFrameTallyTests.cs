using System;
using FanControl.LianLi.Devices;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

/// <summary>The screen's throughput line: due a minute after the first frame since the last line, its numbers, and a fresh start.</summary>
public sealed class ScreenFrameTallyTests {
    private static readonly DateTime Then = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NothingIsDue_UntilAFrameIsSent_NorUntilAMinuteAfterIt() {
        var tally = new ScreenFrameTally();
        Assert.False(tally.IsDue(Then.AddHours(1)));

        tally.Sent(1000, Then, Then.AddMilliseconds(20));

        Assert.False(tally.IsDue(Then.AddSeconds(59)));
        Assert.True(tally.IsDue(Then + ScreenFrameTally.Interval));
        Assert.Equal(TimeSpan.FromMinutes(1), ScreenFrameTally.Interval);
    }

    [Fact]
    public void TheLine_CountsTheFrames_TheDrops_TheWriteTimes_AndTheAverageSize() {
        var tally = new ScreenFrameTally();
        tally.Sent(30_000, Then, Then.AddMilliseconds(40));
        tally.Sent(20_000, Then.AddSeconds(1), Then.AddSeconds(1).AddMilliseconds(25));
        tally.Sent(10_001, Then.AddSeconds(2), Then.AddSeconds(2).AddMilliseconds(10));

        Assert.Equal(
            "G2: screen frames in the last minute: 3 sent, 7 dropped before sending (replaced by a newer one or expired), 25.0 ms average and 40.0 ms longest to write one, 20000 bytes average",
            tally.Close(2, 7));
    }

    [Fact]
    public void Closing_StartsAFreshMinute_FromTheNextFrame() {
        var tally = new ScreenFrameTally();
        tally.Sent(5000, Then, Then.AddMilliseconds(50));
        _ = tally.Close(0, 0);
        Assert.False(tally.IsDue(Then.AddHours(1)));

        tally.Sent(100, Then.AddMinutes(5), Then.AddMinutes(5).AddMilliseconds(2));

        Assert.False(tally.IsDue(Then.AddMinutes(5).AddSeconds(59)));
        Assert.True(tally.IsDue(Then.AddMinutes(6)));
        Assert.Equal(
            "G0: screen frames in the last minute: 1 sent, 0 dropped before sending (replaced by a newer one or expired), 2.0 ms average and 2.0 ms longest to write one, 100 bytes average",
            tally.Close(0, 0));
    }

    [Fact]
    public void AClockThatSteppedBack_CountsAWriteAsInstant_AndTheLineAsDue() {
        var tally = new ScreenFrameTally();
        tally.Sent(100, Then, Then.AddMilliseconds(-5));

        Assert.True(tally.IsDue(Then.AddSeconds(-1)));
        Assert.Equal(
            "G0: screen frames in the last minute: 1 sent, 0 dropped before sending (replaced by a newer one or expired), 0.0 ms average and 0.0 ms longest to write one, 100 bytes average",
            tally.Close(0, 0));
    }
}
