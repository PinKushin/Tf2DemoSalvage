using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// <c>svc_Print</c> — text the server sends to the client console.
/// </summary>
/// <param name="Text">The message, including its trailing newline if the server sent one.</param>
public sealed record PrintMessage(string Text) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.Print;
}

/// <summary>
/// <c>net_StringCmd</c> — a console command the server asks the client to run.
/// </summary>
/// <param name="Command">The command line.</param>
public sealed record StringCmdMessage(string Command) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.StringCmd;
}

/// <summary>
/// <c>net_SetConVar</c> — console variables the server is setting on the client.
/// </summary>
/// <param name="Variables">Name/value pairs, in the order transmitted.</param>
/// <remarks>
/// Worth keeping rather than discarding: this is where a demo records the server's tick rate
/// settings, mp_ tournament configuration and similar, which is useful context for a readable
/// dump even though nothing downstream depends on it.
/// </remarks>
public sealed record SetConVarMessage(IReadOnlyList<KeyValuePair<string, string>> Variables)
    : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.SetConVar;
}
