using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>One entry in a string table: a string, optionally with binary data attached.</summary>
/// <param name="Index">Position in the table.</param>
/// <param name="Text">The string, or <c>null</c> if this update carried only user data.</param>
/// <param name="UserData">Attached bytes, empty when the entry has none.</param>
/// <param name="FollowsPrevious">
/// Whether the index was sent as "one after the last" rather than in full.
/// </param>
/// <param name="HistoryIndex">
/// Which of the last 32 strings this entry's text was built from, or <c>-1</c> when the text was
/// sent whole.
/// </param>
/// <param name="CopyLength">How many characters were taken from that string.</param>
/// <remarks>
/// **The last three are the encoding shape, and without them an entry cannot be written back.**
/// A table's strings are sent against a rolling history of the last 32, so a name sharing a prefix
/// with an earlier one transmits only its tail — and which earlier one, and how much of it, is a
/// choice the sender made that the decoded string does not record. The same is true of the index:
/// a sequential entry sends one bit where an explicit one sends a full field.
///
/// This is the same problem <c>svc_Sounds</c> had. Values are not enough; the shape has to travel
/// with them.
/// </remarks>
public sealed record StringTableEntry(
    int Index,
    string? Text,
    IReadOnlyList<byte> UserData,
    bool FollowsPrevious = false,
    int HistoryIndex = -1,
    int CopyLength = 0);

/// <summary>
/// <c>svc_CreateStringTable</c> — declares a table and its initial contents.
/// </summary>
/// <param name="Name">Table name, e.g. <c>userinfo</c> or <c>modelprecache</c>.</param>
/// <param name="MaxEntries">Capacity. Also sets the bit width of entry indices.</param>
/// <param name="Entries">Decoded entries, empty when the table could not be decoded.</param>
/// <param name="IsCompressed">Whether the payload was compressed.</param>
/// <param name="UndecodedReason">Why entries are empty, or <c>null</c> if they decoded.</param>
/// <param name="Wire">
/// The message exactly as it arrived, or <c>null</c> when it was not read from a demo.
/// </param>
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
    string? UndecodedReason,
    CreateStringTableWire? Wire = null) : INetMessage
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
/// <param name="Wire">
/// The message exactly as it arrived, or <c>null</c> when it was not read from a demo.
/// </param>
public sealed record UpdateStringTableMessage(
    int TableId,
    IReadOnlyList<StringTableEntry> Entries,
    string? UndecodedReason,
    UpdateStringTableWire? Wire = null) : INetMessage
{
    /// <inheritdoc />
    public NetMessageType Type => NetMessageType.UpdateStringTable;

    /// <summary>Whether the update's entries were decoded.</summary>
    public bool IsDecoded => UndecodedReason is null;
}

/// <summary>
/// A <c>svc_CreateStringTable</c> exactly as it sat on the wire.
/// </summary>
/// <param name="EntryCount">Entries the header declared.</param>
/// <param name="BodyBits">Length of the payload, before any decompression.</param>
/// <param name="Body">The payload's bits, compressed if the message said so.</param>
/// <param name="FixedUserDataSizeBytes">
/// The byte-count field sent alongside a fixed user data size, or <c>null</c> when the message
/// declared no fixed size. The decoder has no use for it — the bit count that follows is what
/// sizes a payload — but it is on the wire, so a message cannot be rebuilt without it.
/// </param>
/// <param name="FixedUserDataSizeBits">The bit count that actually sizes a fixed payload.</param>
/// <remarks>
/// **Kept because a decoded table cannot be turned back into the message that carried it.** Two
/// separate obstacles, and only one of them is about the entries: the payload is usually
/// Snappy-compressed, so reproducing the bytes would mean reproducing a particular compressor's
/// output, and that is not something a parser can promise. Holding the compressed bits is the
/// honest answer to that.
///
/// It also means the framing round-trips today rather than after the entry encoder exists, which
/// keeps the two questions separate: whether the message can be rebuilt, and whether the entry
/// decode is lossless. The second is what an entry-level round trip against the DECOMPRESSED body
/// answers, and it is not settled by this.
/// </remarks>
public sealed record CreateStringTableWire(
    int EntryCount,
    int BodyBits,
    System.ReadOnlyMemory<byte> Body,
    int? FixedUserDataSizeBytes,
    int FixedUserDataSizeBits);

/// <summary>A <c>svc_UpdateStringTable</c> exactly as it sat on the wire.</summary>
/// <param name="EntryCount">Entries the header declared.</param>
/// <param name="BodyBits">Length of the payload.</param>
/// <param name="Body">The payload's bits.</param>
/// <remarks>
/// An update is never compressed, so the obstacle here is only the entry encoding — which entry
/// reused which of the last 32 strings, and how much of it. That is a choice the sender made and
/// the decoded strings do not record.
/// </remarks>
public sealed record UpdateStringTableWire(
    int EntryCount, int BodyBits, System.ReadOnlyMemory<byte> Body);
