using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// One player, as the <c>userinfo</c> string table describes them.
/// </summary>
/// <param name="Name">Display name, as the client set it.</param>
/// <param name="UserId">
/// The identifier game events use. Stable for a connection, and unrelated to the entity index.
/// </param>
/// <param name="SteamId">Steam identifier in rendered form, e.g. <c>[U:1:1234567]</c>.</param>
/// <param name="EntityIndex">
/// The entity this player controls, taken from the string table entry's name rather than from
/// the record.
/// </param>
/// <param name="IsBot">Whether the engine marked this slot a fake player.</param>
/// <param name="IsSourceTv">Whether this slot is the SourceTV observer rather than a player.</param>
/// <remarks>
/// **Two identifiers, and they are not interchangeable.** Game events carry <c>user_id</c>;
/// entities are addressed by index. This record is where the two meet, which is what makes a
/// kill attributable to a name. Using one where the other belongs attributes events to the
/// wrong player and nothing fails — both are small integers and both are usually valid.
/// </remarks>
public readonly record struct PlayerInfo(
    string Name,
    int UserId,
    string SteamId,
    int EntityIndex,
    bool IsBot,
    bool IsSourceTv)
{
    /// <summary>Size of the record on the wire. A C struct written out directly.</summary>
    public const int RecordBytes = 132;

    private const int NameOffset = 0;
    private const int NameBytes = 32;
    private const int UserIdOffset = 32;
    private const int SteamIdOffset = 36;
    private const int SteamIdBytes = 32;
    private const int FakePlayerOffset = 108;
    private const int SourceTvOffset = 109;

    /// <summary>Where each field this reads sits, by the engine's name for it.</summary>
    /// <remarks>
    /// **Exposed so the offsets can be derived rather than trusted.** Three of them sit after
    /// padding the declaration does not spell out — <c>guid</c> is 33 bytes, so <c>friendsID</c>
    /// starts at 72 rather than 69 — and the numbers above encode that arithmetic silently.
    /// <c>PlayerInfoConformanceTests</c> computes the layout from <c>public/cdll_int.h</c> and
    /// checks these against it.
    ///
    /// <c>friendsName</c> and <c>customFiles</c> are not read and so are not listed; they are not
    /// gaps in the format, only in what this project needs.
    /// </remarks>
    internal static IReadOnlyList<(string Name, int Offset)> RecordFields =>
    [
        ("name", NameOffset),
        ("userID", UserIdOffset),
        ("guid", SteamIdOffset),
        ("fakeplayer", FakePlayerOffset),
        ("ishltv", SourceTvOffset),
    ];

    /// <summary>Reads one record.</summary>
    /// <param name="data">The string table entry's user data.</param>
    /// <param name="entityIndex">
    /// Entity index, parsed from the entry's name. Not present in <paramref name="data"/>.
    /// </param>
    /// <returns>The player.</returns>
    /// <exception cref="InvalidDataException">The record is shorter than the wire format.</exception>
    public static PlayerInfo Parse(ReadOnlySpan<byte> data, int entityIndex)
    {
        if (data.Length < RecordBytes)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A userinfo record is {RecordBytes} bytes; this one has {data.Length}. " +
                $"Reading it anyway would take whatever follows as a Steam id."));
        }

        return new PlayerInfo(
            ReadFixedString(data.Slice(NameOffset, NameBytes)),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(data[UserIdOffset..]),
            ReadFixedString(data.Slice(SteamIdOffset, SteamIdBytes)),
            entityIndex,
            data[FakePlayerOffset] != 0,
            data[SourceTvOffset] != 0);
    }

    /// <summary>
    /// Reads a fixed-width, NUL-padded string.
    /// </summary>
    /// <remarks>
    /// The terminator is padding, not a delimiter: a name occupying the whole field has none at
    /// all, so scanning for one and stopping there drops the last character. The scan is bounded
    /// by the field instead.
    /// </remarks>
    private static string ReadFixedString(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? field : field[..end]);
    }
}
