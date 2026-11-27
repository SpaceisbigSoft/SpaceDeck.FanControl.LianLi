using System;
using System.Linq;
using FanControl.LianLi.Plugin;
using FanControl.LianLi.SpaceDeck;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>
/// What the plugin registered, as the collector's catalog: grouped into devices, every id and name
/// made valid, nothing past the collector's limits, and the values read in catalog order.
/// </summary>
public class SpaceDeckReadingsTests {
    private readonly FakeLogger _log = new FakeLogger();

    [Fact]
    public void TheRegisteredSensors_AreGroupedIntoDevices_InOrderOfTheirFirstReading() {
        var container = new FakeSensorsContainer();
        container.TempSensors.Add(new FakePluginSensor("LianLi/1/coolant/temp", "Lian Li GA II LCD Coolant", 31.5f));
        container.FanSensors.Add(new FakePluginSensor("LianLi/0/ch0/fan", "Lian Li Uni #1 Ch 1 RPM", 900f));
        container.FanSensors.Add(new FakePluginSensor("LianLi/1/ch1/fan", "Lian Li GA II LCD Pump RPM", 2800f));
        container.ControlSensors.Add(new FakePluginSensor("LianLi/0/ch0/ctl", "Lian Li Uni #1 Ch 1", 40f));
        container.ControlSensors.Add(new FakePluginSensor("LianLi/1/ch0/ctl", "Lian Li GA II LCD Fan", 55f));

        SpaceDeckReadings readings = SpaceDeckReadings.From(container, _log);

        Assert.Equal(
            new[] { ("LianLi/1", "Lian Li GA II LCD", "cooler"), ("LianLi/0", "Lian Li Uni #1", "fans") },
            readings.Catalog.Hardware.Select(h => (h.Id, h.Name, h.Kind)).ToArray());
        Assert.Equal(
            new[] {
                ("LianLi/1/coolant/temp", "LianLi/1", "temperature"),
                ("LianLi/1/ch1/fan", "LianLi/1", "fan"),
                ("LianLi/1/ch0/ctl", "LianLi/1", "control"),
                ("LianLi/0/ch0/fan", "LianLi/0", "fan"),
                ("LianLi/0/ch0/ctl", "LianLi/0", "control"),
            },
            readings.Catalog.Sensors.Select(s => (s.Id, s.HardwareId, s.Kind)).ToArray());
        Assert.Equal(new float?[] { 31.5f, 2800f, 55f, 900f, 40f }, readings.Capture());
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void Capture_ReadsTheValuesAsTheyAreNow() {
        var fan = new FakePluginSensor("LianLi/0/ch0/fan", "Fan", null);
        var container = new FakeSensorsContainer();
        container.FanSensors.Add(fan);
        SpaceDeckReadings readings = SpaceDeckReadings.From(container, _log);

        Assert.Equal(new float?[] { null }, readings.Capture());
        fan.Value = 1200f;
        Assert.Equal(new float?[] { 1200f }, readings.Capture());
    }

    [Fact]
    public void ThePlaceholderSensor_IsNotAReading() {
        var container = new FakeSensorsContainer();
        container.TempSensors.Add(new WaitingSensor());

        SpaceDeckReadings readings = SpaceDeckReadings.From(container, _log);

        Assert.Empty(readings.Catalog.Sensors);
        Assert.Empty(readings.Catalog.Hardware);
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void NamesAndIds_AreMadeValid() {
        var container = new FakeSensorsContainer();
        container.FanSensors.Add(new FakePluginSensor("LianLi/0/ch 0/fan", "Fan \u202e\"1\""));
        container.FanSensors.Add(new FakePluginSensor("LianLi/0/ch1/fan", null));

        SpaceDeckCatalog catalog = SpaceDeckReadings.From(container, _log).Catalog;

        Assert.Equal("LianLi/0/ch_0/fan", catalog.Sensors[0].Id);
        Assert.Equal("Fan ?\"1\"", catalog.Sensors[0].Name);
        Assert.Equal("LianLi/0/ch1/fan", catalog.Sensors[1].Name);
        Assert.Equal("LianLi/0", catalog.Hardware[0].Name); // the names share no word, so the device is named by its id
    }

    [Fact]
    public void ASensorWhoseIdNamesNoDevice_OrRepeatsAnId_IsLeftOutAndLogged() {
        var container = new FakeSensorsContainer();
        container.FanSensors.Add(new FakePluginSensor(string.Empty, "Empty"));
        container.FanSensors.Add(new FakePluginSensor(null, "No id"));
        container.FanSensors.Add(new FakePluginSensor("LianLi/loose", "Loose"));
        container.FanSensors.Add(new FakePluginSensor("LianLi/0/ch0/fan", "Fan"));
        container.FanSensors.Add(new FakePluginSensor("LianLi/0/ch0/fan", "Fan again"));

        SpaceDeckCatalog catalog = SpaceDeckReadings.From(container, _log).Catalog;

        Assert.Equal(new[] { "LianLi/0/ch0/fan" }, catalog.Sensors.Select(s => s.Id).ToArray());
        Assert.Equal(
            new[] {
                "SpaceDeck: sensor '' is not sent to SpaceDeck: its id names no device",
                "SpaceDeck: sensor '' is not sent to SpaceDeck: its id names no device",
                "SpaceDeck: sensor 'LianLi/loose' is not sent to SpaceDeck: its id names no device",
                "SpaceDeck: sensor 'LianLi/0/ch0/fan' is not sent to SpaceDeck: another sensor has the same id",
            },
            _log.Messages);
    }

    [Fact]
    public void DevicesPastThirtyTwo_AreLeftOut() {
        var container = new FakeSensorsContainer();
        for (int device = 0; device < 34; device++) {
            container.FanSensors.Add(new FakePluginSensor("LianLi/" + device + "/ch0/fan", "Fan " + device));
        }

        SpaceDeckCatalog catalog = SpaceDeckReadings.From(container, _log).Catalog;

        Assert.Equal(SpaceDeckReadings.MaximumHardware, catalog.Hardware.Count);
        Assert.Equal(2, _log.Messages.Count(m => m.EndsWith("SpaceDeck takes at most 32 devices", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReadingsPast512_AreLeftOut() {
        var container = new FakeSensorsContainer();
        for (int fan = 0; fan < 515; fan++) {
            container.FanSensors.Add(new FakePluginSensor("LianLi/" + (fan % 4) + "/f" + fan + "/fan", "F"));
        }

        SpaceDeckCatalog catalog = SpaceDeckReadings.From(container, _log).Catalog;

        Assert.Equal(SpaceDeckReadings.MaximumSensors, catalog.Sensors.Count);
        Assert.Equal(3, _log.Messages.Count(m => m.EndsWith("SpaceDeck takes at most 512 readings", StringComparison.Ordinal)));
    }

    [Fact]
    public void ACatalogLongerThanALine_LosesReadingsFromTheEnd_AndDevicesLeftWithout() {
        // Every character of these names is escaped as twelve, so thirty devices of two readings each
        // are past 64 KiB while staying within the 32 devices the collector takes.
        string name = string.Concat(Enumerable.Repeat("\U0001F300", 64));
        var container = new FakeSensorsContainer();
        for (int device = 0; device < 30; device++) {
            for (int fan = 0; fan < 2; fan++) {
                container.FanSensors.Add(new FakePluginSensor("LianLi/" + device + "/" + fan + new string('f', 100), name));
            }
        }

        SpaceDeckReadings readings = SpaceDeckReadings.From(container, _log);
        SpaceDeckCatalog catalog = readings.Catalog;

        Assert.InRange(catalog.Line.Length - 1, SpaceDeckPipeEncoder.MaximumLineBytes / 2, SpaceDeckPipeEncoder.MaximumLineBytes);
        Assert.InRange(catalog.Sensors.Count, 1, 59);
        Assert.Equal(catalog.Sensors.Select(s => s.HardwareId).Distinct().ToArray(), catalog.Hardware.Select(h => h.Id).ToArray());
        Assert.Equal(catalog.Sensors.Count, readings.Capture().Length);
        Assert.Equal(60 - catalog.Sensors.Count, _log.Messages.Count(m => m.EndsWith("longer than SpaceDeck's 64 KiB line", StringComparison.Ordinal)));
    }

    [Fact]
    public void ACatalogLongerThanALine_KeepsADeviceThatStillHasReadings() {
        string name = string.Concat(Enumerable.Repeat("\U0001F300", 64));
        var container = new FakeSensorsContainer();
        for (int fan = 0; fan < 100; fan++) {
            container.FanSensors.Add(new FakePluginSensor("LianLi/0/" + fan + new string('f', 100), name));
        }

        SpaceDeckCatalog catalog = SpaceDeckReadings.From(container, _log).Catalog;

        Assert.Single(catalog.Hardware);
        Assert.InRange(catalog.Sensors.Count, 1, 99);
    }

    [Fact]
    public void NullArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => SpaceDeckReadings.From(null!, _log));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckReadings.From(new FakeSensorsContainer(), null!));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckReadings.From(new FakeSensorsContainer(), null!, _log));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckReadings.From(null!, Array.Empty<string>(), _log));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckReadings.From(new FakeSensorsContainer(), Array.Empty<string>(), null!));
    }

    [Fact]
    public void ADeviceNamedAsAScreen_IsListedWithTheGalahad2LcdScreen_AndOnlyThatOne() {
        var container = new FakeSensorsContainer();
        container.TempSensors.Add(new FakePluginSensor("LianLi/1/coolant/temp", "Lian Li GA II LCD Coolant", 31.5f));
        container.FanSensors.Add(new FakePluginSensor("LianLi/0/ch0/fan", "Lian Li Uni #1 Ch 1 RPM", 900f));

        SpaceDeckCatalog catalog = SpaceDeckReadings.From(container, new[] { "LianLi/1", "LianLi/7" }, _log).Catalog;

        Assert.Same(SpaceDeckScreen.Galahad2Lcd, catalog.Hardware[0].Screen);
        Assert.Null(catalog.Hardware[1].Screen);
        Assert.Contains("\"kind\":\"cooler\",\"screen\":{\"width\":480,\"height\":480,\"format\":\"jpeg\"}}", catalog.Line, StringComparison.Ordinal);
    }
}
