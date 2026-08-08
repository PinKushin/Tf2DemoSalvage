namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// <c>net_Tick</c> — the server's tick counter and frame timing. 64 bits on the wire.
/// </summary>
/// <param name="Tick">Server tick this packet belongs to.</param>
/// <param name="HostFrameTimeRaw">Frame time, scaled, as transmitted.</param>
/// <param name="HostFrameTimeStdDevRaw">Frame time standard deviation, scaled, as transmitted.</param>
/// <remarks>
/// Layout confirmed against <c>tf-demo-parser</c>: a 32-bit tick followed by two 16-bit
/// values. This is usually the first message in a <c>dem_packet</c>, which makes it the
/// natural first target for layer 2.
/// </remarks>
public sealed record NetTickMessage(
    int Tick,
    ushort HostFrameTimeRaw,
    ushort HostFrameTimeStdDevRaw) : INetMessage
{
    /// <summary>
    /// Divisor applied to the transmitted frame time. Source's <c>NET_TICK_SCALEUP</c>.
    /// </summary>
    /// <remarks>
    /// Worth flagging: <c>tf-demo-parser</c> keeps these fields raw and applies no scale, so
    /// this constant comes from Source engine convention rather than from a source we have
    /// verified byte-for-byte. The raw values are exposed alongside the converted ones so a
    /// caller that distrusts the scale is not forced to accept it.
    /// </remarks>
    public const float FrameTimeScale = 100000f;

    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.NetTick;

    /// <summary>Frame time in seconds.</summary>
    public float HostFrameTimeSeconds => HostFrameTimeRaw / FrameTimeScale;

    /// <summary>Frame time standard deviation in seconds.</summary>
    public float HostFrameTimeStdDevSeconds => HostFrameTimeStdDevRaw / FrameTimeScale;
}
