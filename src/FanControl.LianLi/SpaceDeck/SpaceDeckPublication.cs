using System;
using System.Collections.Generic;
using System.Reflection;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Transport;
using FanControl.Plugins;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// Starts a <see cref="SpaceDeckPublisher"/> for what a plugin object registered, and a
/// <see cref="SpaceDeckScreenClient"/> for the coolers' screens it drives, with the process's
/// pipe, service control, bounded-call and clock dependencies: the machine's own in the host
/// (<see cref="Machine"/>), fakes in a test.
/// </summary>
internal sealed class SpaceDeckPublication {
    private readonly INamedPipeApi _pipe;
    private readonly IServiceControlApi _services;
    private readonly IDeviceCallRunner _runner;
    private readonly IDeviceCallClock _clock;
    private readonly ILog _log;

    /// <summary>Create the publication from its dependencies.</summary>
    public SpaceDeckPublication(INamedPipeApi pipe, IServiceControlApi services, IDeviceCallRunner runner, IDeviceCallClock clock, ILog log) {
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The publication over Windows' own pipe and service calls, logging to <paramref name="log"/>.</summary>
    public static SpaceDeckPublication Machine(ILog log)
        => new SpaceDeckPublication(
            WindowsNamedPipeApi.Instance, WindowsServiceControlApi.Instance, new BoundedDeviceCallRunner(log), StopwatchDeviceCallClock.Instance, log);

    /// <summary>
    /// Start publishing the readings <paramref name="container"/> holds, as the plugin
    /// <paramref name="plugin"/> (its FanControl Name) whose object is <paramref name="instance"/>.
    /// Null when there is no reading to send, in which case nothing is started.
    /// </summary>
    public SpaceDeckPublisher? Start(string plugin, Guid instance, IPluginSensorsContainer container)
        => Start(plugin, instance, container, Array.Empty<string>());

    /// <summary>
    /// Start publishing as <see cref="Start(string, Guid, IPluginSensorsContainer)"/> does, with a
    /// screen on each device whose id is in <paramref name="screens"/>.
    /// </summary>
    public SpaceDeckPublisher? Start(string plugin, Guid instance, IPluginSensorsContainer container, IEnumerable<string> screens) {
        SpaceDeckReadings readings = SpaceDeckReadings.From(container, screens, _log);
        if (readings.Catalog.Sensors.Count == 0) {
            return null;
        }

        var publisher = new SpaceDeckPublisher(
            readings, _pipe, _services, _runner, _clock, _log, plugin, VersionOf(typeof(SpaceDeckPublication).Assembly), instance);
        publisher.Start();
        return publisher;
    }

    /// <summary>
    /// Start reading the collector's screen frames for <paramref name="screens"/>, each hardware id's
    /// cooler handoff. Null when there is no screen, in which case nothing is started.
    /// </summary>
    public SpaceDeckScreenClient? StartScreens(IReadOnlyDictionary<string, CoolerHandoff> screens) {
        if (screens is null) {
            throw new ArgumentNullException(nameof(screens));
        }

        if (screens.Count == 0) {
            return null;
        }

        var client = new SpaceDeckScreenClient(screens, _pipe, _services, _clock, _log);
        client.Start();
        return client;
    }

    /// <summary>The informational version <paramref name="assembly"/> was built with, or <c>unknown</c>.</summary>
    public static string VersionOf(Assembly assembly) {
        if (assembly is null) {
            throw new ArgumentNullException(nameof(assembly));
        }

        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    }
}
