namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// A sprite's scale or brightness ramp, as <c>C_Sprite</c> keeps it on the client: the value it ramps from, the value it
/// ramps to, and when the ramp began.
/// </summary>
/// <param name="Start"><c>m_flStartScale</c> or <c>m_nStartBrightness</c>.</param>
/// <param name="Destination"><c>m_flDestScale</c> or <c>m_nDestBrightness</c>.</param>
/// <param name="StartSeconds"><c>m_flScaleTimeStart</c> or <c>m_flBrightnessTimeStart</c>.</param>
/// <remarks>
/// **Neither endpoint is networked, and the client still has both** (read from published source). The client's
/// <c>OnDataChanged</c> sets start and destination to the networked value on <c>DATA_UPDATE_CREATED</c>
/// (`Sprite.cpp:716-720`). Each <c>ClientThink</c> then compares the networked value with the destination, and on a
/// difference moves the old destination to the start, takes the new value as the destination, and stamps the client's
/// time (`Sprite.cpp:725-743`). <c>GetRenderScale</c> and <c>GetRenderBrightness</c> lerp between them for
/// <c>m_flScaleTime</c> or <c>m_flBrightnessTime</c> seconds, both networked (`Sprite.cpp:644-655`, `:690-705`, `:135`,
/// `:152`). B378 had filed the ramp as unrecoverable from a recording; it is the client's own state, rebuilt here from
/// the packets in order.
/// </remarks>
public readonly record struct SpriteRamp(float Start, float Destination, double StartSeconds)
{
    /// <summary><c>OnDataChanged</c>'s <c>DATA_UPDATE_CREATED</c>: both ends at the first value, so nothing ramps.</summary>
    /// <param name="value">The networked value the entity was created with.</param>
    /// <returns>The ramp.</returns>
    public static SpriteRamp Created(float value) => new(value, value, 0d);

    /// <summary><c>ClientThink</c>: a networked value that differs from the destination starts a new ramp.</summary>
    /// <param name="networked">The networked value now.</param>
    /// <param name="seconds">The client's time.</param>
    /// <returns>The ramp after the think.</returns>
    /// <remarks>
    /// The new ramp starts from the old DESTINATION, not from the value drawn at that moment, so a change that arrives
    /// mid-ramp jumps to where the last ramp was going (`Sprite.cpp:730-735`).
    /// </remarks>
    public SpriteRamp Think(float networked, double seconds)
    {
#pragma warning disable S1244 // Valve's own exact comparison: `m_flSpriteScale != m_flDestScale` (`Sprite.cpp:730`).
        return networked != Destination ? new SpriteRamp(Destination, networked, seconds) : this;
#pragma warning restore S1244
    }

    /// <summary><c>GetRenderScale</c> or <c>GetRenderBrightness</c> before its cast.</summary>
    /// <param name="networked">The networked value, which a finished or untimed ramp answers.</param>
    /// <param name="rampSeconds"><c>m_flScaleTime</c> or <c>m_flBrightnessTime</c>.</param>
    /// <param name="now">The client's time.</param>
    /// <returns>The value drawn.</returns>
    public float At(float networked, float rampSeconds, double now)
    {
        if (rampSeconds == 0f || StartSeconds + rampSeconds < now)
        {
            return networked;
        }

        float timeDelta = (float)((now - StartSeconds) / rampSeconds);

        return Start + ((Destination - Start) * timeDelta);
    }
}
