namespace FanControl.LianLi.Devices;

/// <summary>
/// Which Galahad II coolers have their pump handed to the motherboard's PWM header instead of driven
/// by the plugin. For those the plugin sends the set-pump command with its source byte at "follow the
/// motherboard" (<see cref="Protocol.Galahad2Protocol.EncodeFollowMotherboardPump"/>) and registers no
/// pump control: FanControl drives the pump through its own control for the header the cooler is
/// wired to. Why: a pump duty set over USB persists in the cooler across a full power cut and the
/// cooler has no thermal failsafe of its own (measured on the Trinity, issue #30), so if FanControl
/// or the plugin stops, a USB-driven pump holds its last duty however hot the CPU gets. A pump that
/// follows the header keeps following the board, and the BIOS curve on that header, whatever happens
/// to FanControl. The Galahad II Vision LCD (0x7391, 0x7395) is handed over; every other cooler,
/// the Trinity included, keeps the pump control and the plugin's duty floor. Pure, so the rule is
/// testable on its own.
/// </summary>
internal static class Galahad2PumpSourceDecision {
    // The Galahad II Vision LCD's two product ids. Duplicated from the device catalog, which lists
    // them among every Galahad II id, so this rule names exactly the coolers it hands over.
    private const int VisionProductId = 0x7391;
    private const int VisionAlternateProductId = 0x7395;

    /// <summary>
    /// Whether the cooler with <paramref name="productId"/> has its pump follow the motherboard's PWM
    /// header rather than a duty the plugin sends.
    /// </summary>
    public static bool FollowsMotherboard(int productId)
        => productId == VisionProductId || productId == VisionAlternateProductId;
}
