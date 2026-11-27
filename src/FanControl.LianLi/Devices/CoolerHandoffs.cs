using System;
using System.Collections.Generic;

namespace FanControl.LianLi.Devices;

/// <summary>
/// One <see cref="CoolerHandoff"/> per cooler, by the index its sensors are numbered under
/// (<c>LianLi/{index}</c>), for the life of the process: a cooler's controller is built again on
/// every refresh and reconnect, and whatever was handed to the old one is the new one's to send.
/// Safe from any thread.
/// </summary>
internal sealed class CoolerHandoffs {
    private readonly object _gate = new object();
    private readonly IClock _clock;
    private readonly Dictionary<int, CoolerHandoff> _byIndex = new Dictionary<int, CoolerHandoff>();

    /// <summary>Handoffs timed by <paramref name="clock"/>.</summary>
    public CoolerHandoffs(IClock clock) {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>The handoff of the cooler at <paramref name="index"/>, made the first time it is asked for.</summary>
    public CoolerHandoff For(int index) {
        lock (_gate) {
            if (!_byIndex.TryGetValue(index, out CoolerHandoff? handoff)) {
                handoff = new CoolerHandoff(_clock);
                _byIndex.Add(index, handoff);
            }

            return handoff;
        }
    }
}
