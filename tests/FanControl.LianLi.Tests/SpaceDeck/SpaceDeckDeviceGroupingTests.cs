using System;
using FanControl.LianLi.SpaceDeck;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>
/// One device per controller, cooler or wireless fan group: the id up to its second slash, named
/// for what its readings' names share, a cooler when it is one.
/// </summary>
public class SpaceDeckDeviceGroupingTests {
    [Theory]
    [InlineData("LianLi/1/coolant/temp", "LianLi/1")]
    [InlineData("LianLi/1/ch0/fan", "LianLi/1")]
    [InlineData("LianLi/wa1b2c3d4e5f6/f0/fan", "LianLi/wa1b2c3d4e5f6")]
    [InlineData("LianLi/wa1b2c3d4e5f6/ctl", "LianLi/wa1b2c3d4e5f6")]
    public void TheDevice_IsTheIdUpToItsSecondSlash(string sensorId, string hardwareId)
        => Assert.Equal(hardwareId, SpaceDeckDeviceGrouping.HardwareIdOf(sensorId));

    [Theory]
    [InlineData("LianLi")]
    [InlineData("/LianLi/0/x")]
    [InlineData("LianLi/waiting")]
    [InlineData("LianLi//ch0")]
    [InlineData("LianLi/0/")]
    public void AnIdWithoutADevicePart_NamesNoDevice(string sensorId)
        => Assert.Null(SpaceDeckDeviceGrouping.HardwareIdOf(sensorId));

    [Fact]
    public void ACoolersName_IsWhatItsReadingsShare()
        => Assert.Equal("Lian Li GA II LCD", SpaceDeckDeviceGrouping.HardwareNameOf(new[] {
            "Lian Li GA II LCD Coolant", "Lian Li GA II LCD Fan RPM", "Lian Li GA II LCD Pump RPM", "Lian Li GA II LCD Fan",
        }));

    [Fact]
    public void ANumberedName_EndsAtItsNumber_EvenForOneChannel()
        => Assert.Equal("Lian Li Uni #1", SpaceDeckDeviceGrouping.HardwareNameOf(new[] { "Lian Li Uni #1 Ch 2", "Lian Li Uni #1 Ch 2 RPM" }));

    [Fact]
    public void AGalahadsName_KeepsItsNumber()
        => Assert.Equal("Lian Li Galahad #2", SpaceDeckDeviceGrouping.HardwareNameOf(new[] { "Lian Li Galahad #2 Fan", "Lian Li Galahad #2 Pump RPM" }));

    [Fact]
    public void AWirelessGroupsName_IsItsControlsName()
        => Assert.Equal("Lian Li UNI FAN TL V2 Wireless 6232e1", SpaceDeckDeviceGrouping.HardwareNameOf(new[] {
            "Lian Li UNI FAN TL V2 Wireless 6232e1 Fan 1 RPM", "Lian Li UNI FAN TL V2 Wireless 6232e1",
        }));

    [Fact]
    public void NamesSharingNoWord_GiveNoName()
        => Assert.Equal(string.Empty, SpaceDeckDeviceGrouping.HardwareNameOf(new[] { "Pump", "Fan" }));

    [Fact]
    public void NoNames_GiveNoName()
        => Assert.Equal(string.Empty, SpaceDeckDeviceGrouping.HardwareNameOf(Array.Empty<string>()));

    [Theory]
    [InlineData("Lian Li Galahad #1")]
    [InlineData("Lian Li GA II LCD")]
    [InlineData("Lian Li HydroShift II OLED Curve #1")]
    [InlineData("Lian Li HydroShift II Wireless 6232e1")]
    public void ACoolerByName_IsACooler(string name)
        => Assert.Equal(SpaceDeckHardware.CoolerKind, SpaceDeckDeviceGrouping.KindOf(name, reportsTemperature: false));

    [Fact]
    public void ADeviceReportingATemperature_IsACooler()
        => Assert.Equal(SpaceDeckHardware.CoolerKind, SpaceDeckDeviceGrouping.KindOf("Lian Li Something", reportsTemperature: true));

    [Theory]
    [InlineData("Lian Li Uni #1")]
    [InlineData("Lian Li UNI FAN SL-INF FLEX USB 6232e1")]
    [InlineData("")]
    public void EverythingElse_IsFans(string name)
        => Assert.Equal(SpaceDeckHardware.FansKind, SpaceDeckDeviceGrouping.KindOf(name, reportsTemperature: false));

    [Fact]
    public void NullArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => SpaceDeckDeviceGrouping.HardwareIdOf(null!));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckDeviceGrouping.HardwareNameOf(null!));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckDeviceGrouping.KindOf(null!, false));
    }
}
