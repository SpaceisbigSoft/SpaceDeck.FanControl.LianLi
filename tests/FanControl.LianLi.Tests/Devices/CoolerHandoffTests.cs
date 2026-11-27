using System;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

/// <summary>The screen frame slot: replaced not queued, taken once, dropped after five seconds unsent, and the wake.</summary>
public class CoolerHandoffTests {
    private readonly FakeClock _clock = new FakeClock();

    [Fact]
    public void AnOfferedFrame_IsTakenOnce() {
        var handoff = new CoolerHandoff(_clock);
        byte[] frame = { 0xFF, 0xD8, 0xFF, 0xD9 };

        handoff.OfferFrame(frame);

        Assert.Same(frame, handoff.TakeFrame());
        Assert.Null(handoff.TakeFrame());
    }

    [Fact]
    public void ANewerFrame_ReplacesTheOneWaiting() {
        var handoff = new CoolerHandoff(_clock);
        byte[] newer = { 2 };

        handoff.OfferFrame(new byte[] { 1 });
        handoff.OfferFrame(newer);

        Assert.Same(newer, handoff.TakeFrame());
        Assert.Null(handoff.TakeFrame());
    }

    [Fact]
    public void AFrameThatWaitedFiveSeconds_IsStillSent_AndOneThatWaitedLonger_IsDropped() {
        var handoff = new CoolerHandoff(_clock);
        handoff.OfferFrame(new byte[] { 1 });
        _clock.Advance(CoolerHandoff.FrameLifetime);
        Assert.NotNull(handoff.TakeFrame());

        handoff.OfferFrame(new byte[] { 2 });
        _clock.Advance(CoolerHandoff.FrameLifetime + TimeSpan.FromMilliseconds(1));
        Assert.Null(handoff.TakeFrame());

        // A clock that stepped back since the offer counts the frame as fresh.
        handoff.OfferFrame(new byte[] { 3 });
        _clock.Advance(TimeSpan.FromSeconds(-10));
        Assert.NotNull(handoff.TakeFrame());
    }

    [Fact]
    public void Offering_WakesTheLoop_OnceAWakeIsGiven_AndNoLongerOnceItIsTakenAway() {
        var handoff = new CoolerHandoff(_clock);
        int wakes = 0;

        handoff.OfferFrame(new byte[] { 1 });
        handoff.WakeWith(() => wakes++);
        handoff.OfferFrame(new byte[] { 2 });
        handoff.WakeWith(null);
        handoff.OfferFrame(new byte[] { 3 });

        Assert.Equal(1, wakes);
    }

    [Fact]
    public void NullArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => new CoolerHandoff(null!));
        Assert.Throws<ArgumentNullException>(() => new CoolerHandoff(_clock).OfferFrame(null!));
    }
}
