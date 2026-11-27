using System;
#if ENABLE_LIGHTING
using FanControl.LianLi.Protocol;
#endif

namespace FanControl.LianLi.Devices;

/// <summary>
/// What other threads hand one Galahad II Vision LCD's worker: the newest frame for its screen. It is
/// a slot, not a queue: a newer frame replaces the one waiting, so the worker only ever sends the
/// newest, and a frame that has waited longer than <see cref="FrameLifetime"/> is dropped unsent,
/// since a picture shown late is worse than the cooler's own screen; each frame dropped either way is
/// counted, for the worker's throughput line (<see cref="TakeDroppedFrames"/>). Offering wakes the cooler's loop
/// (through the action the plugin gives it), so a frame does not wait for the next one-second tick.
/// The Lighting build hands the screen ring's newest look over the same way, from the OpenRGB server,
/// and keeps the last one offered for a rebuilt or reconnected cooler to show again, or hands the ring
/// back to L-Connect's look, which the worker then replays as it does after a reconnect.
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
    private int _droppedFrames;
    private Action? _wake;
#if ENABLE_LIGHTING
    private Galahad2RingLook? _look;      // waiting to be sent
    private Galahad2RingLook? _lastLook;  // the newest offered or restored, sent or not
    private bool _handedBack;             // handed back to L-Connect since the last look offered
    private bool _handBackWaiting;        // a hand-back the worker has not replayed yet
#endif

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
            if (_frame != null) {
                _droppedFrames++;
            }

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
            if (frame is null) {
                return null;
            }

            if (ClockSpan.IsOlderThan(_clock.UtcNow, _frameOfferedUtc, FrameLifetime)) {
                _droppedFrames++;
                return null;
            }

            return frame;
        }
    }

    /// <summary>
    /// The frames dropped unsent since the last call - replaced by a newer one while waiting, or
    /// expired - and a count from zero again.
    /// </summary>
    public int TakeDroppedFrames() {
        lock (_gate) {
            int dropped = _droppedFrames;
            _droppedFrames = 0;
            return dropped;
        }
    }
#if ENABLE_LIGHTING

    /// <summary>The newest look offered for the ring, or restored from a saved one; null when none has been.</summary>
    public Galahad2RingLook? LastLook {
        get {
            lock (_gate) {
                return _lastLook;
            }
        }
    }

    /// <summary>Hand over <paramref name="look"/> as the ring's newest, replacing any still waiting.</summary>
    public void OfferLook(Galahad2RingLook look) {
        if (look is null) {
            throw new ArgumentNullException(nameof(look));
        }

        Action? wake;
        lock (_gate) {
            _look = look;
            _lastLook = look;
            _handedBack = false;
            _handBackWaiting = false;
            wake = _wake;
        }

        wake?.Invoke();
    }

    /// <summary>
    /// Keep <paramref name="look"/> (a saved one) as the last look, unless one was offered already this
    /// process, which is newer, or the ring was handed back to L-Connect since, which is too; nothing is
    /// sent until <see cref="OfferLastLook"/>.
    /// </summary>
    public void RestoreLook(Galahad2RingLook look) {
        if (look is null) {
            throw new ArgumentNullException(nameof(look));
        }

        lock (_gate) {
            if (!_handedBack) {
                _lastLook ??= look;
            }
        }
    }

    /// <summary>
    /// Hand the ring back to L-Connect's look: forget the last look and any still waiting, so neither
    /// this cooler nor a rebuilt one shows OpenRGB's again until a new one is offered, and have the
    /// worker replay L-Connect's (<see cref="TakeHandBack"/>).
    /// </summary>
    public void HandBackToLConnect() {
        Action? wake;
        lock (_gate) {
            _look = null;
            _lastLook = null;
            _handedBack = true;
            _handBackWaiting = true;
            wake = _wake;
        }

        wake?.Invoke();
    }

    /// <summary>Whether the ring was handed back to L-Connect since the last call: true once per hand-back.</summary>
    public bool TakeHandBack() {
        lock (_gate) {
            bool waiting = _handBackWaiting;
            _handBackWaiting = false;
            return waiting;
        }
    }

    /// <summary>
    /// Offer the last look again, for a cooler that was rebuilt or came back and may have lost it;
    /// false when there is none, and the ring is someone else's to set.
    /// </summary>
    public bool OfferLastLook() {
        Galahad2RingLook? last = LastLook;
        if (last is null) {
            return false;
        }

        OfferLook(last);
        return true;
    }

    /// <summary>The look waiting to be sent, left waiting; null when none is.</summary>
    public Galahad2RingLook? PeekLook() {
        lock (_gate) {
            return _look;
        }
    }

    /// <summary>
    /// <paramref name="look"/> was taken to be sent: it waits no longer, unless a newer one has replaced
    /// it since it was peeked, which still does.
    /// </summary>
    public void Taken(Galahad2RingLook look) {
        lock (_gate) {
            if (ReferenceEquals(_look, look)) {
                _look = null;
            }
        }
    }
#endif
}
