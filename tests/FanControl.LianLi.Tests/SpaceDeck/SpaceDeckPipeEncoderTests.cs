using System;
using FanControl.LianLi.SpaceDeck;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>
/// The pipe's lines, character for character: what SpaceDeck's collector parses, one JSON object per
/// line, every character outside printable ASCII escaped.
/// </summary>
public class SpaceDeckPipeEncoderTests {
    [Fact]
    public void Hello_IsTheVersionedFirstLine() {
        Assert.Equal(
            "{\"t\":\"hello\",\"v\":1,\"plugin\":\"Lian Li Uni (Lighting)\",\"version\":\"2.0.7+abc1234\",\"instance\":\"9f1c\"}\n",
            SpaceDeckPipeEncoder.Hello("Lian Li Uni (Lighting)", "2.0.7+abc1234", "9f1c"));
    }

    [Fact]
    public void Catalog_ListsTheDevicesThenTheReadings() {
        var hardware = new[] {
            new SpaceDeckHardware("LianLi/0", "Lian Li GA II LCD", SpaceDeckHardware.CoolerKind),
            new SpaceDeckHardware("LianLi/wa1b2c3d4e5f6", "Lian Li UNI FAN TL V2 Wireless d4e5f6", SpaceDeckHardware.FansKind),
        };
        var sensors = new[] {
            new SpaceDeckSensor("LianLi/0/coolant/temp", "LianLi/0", "Lian Li GA II LCD Coolant", SpaceDeckSensor.TemperatureKind),
            new SpaceDeckSensor("LianLi/0/ch0/ctl", "LianLi/0", "Lian Li GA II LCD Fan", SpaceDeckSensor.ControlKind),
            new SpaceDeckSensor("LianLi/wa1b2c3d4e5f6/f0/fan", "LianLi/wa1b2c3d4e5f6", "Fan 1", SpaceDeckSensor.FanKind),
        };

        Assert.Equal(
            "{\"t\":\"catalog\",\"hardware\":["
                + "{\"id\":\"LianLi/0\",\"name\":\"Lian Li GA II LCD\",\"kind\":\"cooler\"},"
                + "{\"id\":\"LianLi/wa1b2c3d4e5f6\",\"name\":\"Lian Li UNI FAN TL V2 Wireless d4e5f6\",\"kind\":\"fans\"}"
                + "],\"sensors\":["
                + "{\"id\":\"LianLi/0/coolant/temp\",\"hardware\":\"LianLi/0\",\"name\":\"Lian Li GA II LCD Coolant\",\"kind\":\"temperature\"},"
                + "{\"id\":\"LianLi/0/ch0/ctl\",\"hardware\":\"LianLi/0\",\"name\":\"Lian Li GA II LCD Fan\",\"kind\":\"control\"},"
                + "{\"id\":\"LianLi/wa1b2c3d4e5f6/f0/fan\",\"hardware\":\"LianLi/wa1b2c3d4e5f6\",\"name\":\"Fan 1\",\"kind\":\"fan\"}"
                + "]}\n",
            SpaceDeckPipeEncoder.Catalog(hardware, sensors));
    }

    [Fact]
    public void Catalog_GivesADeviceWithAScreenItsScreenField() {
        var hardware = new[] {
            new SpaceDeckHardware("LianLi/0", "Lian Li GA II LCD", SpaceDeckHardware.CoolerKind, SpaceDeckScreen.Galahad2Lcd),
            new SpaceDeckHardware("LianLi/1", "Odd", SpaceDeckHardware.FansKind, new SpaceDeckScreen(1, 1024, "j\"g")),
        };

        Assert.Equal(
            "{\"t\":\"catalog\",\"hardware\":["
                + "{\"id\":\"LianLi/0\",\"name\":\"Lian Li GA II LCD\",\"kind\":\"cooler\",\"screen\":{\"width\":480,\"height\":480,\"format\":\"jpeg\"}},"
                + "{\"id\":\"LianLi/1\",\"name\":\"Odd\",\"kind\":\"fans\",\"screen\":{\"width\":1,\"height\":1024,\"format\":\"j\\\"g\"}}"
                + "],\"sensors\":[]}\n",
            SpaceDeckPipeEncoder.Catalog(hardware, Array.Empty<SpaceDeckSensor>()));
    }

    [Fact]
    public void Catalog_OfNothing_HasEmptyLists()
        => Assert.Equal(
            "{\"t\":\"catalog\",\"hardware\":[],\"sensors\":[]}\n",
            SpaceDeckPipeEncoder.Catalog(Array.Empty<SpaceDeckHardware>(), Array.Empty<SpaceDeckSensor>()));

    [Fact]
    public void Values_AreTheShortestNumbers_AndNullForNoneOrNonFinite() {
        Assert.Equal(
            "{\"t\":\"values\",\"values\":[21.6,2910,null,-3.5,null,null,null,0]}\n",
            SpaceDeckPipeEncoder.Values(new float?[] { 21.6f, 2910f, null, -3.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f }));
    }

    [Fact]
    public void Values_OfNoReadings_IsAnEmptyList()
        => Assert.Equal("{\"t\":\"values\",\"values\":[]}\n", SpaceDeckPipeEncoder.Values(Array.Empty<float?>()));

    [Theory]
    [InlineData("say \"hi\"", "say \\\"hi\\\"")]
    [InlineData("C:\\fans", "C:\\\\fans")]
    [InlineData("L\u00fcfter", "L\\u00fcfter")]
    [InlineData("tab\there", "tab\\u0009here")]
    [InlineData("del\u007f", "del\\u007f")]
    [InlineData("fan \U0001F300", "fan \\ud83c\\udf00")]
    [InlineData("~ !", "~ !")]
    public void Strings_AreEscaped(string text, string escaped)
        => Assert.Equal("{\"t\":\"hello\",\"v\":1,\"plugin\":\"" + escaped + "\",\"version\":\"1\",\"instance\":\"i\"}\n", SpaceDeckPipeEncoder.Hello(text, "1", "i"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ALoneSurrogate_IsWrittenAsTheReplacementCharacter(int shape) {
        // Built here rather than in the attribute: a lone surrogate does not survive test discovery.
        string text = shape switch {
            0 => "a" + '\ud83c',
            1 => "a" + '\ud83c' + "b",
            _ => "a" + '\udf00' + "b",
        };

        Assert.Contains("\"plugin\":\"a\\ufffd", SpaceDeckPipeEncoder.Hello(text, "1", "i"), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLine_IsPlainAscii_EndingInOneNewline() {
        string line = SpaceDeckPipeEncoder.Hello("Ventil\u00e1tor \U0001F300 \u202E", "1", "i");

        Assert.EndsWith("}\n", line, StringComparison.Ordinal);
        Assert.All(line.Substring(0, line.Length - 1), c => Assert.InRange(c, ' ', '~'));
    }

    [Fact]
    public void NullArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => SpaceDeckPipeEncoder.Hello(null!, "1", "i"));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckPipeEncoder.Hello("p", null!, "i"));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckPipeEncoder.Hello("p", "1", null!));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckPipeEncoder.Catalog(null!, Array.Empty<SpaceDeckSensor>()));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckPipeEncoder.Catalog(Array.Empty<SpaceDeckHardware>(), null!));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckPipeEncoder.Values(null!));
    }
}
