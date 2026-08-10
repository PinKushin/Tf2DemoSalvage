using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// State carried across packets while decoding a demo.
/// </summary>
/// <remarks>
/// Some messages describe how to read later ones — <c>svc_GameEventList</c> is the first
/// example, and string tables and the class list will follow. A packet therefore cannot be
/// decoded in isolation, which is why this is threaded through rather than each packet being
/// read independently.
/// </remarks>
public sealed class NetDecodeState
{
    private readonly Dictionary<int, GameEventDefinition> _eventDefinitions = [];

    /// <summary>
    /// Network protocol this demo was recorded at, from its header. Defaults to the current one.
    /// </summary>
    /// <remarks>
    /// **Taken from the demo header rather than from <see cref="ServerInfo"/>, and it has to
    /// be.** It sizes the message type field, and <c>svc_ServerInfo</c> is itself a message —
    /// reading it already requires knowing the width. The header is the only source available
    /// before the first message is read.
    ///
    /// Defaulting to <see cref="CurrentProtocol"/> rather than to zero is deliberate: an
    /// unqualified <see cref="NetDecodeState"/> should behave as a modern demo, which is what
    /// every synthetic fixture in the tests assumes and what almost every real demo is.
    /// </remarks>
    public ushort NetworkProtocol { get; init; } = CurrentProtocol;

    /// <summary>The protocol current builds record at.</summary>
    private const ushort CurrentProtocol = 24;

    /// <summary>Last protocol whose message type field was five bits wide.</summary>
    /// <remarks>
    /// **Exact, and measured on both sides.** Protocol 15 is five bits, confirmed against a demo
    /// recorded on TF2 build 3862 (June 2009). Protocol 16 is six, confirmed against a demo
    /// recorded on build 4604 (June 2011): it decodes end to end, 11,131 commands with no stops,
    /// which a five-bit read cannot produce.
    ///
    /// This was a guess until that demo arrived — the flip was known only to be somewhere in
    /// 16–23, and 15 was chosen because 16 is where Replay shipped and a protocol number only
    /// moves when the wire format does. The reasoning was right; it is now evidence.
    ///
    /// The failure mode is loud rather than silent, which is what made guessing tolerable in the
    /// meantime. A wrong width desynchronises the first message of the signon: the 2009 demo
    /// produced 11,002 unreadable packets and a server protocol of 25,482 before this was fixed,
    /// and zero afterwards. There is no reading of a wrong width that quietly produces plausible
    /// output — see <c>RISKS.md</c> B17.
    /// </remarks>
    private const ushort FiveBitTypeProtocol = 15;

    /// <summary>Width of a message's type field at this demo's protocol.</summary>
    public int MessageTypeBits =>
        NetworkProtocol > FiveBitTypeProtocol ? NetMessage.TypeBits : NetMessage.OldTypeBits;

    /// <summary>
    /// The server's own description of itself, once seen. Its <c>MaxClasses</c> determines the
    /// bit width of entity class ids, so entity decoding cannot begin without it.
    /// </summary>
    public ServerInfoMessage? ServerInfo { get; set; }

    /// <summary>Game event definitions seen so far, keyed by event id.</summary>
    public IReadOnlyDictionary<int, GameEventDefinition> EventDefinitions => _eventDefinitions;

    /// <summary>
    /// The networked class list, once seen. Entity updates carry a class id sized from it, so
    /// entity decoding cannot start without it.
    /// </summary>
    public ClassInfoMessage? ClassInfo { get; set; }

    /// <summary>
    /// Capacities of the string tables declared so far, in creation order. An update names its
    /// table by that order, and needs the capacity to size its entry indices.
    /// </summary>
    private readonly List<int> _stringTableCapacities = [];

    /// <summary>Names of the string tables declared so far, in creation order.</summary>
    private readonly List<string> _stringTableNames = [];

    /// <summary>Records a table's name and capacity as it is created.</summary>
    /// <param name="name">The table's name, e.g. <c>userinfo</c>.</param>
    /// <param name="maxEntries">The table's capacity.</param>
    /// <remarks>
    /// **The name is kept because an update does not carry one.** `svc_UpdateStringTable`
    /// identifies its table only by creation-order id, so without this there is no way to ask
    /// whether an update is for `userinfo` — which is why every player who joined after signon
    /// was invisible (RISKS B22).
    /// </remarks>
    public void AddStringTable(string name, int maxEntries)
    {
        _stringTableNames.Add(name);
        _stringTableCapacities.Add(maxEntries);
    }

    /// <summary>Name of the table with the given id, or <c>null</c> if it has not been seen.</summary>
    /// <param name="tableId">Table id, by creation order.</param>
    /// <returns>The name, or <c>null</c>.</returns>
    public string? StringTableName(int tableId) =>
        tableId >= 0 && tableId < _stringTableNames.Count ? _stringTableNames[tableId] : null;

    /// <summary>Capacity of the table with the given id, or 0 if it has not been seen.</summary>
    /// <param name="tableId">Table id, by creation order.</param>
    /// <returns>The capacity, or 0.</returns>
    public int StringTableCapacity(int tableId) =>
        tableId >= 0 && tableId < _stringTableCapacities.Count
            ? _stringTableCapacities[tableId]
            : 0;

    /// <summary>Records the definitions from a <c>svc_GameEventList</c>.</summary>
    /// <param name="definitions">Definitions to remember.</param>
    public void AddEventDefinitions(IEnumerable<GameEventDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        foreach (GameEventDefinition definition in definitions)
        {
            _eventDefinitions[definition.Id] = definition;
        }
    }
}
