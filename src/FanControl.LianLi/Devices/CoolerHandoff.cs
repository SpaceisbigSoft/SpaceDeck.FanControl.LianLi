using System;

namespace FanControl.LianLi.Devices;

/// <summary>
/// What other threads hand one Galahad II Vision LCD's worker: the newest frame for its screen. It is
/// a slot, not a queue: a newer frame replaces the one waiting, so the worker only ever sends the
/// newest, and a frame that has waited longer than <see cref="FrameLifetime"/> is dropped unsent,
/// since a picture shown late is worse than the cooler's own screen. Offering wakes the cooler's loop
/// (through the action the plugin gives it), so a frame does not wait for the next one-second tick.
/// Safe from any thread; it outlives the controller objects, which the plugin builds again on every
/// refresh and reconnect, so it is kept per cooler for the life of the process.
/// </summary>
internal sealed class CoolerHandoff {
    /// <summary>
    /// How long a frame may wait unsent: the collector forwards nothing older, and the cooler shows
    /// its own screen again about 20 seconds after the last frame anyway.
    /// </summary>
    public static readonly TimeSpan FrameLifetime = TimeSpan.FromSeconds(5);

    private readonly object _gate = new object();
    private readonly IClock _clock;

    // Under _gate.
    private byte[]? _frame;
    private DateTime _frameOfferedUtc;
    private Action? _wake;

    /// <summary>A slot timed by <paramref name="clock"/>.</summary>
    public CoolerHandoff(IClock clock) {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Wake the cooler's loop through <paramref name="wake"/> whenever something is offered, or nobody
    /// when null. The plugin sets it each time it starts the worker.
    /// </summary>
    public void WakeWith(Action? wake) {
        lock (_gate) {
            _wake = wake;
        }
    }

    /// <summary>Hand over <paramref name="jpeg"/> as the newest frame, replacing any still waiting.</summary>
    public void OfferFrame(byte[] jpeg) {
        if (jpeg is null) {
            throw new ArgumentNullException(nameof(jpeg));
        }

        Action? wake;
        lock (_gate) {
            _frame = jpeg;
            _frameOfferedUtc = _clock.UtcNow;
            wake = _wake;
        }

        wake?.Invoke();
    }

    /// <summary>
    /// Take the frame waiting, so it is sent once and never again; null when there is none, or when
    /// it has waited longer than <see cref="FrameLifetime"/>, which drops it.
    /// </summary>
    public byte[]? TakeFrame() {
        lock (_gate) {
            byte[]? frame = _frame;
            _frame = null;
            return frame is null || ClockSpan.IsOlderThan(_clock.UtcNow, _frameOfferedUtc, FrameLifetime) ? null : frame;
        }
    }
}
