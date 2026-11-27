#if ENABLE_LIGHTING
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

public class OpenRgbHostingTests {
    private readonly FakeLoopbackListenerApi _api = new FakeLoopbackListenerApi();
    private readonly FakeLogger _log = new FakeLogger();
    private readonly OpenRgbLookFile _looks = new OpenRgbLookFile(Path.Combine(Path.GetTempPath(), "lianli-openrgb-hosting-" + Guid.NewGuid().ToString("N")));

    private OpenRgbHosting NewHosting() => new OpenRgbHosting(_api, _looks, new FakeDeviceCallClock(), _log);

    [Fact]
    public void NoRing_StartsNoServer() {
        Assert.Null(NewHosting().Start(Array.Empty<KeyValuePair<string, CoolerHandoff>>(), "v"));
        Assert.Empty(_api.Ports);
    }

    [Fact]
    public void ARing_StartsAServerOnItsOwnThread() {
        var rings = new[] { new KeyValuePair<string, CoolerHandoff>("LianLi/0", new CoolerHandoff(new FakeClock())) };

        using OpenRgbServer? server = NewHosting().Start(rings, "v");

        Assert.NotNull(server);
        Assert.True(SpinWait.SpinUntil(() => _api.Ports.Count == 1, TimeSpan.FromSeconds(10)));
        Assert.Same(_looks, NewHosting().Looks);
    }

    [Fact]
    public void TheMachinesHosting_IsComposed()
        => Assert.Equal(OpenRgbLookFile.Machine.FilePath, OpenRgbHosting.Machine(_log).Looks.FilePath);

    [Fact]
    public void NullArguments_AreRejected() {
        var clock = new FakeDeviceCallClock();
        Assert.Throws<ArgumentNullException>(() => new OpenRgbHosting(null!, _looks, clock, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbHosting(_api, null!, clock, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbHosting(_api, _looks, null!, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbHosting(_api, _looks, clock, null!));
        Assert.Throws<ArgumentNullException>(() => NewHosting().Start(null!, "v"));
    }
}
#endif
