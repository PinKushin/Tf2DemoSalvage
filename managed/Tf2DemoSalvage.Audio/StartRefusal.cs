namespace Tf2DemoSalvage.Audio;

/// <summary>Why a start played nothing, so its caller can say so rather than guess (B732).</summary>
public enum StartRefusal
{
    /// <summary>The start played, or the sink does not say.</summary>
    None,

    /// <summary>A sample with no frames, rate or channels, or none that convert to a whole frame.</summary>
    Unplayable,

    /// <summary>`SND_PickDynamicChannel` gave no channel (engine.dll FUN_18002a480).</summary>
    NoDynamicChannel,

    /// <summary>All 64 static channels were taken: "total_channels == MAX_CHANNELS" (FUN_18002f4c0).</summary>
    NoStaticChannel,

    /// <summary>The same loop already plays on this source and channel, and plays on (FUN_18002e6e0, 0x18002e8a4-).</summary>
    LoopKept,

    /// <summary>
    /// A `SND_SPEAKER` start whose wave loops: "Speaker ignored looping sound" (FUN_18002e6e0, 0x18002ec0a; FUN_18002f4c0).
    /// The channel it had taken is freed (B724).
    /// </summary>
    SpeakerLoop,

    /// <summary>A negative delay skipped the whole wave or more (FUN_18002e6e0, 0x18002eff5).</summary>
    SkippedPastTheEnd,

    /// <summary>The device gave no source for an ear.</summary>
    NoSource,
}
