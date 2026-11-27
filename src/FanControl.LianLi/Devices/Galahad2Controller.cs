using System;
using System.Collections.Generic;
using System.Globalization;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.Devices;

/// <summary>
/// Coordinates one Galahad II Trinity AIO (vendor 0x0416): channel 0 is the fan, channel 1 the
/// pump. Like <see cref="FanController"/> the FanControl-thread methods only mutate locked
/// in-memory state and every USB transfer happens on the worker-thread methods, but the 0x0416
/// family writes a command packet and reads its reply instead of pulling an input report: a
/// keepalive write per channel, and an RPM poll that writes a handshake and reads to the answer. The
/// pump duty is floored in <see cref="Galahad2Protocol"/> so a curve can never stop the pump. On a
/// Galahad II Vision LCD the same answer carries the coolant temperature, published as an
/// <see cref="ITemperatureSource"/>; every other cooler of the family reports no temperature. On a
/// cooler whose pump follows the motherboard header (<see cref="Galahad2PumpSourceDecision"/>) the
/// pump channel is not a control: the plugin keeps re-sending the command that hands the pump to the
/// header, on the same schedule as a duty, and still reports the pump's RPM. Every speed reading is
/// reported through <see cref="IFanSpeedSource"/>, under the channels' RPM identities, so the pump's
/// stays when its control does not. A Vision LCD is also given a <see cref="CoolerHandoff"/>, through
/// which SpaceDeck's screen frames reach it: the worker sends the newest waiting frame after the
/// duties and before the status poll, at most thirty a second on average, and since a frame wakes the loop, a
/// cooler with a handoff polls its status at most every half second however often it is woken. Once
/// a minute while frames go out it logs what they cost (<see cref="ScreenFrameTally"/>).
/// </summary>
internal sealed class Galahad2Controller : IFanDevice, ITemperatureSource, IFanSpeedSource {
    private const int FanChannel = 0;
    private const int PumpChannel = 1;
    private const int Channels = 2;
    private const int CoolantTemperatures = 1;

    // The pump target of a cooler whose pump follows the motherboard header: the duty the
    // follow-motherboard command carries. It never changes, so the keepalive decision re-sends the
    // command on its refresh interval, and a reconnect, which forgets the last write, re-sends it at once.
    private const int MotherboardPumpTarget = 100;

    // The 0x0416 handshake reply is one 64-byte command-packet frame.
    private const int HandshakeReplyLength = 64;

    // The transport empties the input queue before the handshake is written, so a poll reads past
    // only what arrives after that and before the cooler's answer: another program's reply, or a packet
    // the Vision and HydroShift LCD send unasked (L-Connect's own reader for those takes every packet
    // and keeps the ones that echo the handshake). Up to this many; L-Connect asks once a second and
    // no more, so there is no second ask.
    private const int MaxReportsPerHandshake = 32;

    // The longest a poll keeps reading for the handshake's answer. L-Connect waits 200 ms for the
    // Trinity's one answer (Galahad2TrinityDevice.ReadTimeout) and reads the Vision and HydroShift LCD
    // in 20 ms slices (ReadACommandPacket(20)) on a reader of its own, but on every one of them it asks
    // again each second, so an answer later than a second is overtaken by the next ask there too.
    private static readonly TimeSpan HandshakeTimeLimit = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The most frames a second sent to one screen on average: enough for smooth motion, and what
    /// SpaceDeck's screen pipe promises at most; a frame without a token waits, replaced by any newer one.
    /// </summary>
    public const int MostFramesPerSecond = 30;

    /// <summary>
    /// The frames sent one after another after a quiet spell: the size of the screen's token bucket,
    /// refilled at <see cref="MostFramesPerSecond"/>. A tenth of a second of the refill, so a frame that
    /// comes a timer tick late and the next on time are both sent, where a strict thirtieth-of-a-second
    /// gap held the second back until a newer one replaced it.
    /// </summary>
    public const int FrameBurst = 3;

    // The bucket counts in ticks' worth of refill: a frame costs a second's ticks and every tick of the
    // clock adds MostFramesPerSecond, so a thirtieth of a second is not rounded to whole ticks.
    private const long FrameCost = TimeSpan.TicksPerSecond;
    private const long FrameCreditLimit = FrameBurst * FrameCost;

    // A cooler woken for each frame still polls its status no more than twice a second: L-Connect
    // asks once a second, and nothing shows that a cooler wants to be asked more often.
    private static readonly TimeSpan MinimumHandshakeGap = TimeSpan.FromMilliseconds(500);
#if ENABLE_LIGHTING

    // The ring's built-in effects need a moment between 0x83 writes, or the ring can keep showing an
    // older one (register note), and a direct look is sent at most ten times a second: enough for an
    // OpenRGB effect to move, and well inside what the screen's B packets already carry.
    private static readonly TimeSpan MinimumEffectGap = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumDirectGap = TimeSpan.FromMilliseconds(100);
#endif

    // The name the cooler's sensors and controls start with. A Galahad II Vision LCD is named as Lian
    // Li sells it, "GA II LCD": every variant (280, 360, SL-INF, TL Wireless) reports the same product
    // ids, so the model is as far as the name can go, and it carries no number because the ids, not
    // the names, tell two coolers apart. Every other Galahad keeps the numbered name it has always had.
    private const string VisionName = "Lian Li GA II LCD";

    private readonly int _index;
    private readonly string _name;
    private readonly IDeviceTransport _transport;
    private readonly bool _reportsCoolant;     // a Vision LCD, whose handshake reply carries the coolant
    private readonly bool _pumpFollowsMotherboard; // the pump is handed to the motherboard header
    private readonly IClock _clock;
    private readonly ILog _log;
    private readonly CoolerHandoff? _handoff;      // a Vision LCD's screen frames; null on every other cooler

    private readonly object _lock = new object();
    private readonly int[] _target = { -1, -1 };          // commanded duty %, -1 = unassigned
    private readonly int[] _lastWritten = { -2, -2 };     // last duty written
    private readonly DateTime[] _lastWriteUtc = { DateTime.MinValue, DateTime.MinValue };
    private readonly float?[] _rpm = new float?[2];       // last measured RPM, null until one is read
    private readonly bool[] _rpmImplausible = { false, false }; // last read rejected as garbage
    private float? _coolant;                              // last coolant temperature, null when not measured

    // The transport generation this cooler was last set up under. A newer one means the transport
    // lost its handle to the device after that; the cooler needs no setup writes of its own, but a
    // re-enumerated one may have been power-cycled and lost its saved look, so once the transport
    // has reopened it, that is replayed and both duties re-sent before the next write.
    // Worker-thread only; the replay is registered before the worker starts.
    private int _setUpGeneration;
    private readonly ReconnectReplay _reconnectReplay;

    // Whether the last handshake went unanswered, so the log says it once, and whether any has been
    // answered, so the line when one is says which. Worker only.
    private bool _handshakeMissing;
    private bool _handshakeAnswered;

    // When the last handshake went out; the screen's token bucket and when it was last refilled; and
    // whether the last frame failed and any has been shown, so the log says each once. Worker only.
    private DateTime? _lastHandshakeUtc;
    private long _frameCredit = FrameCreditLimit;
    private DateTime? _frameCreditUtc;
    private bool _framesFailing;
    private bool _framesShown;
    private readonly ScreenFrameTally _frameTally = new ScreenFrameTally();
#if ENABLE_LIGHTING
    private DateTime? _lastEffectUtc;
    private DateTime? _lastDirectUtc;
    private bool _ringFailing;
#endif

    public Galahad2Controller(int index, IDeviceTransport transport, int productId, IClock clock, ILog log)
        : this(index, transport, productId, clock, log, null) {
    }

    /// <summary>
    /// A cooler that also takes screen frames from <paramref name="handoff"/>; null for a cooler
    /// without a screen, or with nothing to show on it.
    /// </summary>
    public Galahad2Controller(int index, IDeviceTransport transport, int productId, IClock clock, ILog log, CoolerHandoff? handoff) {
        _index = index;
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _reportsCoolant = Galahad2Protocol.ReportsCoolant(productId);
        // The coolers that report the coolant are exactly the Vision LCD's two product ids.
        _name = _reportsCoolant
            ? VisionName
            : string.Format(CultureInfo.InvariantCulture, "Lian Li Galahad #{0}", index + 1);
        _pumpFollowsMotherboard = Galahad2PumpSourceDecision.FollowsMotherboard(productId);
        if (_pumpFollowsMotherboard) {
            _target[PumpChannel] = MotherboardPumpTarget;
        }

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _handoff = handoff;
        _setUpGeneration = _transport.Generation;
        _reconnectReplay = new ReconnectReplay(_clock);
    }

    /// <summary>The Galahad exposes two channels: the fan and the pump.</summary>
    public int ChannelCount => Channels;

    /// <summary>
    /// The Galahad's two channels - fan and pump - are both physical parts of the AIO, so both are
    /// populated, except that a pump which follows the motherboard header is not shown as a control:
    /// FanControl drives it through the header's own control. Its RPM is still reported.
    /// </summary>
    public bool IsChannelPopulated(int channel) => !IsMotherboardPump(channel);

    /// <summary>One speed reading per channel, the fan's and the pump's, whether or not the channel is a control.</summary>
    public int FanSpeedCount => Channels;

    /// <summary>
    /// The sensor identity for a Galahad channel - fan (0) or pump (1). The ids share the stable
    /// LianLi/{index}/ch{channel} scheme so a saved curve binding survives a restart; only the names
    /// depend on the model (a binding keys on the id, so renaming never orphans it).
    /// </summary>
    public ChannelDescriptor Describe(int channel) {
        string role = channel == PumpChannel ? "Pump" : "Fan";
        return new ChannelDescriptor(
            $"LianLi/{_index}/ch{channel}/ctl",
            $"{_name} {role}",
            $"LianLi/{_index}/ch{channel}/fan",
            $"{_name} {role} RPM");
    }

    /// <summary>
    /// The identity of a channel's speed reading: the channel's own RPM identity, so a reading keeps
    /// the id and name it has always had.
    /// </summary>
    public FanSpeedDescriptor DescribeFanSpeed(int index) {
        ChannelDescriptor channel = Describe(index);
        return new FanSpeedDescriptor(channel.RpmId, channel.RpmName);
    }

    /// <summary>
    /// A Galahad II Vision LCD reports one temperature, the coolant's; the Trinity and the HydroShift
    /// LCD report none, so no temperature sensor is registered for them.
    /// </summary>
    public int TemperatureCount => _reportsCoolant ? CoolantTemperatures : 0;

    /// <summary>
    /// The sensor identity for the coolant temperature, keyed on the controller's index like the
    /// fan and pump and named as the HydroShift II OLED Curve names its own.
    /// </summary>
    public TemperatureDescriptor DescribeTemperature(int index) {
        return new TemperatureDescriptor(
            $"LianLi/{_index}/coolant/temp",
            $"{_name} Coolant");
    }

    /// <inheritdoc />
    public void ReplayOnReconnect(Func<bool> replay) => _reconnectReplay.Register(replay);

    // ---------- FanControl-thread methods (no I/O) ----------

    /// <summary>
    /// Set the commanded duty for a channel. The worker pushes it to hardware. A pump that follows
    /// the motherboard header keeps following it: a duty for it (from a control remembered from a
    /// build that had one) is ignored.
    /// </summary>
    public void SetTarget(int channel, int duty) {
        if (IsMotherboardPump(channel)) {
            return;
        }

        lock (_lock) {
            _target[channel] = duty;
        }
    }

    /// <summary>
    /// Release a channel so the keepalive stops asserting it (used by Reset). A pump that follows the
    /// motherboard header is never released, so the command that hands it to the header keeps going out.
    /// </summary>
    public void ReleaseChannel(int channel) {
        if (IsMotherboardPump(channel)) {
            return;
        }

        lock (_lock) {
            _target[channel] = -1;
        }
    }

    /// <summary>Read the last measured RPM for a channel, or null until one is read.</summary>
    public float? GetRpm(int channel) {
        lock (_lock) {
            return _rpm[channel];
        }
    }

    /// <inheritdoc />
    public float? GetFanSpeed(int index) => GetRpm(index);

    /// <inheritdoc />
    public float? GetTemperature(int index) {
        lock (_lock) {
            return _coolant;
        }
    }

    // ---------- worker-thread I/O (the only place HID is touched) ----------

    /// <summary>Push any changed-or-stale channel targets to the hardware.</summary>
    public void ApplyPending() {
        ReplaySetupIfReconnected();

        for (int ch = 0; ch < Channels; ch++) {
            int target;
            int lastWritten;
            DateTime lastWrite;
            lock (_lock) {
                target = _target[ch];
                lastWritten = _lastWritten[ch];
                lastWrite = _lastWriteUtc[ch];
            }

            if (!ChannelWriteDecision.ShouldWrite(
                    target, lastWritten, lastWrite, _clock.UtcNow, ChannelWriteDecision.RefreshInterval)) {
                continue;
            }

            WriteDuty(ch, target);

            DateTime writtenAt = _clock.UtcNow;
            lock (_lock) {
                _lastWritten[ch] = target;
                _lastWriteUtc[ch] = writtenAt;
            }

            // Only the first speed after the controller is set up (at start or after a reconnect) is
            // logged, to show it is being driven. Each later change and the 15-second keepalive
            // re-send are not: a curve moves the speed hundreds of times a day.
            if (lastWritten < 0 && IsMotherboardPump(ch)) {
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture, "Set G{0}:pump to follow the motherboard's PWM header", _index));
            } else if (lastWritten < 0) {
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture,
                    "Set G{0}:{1} = {2}%",
                    _index,
                    ch == PumpChannel ? "pump" : "fan",
                    target));
            }
        }

        SendScreenFrame();
        LogScreenFrameTally();
#if ENABLE_LIGHTING
        SendRingLook();
#endif
    }

    /// <inheritdoc />
    public void ForgetReadings() {
        lock (_lock) {
            for (int ch = 0; ch < Channels; ch++) {
                _rpm[ch] = null;
            }

            _coolant = null;
        }
    }

    /// <summary>
    /// Write a handshake and decode the reply into the fan and pump RPM cache and, on a Vision LCD,
    /// the coolant temperature. A reply that answers without a coolant reading (its valid flag clear)
    /// leaves the coolant not measured rather than at its last value.
    /// </summary>
    public void PollRpm() {
        if (HandshakeIsRecent()) {
            return;
        }

        // Without an answer both readings stay as they were, as L-Connect leaves them.
        if (Handshake() is not byte[] reply) {
            return;
        }

        Galahad2Reading reading = Galahad2Protocol.DecodeHandshake(reply);

        List<string>? transitions = null;
        lock (_lock) {
            UpdateRpm(FanChannel, reading.FanRpm, ref transitions);
            UpdateRpm(PumpChannel, reading.PumpRpm, ref transitions);
            if (_reportsCoolant) {
                _coolant = reading.CoolantTemperature;
            }
        }

        if (transitions != null) {
            foreach (string line in transitions) {
                _log.Write(line);
            }
        }
    }

    public void Dispose() {
        _transport.Dispose();
    }

    // Write a handshake and read to the reply that answers it, past whatever else arrives after the
    // write. Gives up once HandshakeTimeLimit has passed as well as after MaxReportsPerHandshake
    // reports, so a stream of other reports cannot hold the tick. Null when none answers it.
    private byte[]? Handshake() {
        DateTime started = _clock.UtcNow;
        _lastHandshakeUtc = started;
        _transport.Write(Galahad2Protocol.EncodeHandshakeRequest());
        int reads = 0;
        byte lastCommand = 0;
        for (int read = 0; read < MaxReportsPerHandshake && !HandshakeTimedOut(read, started); read++) {
            byte[] reply = _transport.Read(HandshakeReplyLength);
            reads++;
            if (Galahad2Protocol.IsHandshakeReply(reply)) {
                RecordHandshake(answered: true, reads, lastCommand);
                return reply;
            }

            lastCommand = CommandPacket.CommandOf(reply);
        }

        RecordHandshake(answered: false, reads, lastCommand);
        return null;
    }

    // A cooler with a handoff is woken for every frame it is handed, and the status is not asked for
    // on each of those ticks. A clock that stepped back counts as long ago, so the poll goes ahead.
    private bool HandshakeIsRecent()
        => _handoff != null
            && _lastHandshakeUtc is DateTime last
            && ClockSpan.Since(_clock.UtcNow, last) < MinimumHandshakeGap;

    // Send the newest frame waiting for the screen, between two status polls, as 0x0E SendJpeg in
    // B packets, and never again: the handoff gives a frame out once, and a tick sends at most one. A
    // frame that arrives while the screen's token bucket is empty stays waiting for a later tick - the
    // next frame's wake, or the one-second tick - replaced by any newer one (and is dropped if it waits
    // five seconds); one that arrives while the cooler is off the bus waits the same way, so a frame is
    // never the write that reopens the cooler. Nothing reads the cooler's one B-packet echo: every
    // transport write starts by discarding the input queued for its handle (IDeviceTransport.Write),
    // so the next packet's write or the next handshake's drops an echo that has already arrived, and
    // the handshake reads past one that arrives later, since it takes only a reply echoing 0x81.
    // Reading the echo here instead would cost the worker a read timeout whenever the cooler sent
    // none. A write that fails costs only this frame - the next complete one puts the screen right -
    // and is logged once until a frame goes out again; the duties and the poll go on regardless.
    private void SendScreenFrame() {
        if (_handoff is null || _transport.IsFaulted) {
            return;
        }

        DateTime now = _clock.UtcNow;
        if (!HasFrameToken(now)) {
            return;
        }

        if (_handoff.TakeFrame() is not byte[] jpeg) {
            return;
        }

        _frameCredit -= FrameCost;
        IReadOnlyList<byte[]> packets = Galahad2ScreenEncoder.EncodeJpeg(jpeg);
        try {
            foreach (byte[] packet in packets) {
                _transport.Write(packet);
            }

            _frameTally.Sent(jpeg.Length, now, _clock.UtcNow);
        }
#pragma warning disable CA1031 // opt-in feature: a screen frame that cannot be written costs only that frame, never the duties or the status poll
        catch (Exception ex) {
#pragma warning restore CA1031
            if (!_framesFailing) {
                _framesFailing = true;
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture,
                    "G{0}: a screen frame could not be sent, so it was dropped (logged once until one is sent): {1}",
                    _index,
                    ex.Message));
            }

            return;
        }

        if (_framesFailing || !_framesShown) {
            _log.Write(string.Format(
                CultureInfo.InvariantCulture,
                _framesFailing ? "G{0}: screen frames are sent again ({1} bytes in {2} packets)" : "G{0}: showing SpaceDeck's screen frames ({1} bytes in {2} packets)",
                _index,
                jpeg.Length,
                packets.Count));
            _framesFailing = false;
            _framesShown = true;
        }
    }

    // Refill the screen's token bucket for the time since it was last refilled, and whether it holds a
    // frame's worth. A second refills more than the whole bucket, so the time is counted up to a second,
    // and a clock that stepped back counts as long ago (ClockSpan.Since), which fills it: a frame is
    // never held back by a clock correction.
    private bool HasFrameToken(DateTime now) {
        if (_frameCreditUtc is DateTime then) {
            long elapsed = Math.Min(ClockSpan.Since(now, then).Ticks, TimeSpan.TicksPerSecond);
            _frameCredit = Math.Min(FrameCreditLimit, _frameCredit + (elapsed * MostFramesPerSecond));
        }

        _frameCreditUtc = now;
        return _frameCredit >= FrameCost;
    }

    // Once a minute while frames go out, one line of what they cost, with the frames the handoff
    // dropped meanwhile; a screen nobody draws on logs nothing.
    private void LogScreenFrameTally() {
        if (_handoff is null || !_frameTally.IsDue(_clock.UtcNow)) {
            return;
        }

        _log.Write(_frameTally.Close(_index, _handoff.TakeDroppedFrames()));
    }

#if ENABLE_LIGHTING
    // Send the ring's newest look handed over by the OpenRGB server, between two status polls: a
    // direct look as 0x14, at most ten a second, an effect as 0x83, at least a second apart. One that
    // is not due yet stays waiting, and a newer one replaces it there, so the ring always ends on the
    // newest. Like a frame, it waits while the cooler is off the bus, and a write that fails costs only
    // that look and is logged once until one is sent again. A ring handed back to L-Connect has its
    // look replayed as after a reconnect - by the next tick's replay, which the Lighting build
    // registered and which shows L-Connect's look now that the handoff holds none of OpenRGB's.
    private void SendRingLook() {
        if (_handoff is not null && _handoff.TakeHandBack()) {
            _reconnectReplay.Owe();
            _log.Write(string.Format(CultureInfo.InvariantCulture, "G{0}: the screen ring is handed back to L-Connect's look", _index));
        }

        if (_handoff is null || _transport.IsFaulted || _handoff.PeekLook() is not Galahad2RingLook look) {
            return;
        }

        bool direct = look.Mode == Galahad2RingMode.Direct;
        DateTime now = _clock.UtcNow;
        DateTime? last = direct ? _lastDirectUtc : _lastEffectUtc;
        if (last is DateTime then && ClockSpan.Since(now, then) < (direct ? MinimumDirectGap : MinimumEffectGap)) {
            return;
        }

        _handoff.Taken(look);
        if (direct) {
            _lastDirectUtc = now;
        } else {
            _lastEffectUtc = now;
        }

        try {
            foreach (byte[] packet in Galahad2RingEncoder.Encode(look)) {
                _transport.Write(packet);
            }
        }
#pragma warning disable CA1031 // opt-in feature: a ring look that cannot be written costs only that look, never the duties or the status poll
        catch (Exception ex) {
#pragma warning restore CA1031
            if (!_ringFailing) {
                _ringFailing = true;
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture,
                    "G{0}: the screen ring's {1} look could not be sent (logged once until one is sent): {2}",
                    _index,
                    look.Mode,
                    ex.Message));
            }

            return;
        }

        if (_ringFailing) {
            _ringFailing = false;
            _log.Write(string.Format(CultureInfo.InvariantCulture, "G{0}: the screen ring's looks are sent again", _index));
        }
    }

#endif
    // The first read always goes ahead, so a slow write never costs the poll its answer; every later
    // read waits on the limit. A clock that stepped back counts as within it, and the report count
    // still bounds the poll.
    private bool HandshakeTimedOut(int read, DateTime started)
        => read > 0 && ClockSpan.IsOlderThan(_clock.UtcNow, started, HandshakeTimeLimit);

    // A cooler that stops answering the handshake tends to stay that way for a while, so the log says
    // so on the change only: once when it stops, with how far the poll got and the command the last
    // report read echoed, and once when it answers - again, or for the first time when it never had.
    private void RecordHandshake(bool answered, int reads, byte lastCommand) {
        if (answered) {
            bool answeredBefore = _handshakeAnswered;
            _handshakeAnswered = true;
            if (_handshakeMissing) {
                _handshakeMissing = false;
                _log.Write(string.Format(
                    CultureInfo.InvariantCulture,
                    answeredBefore ? "G{0}: the cooler answers the handshake again" : "G{0}: the cooler answers the handshake for the first time",
                    _index));
            }

            return;
        }

        if (_handshakeMissing) {
            return;
        }

        _handshakeMissing = true;
        _log.Write(string.Format(
            CultureInfo.InvariantCulture,
            "G{0}: the cooler did not answer the handshake ({1} report(s) read, the last echoing command 0x{2:X2}); RPM left as it was (logged once until it does)",
            _index,
            reads,
            lastCommand));
    }

    // A reopened transport means the cooler was re-enumerated and may have come back reset. Nothing
    // is replayed while the handle is still faulted: the cooler is off the bus, and the next poll's
    // handshake is what reopens it, on the backoff. Once it is back, replay the saved look (if the
    // Lighting build registered one) and forget every last-written duty so the loop that follows
    // re-sends the fan and pump now. A throw leaves the generation unrecorded, so the replay is
    // retried on the next tick; a look that reports failure stays owed and is tried again on its own.
    private void ReplaySetupIfReconnected() {
        if (_transport.IsFaulted) {
            return;
        }

        int generation = _transport.Generation;
        if (generation == _setUpGeneration) {
            _reconnectReplay.Apply();
            return;
        }

        _reconnectReplay.Owe();
        _reconnectReplay.Apply();
        lock (_lock) {
            for (int ch = 0; ch < Channels; ch++) {
                _lastWritten[ch] = -2;
            }
        }

        _setUpGeneration = generation;
        _log.Write(string.Format(
            CultureInfo.InvariantCulture,
            "G{0} reconnected: setup replayed (transport generation {1})",
            _index,
            generation));
    }

    private void WriteDuty(int channel, int duty) {
        // Fan and pump take distinct commands; the pump duty is floored inside the encoder so a
        // FanControl curve can never command a stopped pump on an AIO. A pump that follows the
        // motherboard header gets the command that hands it to the header instead of a duty.
        byte[] command;
        if (IsMotherboardPump(channel)) {
            command = Galahad2Protocol.EncodeFollowMotherboardPump();
        } else if (channel == PumpChannel) {
            command = Galahad2Protocol.EncodeSetPump(motherboardSync: false, dutyPercent: duty);
        } else {
            command = Galahad2Protocol.EncodeSetFan(motherboardSync: false, dutyPercent: duty);
        }

        _transport.Write(command);
    }

    private bool IsMotherboardPump(int channel) => _pumpFollowsMotherboard && channel == PumpChannel;

    // Caller holds _lock. Mirrors FanController: cache plausible readings, keep the last good value
    // on a garbage read, and log each onset/recovery transition once.
    private void UpdateRpm(int channel, int rpm, ref List<string>? transitions) {
        if (ChannelReadDecision.IsPlausible(rpm)) {
            _rpm[channel] = rpm;
            if (_rpmImplausible[channel]) {
                _rpmImplausible[channel] = false;
                (transitions ??= new List<string>()).Add(string.Format(
                    CultureInfo.InvariantCulture, "G{0}:{1} rpm recovered ({2})", _index, channel, rpm));
            }
        } else if (!_rpmImplausible[channel]) {
            _rpmImplausible[channel] = true;
            (transitions ??= new List<string>()).Add(string.Format(
                CultureInfo.InvariantCulture, "G{0}:{1} implausible rpm {2} ignored, keeping {3}", _index, channel, rpm, ChannelReadDecision.Describe(_rpm[channel])));
        }
    }
}
