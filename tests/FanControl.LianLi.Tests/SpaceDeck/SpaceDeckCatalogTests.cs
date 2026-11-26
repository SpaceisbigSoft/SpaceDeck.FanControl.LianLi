using System;
using FanControl.LianLi.SpaceDeck;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>The catalog and its entries: the line is the encoder's, and every part is required.</summary>
public class SpaceDeckCatalogTests {
    private static readonly SpaceDeckHardware Cooler = new SpaceDeckHardware("LianLi/0", "Cooler", SpaceDeckHardware.CoolerKind);
    private static readonly SpaceDeckSensor Coolant = new SpaceDeckSensor("LianLi/0/coolant/temp", "LianLi/0", "Coolant", SpaceDeckSensor.TemperatureKind);

    [Fact]
    public void TheLine_IsTheEncodedCatalog() {
        var catalog = new SpaceDeckCatalog(new[] { Cooler }, new[] { Coolant });

        Assert.Equal(SpaceDeckPipeEncoder.Catalog(new[] { Cooler }, new[] { Coolant }), catalog.Line);
        Assert.Same(Cooler, Assert.Single(catalog.Hardware));
        Assert.Same(Coolant, Assert.Single(catalog.Sensors));
    }

    [Fact]
    public void TheEntries_KeepWhatTheyWereGiven() {
        Assert.Equal(("LianLi/0", "Cooler", "cooler"), (Cooler.Id, Cooler.Name, Cooler.Kind));
        Assert.Equal(("LianLi/0/coolant/temp", "LianLi/0", "Coolant", "temperature"), (Coolant.Id, Coolant.HardwareId, Coolant.Name, Coolant.Kind));
    }

    [Fact]
    public void NullArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckCatalog(null!, Array.Empty<SpaceDeckSensor>()));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckCatalog(Array.Empty<SpaceDeckHardware>(), null!));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckHardware(null!, "n", "k"));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckHardware("i", null!, "k"));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckHardware("i", "n", null!));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckSensor(null!, "h", "n", "k"));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckSensor("i", null!, "n", "k"));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckSensor("i", "h", null!, "k"));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckSensor("i", "h", "n", null!));
    }
}
