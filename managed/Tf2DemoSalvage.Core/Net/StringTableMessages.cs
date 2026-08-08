using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>One entry in a string table: a string, optionally with binary data attached.</summary>
/// <param name="Index">Position in the table.</param>
/// <param name="Text">The string, or <c>null</c> if this update carried only user data.</param>
/// <param name="UserData">Attached bytes, empty when the entry has none.</param>
public sealed record StringTableEntry(int Index, string? Text, IReadOnlyList<byte> UserData);

/// <summary>
/// <c>svc_CreateStringTable</c> — declares a table and its initial contents.
/// </summary>
/// <param name="Name">Table name, e.g. <c>userinfo</c> or <c>modelprecache</c>.</param>
/// <param name="MaxEntries">Capacity. Also sets the bit width of entry indices.</param>
/// <param name="Entries">Decoded entries, empty when the table could not be decoded.</param>
/// <param name="IsCompressed">Whether the payload was compressed.</param>
/// <param name="UndecodedReason">Why entries are empty, or <c>null</c> if they decoded.</param>
/// <remarks>
/// The <c>userinfo</c> table is the interesting one: it holds each player's name, SteamID and
/// user id, which is what turns an entity index into a person.
///
/// Carries an explicit bit length, so like the game event messages it can be stepped over even
/// when its contents cannot be read — a compressed payload costs that table, not the rest of
/// the stream.
/// </remarks>
public sealed record CreateStringTableMessage(
    string Name,
    int MaxEntries,
    IReadOnlyList<StringTableEntry> Entries,
    bool IsCompressed,
    string? UndecodedReason) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.CreateStringTable;

    /// <summary>Whether the table's entries were decoded.</summary>
    public bool IsDecoded => UndecodedReason is null;
}

/// <summary>
/// <c>svc_UpdateStringTable</c> — changes to a table declared earlier.
/// </summary>
/// <param name="TableId">Which table, by creation order.</param>
/// <param name="Entries">Decoded changes, empty when they could not be decoded.</param>
/// <param name="UndecodedReason">Why entries are empty, or <c>null</c> if they decoded.</param>
public sealed record UpdateStringTableMessage(
    int TableId,
    IReadOnlyList<StringTableEntry> Entries,
    string? UndecodedReason) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.UpdateStringTable;

    /// <summary>Whether the update's entries were decoded.</summary>
    public bool IsDecoded => UndecodedReason is null;
}
