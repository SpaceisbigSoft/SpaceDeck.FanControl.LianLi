#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.Linq;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.OpenRgb;

/// <summary>
/// Starts an <see cref="OpenRgbServer"/> for the screen rings a plugin object drives, with the process's
/// listener, saved looks' file and clock: the machine's own in the host (<see cref="Machine"/>), fakes in
/// a test.
/// </summary>
internal sealed class OpenRgbHosting {
    private readonly ILoopbackListenerApi _listeners;
    private readonly IDeviceCallClock _clock;
    private readonly ILog _log;

    /// <summary>Create the hosting from its dependencies.</summary>
    public OpenRgbHosting(ILoopbackListenerApi listeners, OpenRgbLookFile looks, IDeviceCallClock clock, ILog log) {
        _listeners = listeners ?? throw new ArgumentNullException(nameof(listeners));
        Looks = looks ?? throw new ArgumentNullException(nameof(looks));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The saved looks, read at each start and written by SAVEMODE.</summary>
    public OpenRgbLookFile Looks { get; }

    /// <summary>The hosting over the machine's own sockets and the plugin's own folder, logging to <paramref name="log"/>.</summary>
    public static OpenRgbHosting Machine(ILog log)
        => new OpenRgbHosting(WindowsLoopbackListenerApi.Instance, OpenRgbLookFile.Machine, StopwatchDeviceCallClock.Instance, log);

    /// <summary>
    /// Start the server for <paramref name="rings"/>, each cooler's id and handoff, by OpenRGB index, as
    /// plugin <paramref name="version"/>. Null when there is no ring, in which case nothing is started.
    /// </summary>
    public OpenRgbServer? Start(IReadOnlyList<KeyValuePair<string, CoolerHandoff>> rings, string version) {
        if (rings is null) {
            throw new ArgumentNullException(nameof(rings));
        }

        if (rings.Count == 0) {
            return null;
        }

        var server = new OpenRgbServer(
            rings.Select(ring => new OpenRgbRing(ring.Key, ring.Value)).ToArray(), Looks, version, _listeners, _clock, _log);
        server.Start();
        return server;
    }
}
#endif
