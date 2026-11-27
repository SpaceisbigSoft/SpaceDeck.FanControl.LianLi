using System;
using System.Collections.Generic;
using System.Globalization;
using FanControl.LianLi.Logging;
using FanControl.LianLi.Plugin;
using FanControl.Plugins;

namespace FanControl.LianLi.SpaceDeck;

/// <summary>
/// What the plugin has registered with FanControl, as the SpaceDeck collector sees it: the
/// <see cref="SpaceDeckCatalog"/>, built once from the sensors <c>Load</c> registered, and their
/// values, read by <see cref="Capture"/>. Every temperature sensor is a temperature reading, every
/// fan sensor a fan reading and every control a control reading (the duty FanControl is applying,
/// as the control last recorded it). The placeholder registered while devices are still to come is
/// not a reading. A sensor whose id cannot be made valid, that repeats an id, or that would take the
/// catalog past one of the collector's limits is left out, and logged.
/// </summary>
internal sealed class SpaceDeckReadings {
    /// <summary>The most devices the collector takes in one catalog.</summary>
    public const int MaximumHardware = 32;

    /// <summary>The most readings the collector takes in one catalog.</summary>
    public const int MaximumSensors = 512;

    private readonly IPluginSensor[] _sources;

    private SpaceDeckReadings(SpaceDeckCatalog catalog, IPluginSensor[] sources) {
        Catalog = catalog;
        _sources = sources;
    }

    /// <summary>The devices and readings, in the order <see cref="Capture"/> returns the values.</summary>
    public SpaceDeckCatalog Catalog { get; }

    /// <summary>The readings of every sensor <paramref name="container"/> holds, grouped into devices.</summary>
    public static SpaceDeckReadings From(IPluginSensorsContainer container, ILog log)
        => From(container, Array.Empty<string>(), log);

    /// <summary>
    /// The readings of every sensor <paramref name="container"/> holds, grouped into devices, with
    /// the Galahad II Vision LCD's screen on each device whose id is in <paramref name="screens"/>.
    /// </summary>
    public static SpaceDeckReadings From(IPluginSensorsContainer container, IEnumerable<string> screens, ILog log) {
        if (container is null) {
            throw new ArgumentNullException(nameof(container));
        }

        if (screens is null) {
            throw new ArgumentNullException(nameof(screens));
        }

        if (log is null) {
            throw new ArgumentNullException(nameof(log));
        }

        var withScreen = new HashSet<string>(screens, StringComparer.Ordinal);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<Group>();
        var byHardware = new Dictionary<string, Group>(StringComparer.Ordinal);
        int count = 0;
        foreach (Candidate candidate in Candidates(container)) {
            string rawId = candidate.Sensor.Id ?? string.Empty;
            string? id = SpaceDeckText.Id(rawId);
            string? hardwareId = id is null ? null : SpaceDeckDeviceGrouping.HardwareIdOf(id);
            if (id is null || hardwareId is null) {
                LeaveOut(log, rawId, "its id names no device");
                continue;
            }

            if (!ids.Add(id)) {
                LeaveOut(log, id, "another sensor has the same id");
                continue;
            }

            if (count == MaximumSensors) {
                LeaveOut(log, id, "SpaceDeck takes at most 512 readings");
                continue;
            }

            if (!byHardware.TryGetValue(hardwareId, out Group? group)) {
                if (groups.Count == MaximumHardware) {
                    LeaveOut(log, id, "SpaceDeck takes at most 32 devices");
                    continue;
                }

                group = new Group(hardwareId);
                groups.Add(group);
                byHardware.Add(hardwareId, group);
            }

            group.Members.Add(new Member(candidate.Sensor, id, candidate.Kind));
            count++;
        }

        var hardware = new List<SpaceDeckHardware>();
        var sensors = new List<SpaceDeckSensor>();
        var sources = new List<IPluginSensor>();
        foreach (Group group in groups) {
            var names = new List<string>();
            bool reportsTemperature = false;
            foreach (Member member in group.Members) {
                names.Add(member.Name);
                reportsTemperature |= member.Kind == SpaceDeckSensor.TemperatureKind;
            }

            string name = SpaceDeckDeviceGrouping.HardwareNameOf(names);
            hardware.Add(new SpaceDeckHardware(
                group.Id,
                SpaceDeckText.Name(name, group.Id),
                SpaceDeckDeviceGrouping.KindOf(name, reportsTemperature),
                withScreen.Contains(group.Id) ? SpaceDeckScreen.Galahad2Lcd : null));
            foreach (Member member in group.Members) {
                sensors.Add(new SpaceDeckSensor(member.Id, group.Id, SpaceDeckText.Name(member.Name, member.Id), member.Kind));
                sources.Add(member.Sensor);
            }
        }

        // The catalog is one line, and the collector drops a client whose line is longer than 64 KiB;
        // the plugin's own names never come near it, but whatever does is cut from the end.
        var catalog = new SpaceDeckCatalog(hardware, sensors);
        while (catalog.Line.Length - 1 > SpaceDeckPipeEncoder.MaximumLineBytes) {
            int last = sensors.Count - 1;
            string hardwareId = sensors[last].HardwareId;
            LeaveOut(log, sensors[last].Id, "the catalog would be longer than SpaceDeck's 64 KiB line");
            sensors.RemoveAt(last);
            sources.RemoveAt(last);
            if (!sensors.Exists(sensor => string.Equals(sensor.HardwareId, hardwareId, StringComparison.Ordinal))) {
                _ = hardware.RemoveAll(entry => string.Equals(entry.Id, hardwareId, StringComparison.Ordinal));
            }

            catalog = new SpaceDeckCatalog(hardware, sensors);
        }

        return new SpaceDeckReadings(catalog, sources.ToArray());
    }

    /// <summary>
    /// The value of every reading, in catalog order, as the sensors hold it now. Cheap - a property
    /// read per sensor - and made on FanControl's own thread as it updates the sensors, so the values
    /// are the ones it shows.
    /// </summary>
    public float?[] Capture() {
        var values = new float?[_sources.Length];
        for (int i = 0; i < _sources.Length; i++) {
            values[i] = _sources[i].Value;
        }

        return values;
    }

    private static IEnumerable<Candidate> Candidates(IPluginSensorsContainer container) {
        foreach (IPluginSensor sensor in container.TempSensors) {
            if (!(sensor is WaitingSensor)) {
                yield return new Candidate(sensor, SpaceDeckSensor.TemperatureKind);
            }
        }

        foreach (IPluginSensor sensor in container.FanSensors) {
            yield return new Candidate(sensor, SpaceDeckSensor.FanKind);
        }

        foreach (IPluginControlSensor sensor in container.ControlSensors) {
            yield return new Candidate(sensor, SpaceDeckSensor.ControlKind);
        }
    }

    private static void LeaveOut(ILog log, string id, string reason)
        => log.Write(string.Format(CultureInfo.InvariantCulture, "SpaceDeck: sensor '{0}' is not sent to SpaceDeck: {1}", id, reason));

    private sealed class Candidate {
        public Candidate(IPluginSensor sensor, string kind) {
            Sensor = sensor;
            Kind = kind;
        }

        public IPluginSensor Sensor { get; }

        public string Kind { get; }
    }

    private sealed class Member {
        public Member(IPluginSensor sensor, string id, string kind) {
            Sensor = sensor;
            Id = id;
            Kind = kind;
            Name = sensor.Name ?? string.Empty;
        }

        public IPluginSensor Sensor { get; }

        public string Id { get; }

        public string Kind { get; }

        public string Name { get; }
    }

    private sealed class Group {
        public Group(string id) => Id = id;

        public string Id { get; }

        public List<Member> Members { get; } = new List<Member>();
    }
}
