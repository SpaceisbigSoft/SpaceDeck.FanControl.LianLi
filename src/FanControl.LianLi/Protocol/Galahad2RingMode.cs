#if ENABLE_LIGHTING
namespace FanControl.LianLi.Protocol;

/// <summary>
/// What the Galahad II Vision's screen ring shows: one colour per LED (<see cref="Direct"/>, sent with
/// 0x14 SetLEDColors), nothing (<see cref="Off"/>), or one of the cooler's own effects (0x83
/// SetPumpLighting). The effects are the ones lian-li-linux offers for the Vision; the cooler lists more.
/// </summary>
internal enum Galahad2RingMode {
    /// <summary>Each of the twelve LEDs its own colour, set by the host.</summary>
    Direct,

    /// <summary>The ring dark.</summary>
    Off,

    /// <summary>Up to four still colours.</summary>
    Static,

    /// <summary>Up to four colours fading in and out.</summary>
    Breathing,

    /// <summary>A rainbow turning round the ring.</summary>
    Rainbow,

    /// <summary>The whole ring cycling through the rainbow.</summary>
    RainbowMorph,

    /// <summary>Up to four colours running round the ring.</summary>
    Runway,

    /// <summary>Up to four colours shooting round the ring with a tail.</summary>
    Meteor,
}
#endif
