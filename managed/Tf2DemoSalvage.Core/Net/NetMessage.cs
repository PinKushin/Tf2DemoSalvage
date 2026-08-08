namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Constants shared by the network message layer.
/// </summary>
public static class NetMessage
{
    /// <summary>
    /// Width of the message type field, in bits. Matches Source's <c>NETMSG_TYPE_BITS</c>.
    /// </summary>
    /// <remarks>
    /// Six bits allows ids up to 63; the highest defined at network protocol 24 is 32.
    /// </remarks>
    public const int TypeBits = 6;
}
