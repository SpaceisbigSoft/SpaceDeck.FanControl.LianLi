using FanControl.LianLi.Devices;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

/// <summary>Only the Galahad II Vision LCD's pump follows the motherboard header; every other cooler's is driven.</summary>
public class Galahad2PumpSourceDecisionTests {
    [Theory]
    [InlineData(0x7391, true)]  // Galahad II Vision LCD
    [InlineData(0x7395, true)]  // Galahad II Vision LCD (alternate pid)
    [InlineData(0x7371, false)] // Galahad II Trinity (performance)
    [InlineData(0x7373, false)] // Galahad II Trinity (regular)
    [InlineData(0x7398, false)] // HydroShift LCD
    [InlineData(0x7399, false)]
    [InlineData(0x739A, false)]
    public void FollowsMotherboard_OnlyTheVisionLcd(int productId, bool expected)
        => Assert.Equal(expected, Galahad2PumpSourceDecision.FollowsMotherboard(productId));
}
