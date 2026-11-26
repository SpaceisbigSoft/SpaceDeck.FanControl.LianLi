using System;
using FanControl.LianLi.SpaceDeck;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>
/// Ids and names made valid for the collector, which disconnects on the first one that is not: ids
/// from <c>A-Z a-z 0-9 / . _ : -</c> and at most 128 long, names of at most 64 printable Unicode
/// characters, not blank.
/// </summary>
public class SpaceDeckTextTests {
    [Fact]
    public void AValidId_IsKept()
        => Assert.Equal("LianLi/wa1b2c3d4e5f6/f0/fan", SpaceDeckText.Id("LianLi/wa1b2c3d4e5f6/f0/fan"));

    [Fact]
    public void AnIdCharacterOutsideTheSet_IsReplaced()
        => Assert.Equal("LianLi/0/ch_1/a.b_c:d-e/_", SpaceDeckText.Id("LianLi/0/ch 1/a.b_c:d-e/é"));

    [Fact]
    public void ALongId_IsCutTo128() {
        string id = SpaceDeckText.Id(new string('a', 200))!;

        Assert.Equal(new string('a', SpaceDeckText.MaximumIdLength), id);
    }

    [Fact]
    public void AnEmptyId_CannotBeMadeValid()
        => Assert.Null(SpaceDeckText.Id(string.Empty));

    [Fact]
    public void AValidName_IsKept_NonAsciiAndPairsIncluded()
        => Assert.Equal("Ventilátor \U0001F300 #2", SpaceDeckText.Name("Ventilátor \U0001F300 #2", "x"));

    [Theory]
    [InlineData("a\u0007b")]
    [InlineData("a‮b")]
    [InlineData("a​b")]
    [InlineData("a b")]
    [InlineData("a b")]
    [InlineData("a͸b")]
    public void ACharacterTheCollectorRefuses_IsReplaced(string name)
        => Assert.Equal("a?b", SpaceDeckText.Name(name, "x"));

    [Fact]
    public void AnUnassignedCharacterOutsideTheBasicPlane_IsReplacedOnce()
        => Assert.Equal("a?b", SpaceDeckText.Name("a" + char.ConvertFromUtf32(0xE0080) + "b", "x"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ALoneSurrogate_IsReplaced(int shape) {
        // Built here rather than in the attribute: a lone surrogate does not survive test discovery.
        string name = shape switch {
            0 => "a" + '\ud83c',
            1 => "a" + '\ud83c' + "b",
            _ => "a" + '\udf00' + "b",
        };

        Assert.Equal(shape == 0 ? "a?" : "a?b", SpaceDeckText.Name(name, "x"));
    }

    [Fact]
    public void ALongName_IsCutTo64Characters_CountingAPairOnce() {
        string emoji = "\U0001F300";
        string name = string.Concat(System.Linq.Enumerable.Repeat(emoji, 70));

        string cleaned = SpaceDeckText.Name(name, "x");

        Assert.Equal(SpaceDeckText.MaximumNameLength * 2, cleaned.Length);
        Assert.Equal(string.Concat(System.Linq.Enumerable.Repeat(emoji, SpaceDeckText.MaximumNameLength)), cleaned);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankName_FallsBackToTheId(string name)
        => Assert.Equal("LianLi/0/ch0/fan", SpaceDeckText.Name(name, "LianLi/0/ch0/fan"));

    [Fact]
    public void AFallbackLongerThanAName_IsCutToo()
        => Assert.Equal(new string('a', SpaceDeckText.MaximumNameLength), SpaceDeckText.Name(string.Empty, new string('a', 100)));

    [Fact]
    public void NullArguments_AreRejected() {
        Assert.Throws<ArgumentNullException>(() => SpaceDeckText.Id(null!));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckText.Name(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => SpaceDeckText.Name("x", null!));
    }
}
