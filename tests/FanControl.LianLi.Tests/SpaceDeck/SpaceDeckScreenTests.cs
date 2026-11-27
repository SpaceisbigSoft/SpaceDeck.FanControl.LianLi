using System;
using FanControl.LianLi.SpaceDeck;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

public class SpaceDeckScreenTests {
    [Fact]
    public void TheGalahad2Lcd_Is480By480Jpeg() {
        Assert.Equal(480, SpaceDeckScreen.Galahad2Lcd.Width);
        Assert.Equal(480, SpaceDeckScreen.Galahad2Lcd.Height);
        Assert.Equal("jpeg", SpaceDeckScreen.Galahad2Lcd.Format);
    }

    [Theory]
    [InlineData(0, 480)]
    [InlineData(1025, 480)]
    [InlineData(480, 0)]
    [InlineData(480, 1025)]
    public void ASizeOutsideTheCollectorsRange_IsRejected(int width, int height)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SpaceDeckScreen(width, height, "jpeg"));

    [Fact]
    public void ANullFormat_IsRejected()
        => Assert.Throws<ArgumentNullException>(() => new SpaceDeckScreen(480, 480, null!));
}
