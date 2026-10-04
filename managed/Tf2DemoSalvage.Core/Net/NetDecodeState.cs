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
    /// before the first message is read. At protocol 15, which two builds wrote at two widths, it
    /// is also the check: the width is the one at which the first packet's ServerInfo restates
    /// this number (<see cref="MessageTypeBits"/>, B440).
    ///
    /// Defaulting to <see cref="CurrentProtocol"/> rather than to zero is deliberate: an
    /// unqualified <see cref="NetDecodeState"/> should behave as a modern demo, which is what
    /// every synthetic fixture in the tests assumes and what almost every real demo is.
    /// </remarks>
    public ushort NetworkProtocol { get; set; } = CurrentProtocol;

    /// <summary>The protocol current builds record at.</summary>
    private const ushort CurrentProtocol = 24;

    /// <summary>
    /// Last protocol any build wrote five-bit message type fields at — and the one protocol that
    /// was written at both widths.
    /// </summary>
    /// <remarks>
    /// **Measured on both sides, and the protocol turned out not to be the boundary.** TF2 build
    /// 3862 (June 2009) records protocol 15 at five bits; build 4604 (June 2011) records 16 at six.
    /// That was read as "the flip is at 15→16", on the reasoning that a protocol number only moves
    /// when the wire format does. It does not: two SourceTV demos from later protocol-15 builds (one
    /// named for 9 November 2010) write six, and read at five they are noise from the first
    /// message — 32,497 of 32,511 packets stopped, a server protocol of 30 (B440).
    ///
    /// **So below 15 the protocol decides the width, above it too, and at 15 the demo does** — see
    /// <see cref="MessageTypeBits"/>. The failure is loud either way: a wrong width desynchronises
    /// the first message of the signon, and the 2009 POV read at six produced 11,002 unreadable
    /// packets and a server protocol of 25,482 (B17).
    /// </remarks>
    private const ushort FiveBitTypeProtocol = 15;

    /// <summary>The width a packet decided, or null while nothing has.</summary>
    private int? _messageTypeBits;

    /// <summary>Width of a message's type field in this demo.</summary>
    /// <remarks>
    /// **The protocol decides it everywhere but 15.** Build 3862 wrote five bits at 15 and the
    /// builds after it wrote six, still announcing 15, and nothing Valve versioned tells them apart
    /// (B440). So a state at 15 leaves the width to the first packet it reads —
    /// <see cref="NetMessageReader.Read(System.ReadOnlySpan{byte}, NetDecodeState)"/> sets it there,
    /// and until then this answers five, build 3862's width.
    ///
    /// **Settable because a writer has no packet to read it from.** A demo compiled from text states
    /// its width, and a test writing what a reader learned copies it; the value set is the value
    /// used, at any protocol.
    /// </remarks>
    public int MessageTypeBits
    {
        get => _messageTypeBits ??
            (NetworkProtocol > FiveBitTypeProtocol ? NetMessage.TypeBits : NetMessage.OldTypeBits);
        set => _messageTypeBits = value;
    }

    /// <summary>Whether the next packet read has to decide <see cref="MessageTypeBits"/>.</summary>
    internal bool MessageTypeBitsUndecided =>
        _messageTypeBits is null && NetworkProtocol == FiveBitTypeProtocol;

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

    /// <summary>Width of a model index in <c>svc_BspDecal</c>: <c>SP_MODEL_INDEX_BITS</c>.</summary>
    /// <remarks>
    /// <c>SP_MODEL_INDEX_BITS</c> is <c>MAX_MODEL_INDEX_BITS + 1</c>, and the engine creates the
    /// <c>modelprecache</c> table <c>1 &lt;&lt; MAX_MODEL_INDEX_BITS</c> long, so the demo states
    /// the constant it was built with: 2048 entries (12 bits) in every corpus demo through 2013,
    /// 4096 (13) in modern ones (B489). Until the table is seen, the modern width.
    /// </remarks>
    public int ModelIndexBits
    {
        get
        {
            int table = _stringTableNames.IndexOf(ModelPrecacheTable);
            return table < 0
                ? NetMessageReader.ModelIndexBits
                : System.Numerics.BitOperations.Log2((uint)_stringTableCapacities[table]) + 1;
        }
    }

    private const string ModelPrecacheTable = "modelprecache";

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
