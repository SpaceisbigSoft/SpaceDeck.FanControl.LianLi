using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.SpaceDeck;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>Starting a publisher for what a plugin object registered, and the version it reports.</summary>
public class SpaceDeckPublicationTests {
    private readonly FakeNamedPipeApi _pipe = new FakeNamedPipeApi();
    private readonly FakeServiceControlApi _services = new FakeServiceControlApi();
    private readonly FakeDeviceCallRunner _runner = new FakeDeviceCallRunner();
    private readonly FakeDeviceCallClock _clock = new FakeDeviceCallClock();
    private readonly FakeLogger _log = new FakeLogger();

    private SpaceDeckPublication NewPublication() => new SpaceDeckPublication(_pipe, _services, _runner, _clock, _log);

    [Fact]
    public void NothingRegistered_StartsNothing() {
        Assert.Null(NewPublication().Start("Lian Li Uni", Guid.NewGuid(), new FakeSensorsContainer()));

        Assert.Empty(_services.Queries);
    }

    [Fact]
    public void ARegisteredReading_StartsAPublisherOnItsOwnThread() {
        _services.ProcessId = null;
        var container = new FakeSensorsContainer();
        container.FanSensors.Add(new FakePluginSensor("LianLi/0/ch0/fan", "Fan", 900f));

        using SpaceDeckPublisher? publisher = NewPublication().Start("Lian Li Uni", Guid.NewGuid(), container);

        Assert.NotNull(publisher);
        Assert.True(SpinWait.SpinUntil(() => publisher.HasStopped, TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { SpaceDeckPublisher.ServiceName }, _services.Queries);
    }

    [Fact]
    public void AScreen_IsInTheCatalogOfThePublisherStarted() {
        _services.ProcessId = null;
        var container = new FakeSensorsContainer();
        container.FanSensors.Add(new FakePluginSensor("LianLi/0/ch1/fan", "Lian Li GA II LCD Pump RPM", 2800f));

        using SpaceDeckPublisher? publisher = NewPublication().Start("Lian Li Uni", Guid.NewGuid(), container, new[] { "LianLi/0" });

        Assert.NotNull(publisher);
        Assert.True(SpinWait.SpinUntil(() => publisher.HasStopped, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void NoScreen_StartsNoScreenClient()
        => Assert.Null(NewPublication().StartScreens(new Dictionary<string, CoolerHandoff>()));

    [Fact]
    public void AScreen_StartsAScreenClientOnItsOwnThread() {
        _services.ProcessId = null;
        var screens = new Dictionary<string, CoolerHandoff> { ["LianLi/0"] = new CoolerHandoff(new FakeClock()) };

        using SpaceDeckScreenClient? client = NewPublication().StartScreens(screens);

        Assert.NotNull(client);
        Assert.True(SpinWait.SpinUntil(() => client.HasStopped, TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { SpaceDeckPublisher.ServiceName }, _services.Queries);
    }

    [Fact]
    public void TheVersion_IsTheAssemblysInformationalVersion() {
        Assembly plugin = typeof(SpaceDeckPublication).Assembly;

        Assert.Equal(
            plugin.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            SpaceDeckPublication.VersionOf(plugin));
    }

    [Fact]
    public void AnAssemblyWithoutAnInformationalVersion_IsUnknown()
        => Assert.Equal(
            "unknown",
            SpaceDeckPublication.VersionOf(AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SpaceDeckVersionless"), AssemblyBuilderAccess.Run)));

    [Fact]
    public void TheMachinesPublication_IsComposed()
        => Assert.NotNull(SpaceDeckPublication.Machine(_log));

    [Fact]
    public void NullArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => SpaceDeckPublication.VersionOf(null!));
        Assert.Throws<ArgumentNullException>(() => NewPublication().StartScreens(null!));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublication(null!, _services, _runner, _clock, _log));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublication(_pipe, null!, _runner, _clock, _log));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublication(_pipe, _services, null!, _clock, _log));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublication(_pipe, _services, _runner, null!, _log));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckPublication(_pipe, _services, _runner, _clock, null!));
    }
}
