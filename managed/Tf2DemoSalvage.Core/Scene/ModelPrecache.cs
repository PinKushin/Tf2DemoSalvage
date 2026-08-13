using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The <c>modelprecache</c> string table: which model each <c>m_nModelIndex</c> names.
/// </summary>
/// <remarks>
/// **This is the client's own route and the map's entity lump plays no part in it.** A health pack,
/// a dropped weapon, a door and a rocket are all networked entities; Valve's client reads
/// <c>m_nModelIndex</c> off the entity — <c>c_baseentity.cpp</c> line 449 — and asks
/// <c>modelinfo</c> for the model that index names, which is this table. Reading the entity lump
/// instead would place pickups where the mapper put them, which is only where they are until
/// somebody takes one, and would miss every entity the map never placed.
///
/// Only static props come from the map file, and they are a separate system in the engine too
/// (<c>StaticPropMgr</c>) precisely because nothing about them is networked.
/// </remarks>
public sealed class ModelPrecache
{
    /// <summary>The table this reads. Updates name their table only by id, not by name.</summary>
    public const string TableName = "modelprecache";

    /// <summary>Last protocol that packed model indices below −1.</summary>
    /// <remarks><c>PROTOCOL_VERSION_20</c> in the proxy's own condition.</remarks>
    public const int LastPackedIndexProtocol = 20;

    private readonly Dictionary<int, string> _paths = [];

    /// <summary>Records a create or update message's entries.</summary>
    /// <param name="entries">Entries from the message; later ones replace earlier ones.</param>
    /// <remarks>
    /// **By the entry's own index, not by its position in this list.** An update carries only the
    /// entries that changed, each stating where it belongs, so numbering them from zero would
    /// rewrite the front of the table with whatever happened to change.
    /// </remarks>
    public void Apply(IReadOnlyList<StringTableEntry> entries)
    {
        if (entries is null)
        {
            return;
        }

        foreach (StringTableEntry entry in entries)
        {
            // An entry with no text is a payload-only update to an existing one - the model name
            // does not change - and an empty name is index zero's placeholder for "no model".
            if (entry.Index < 0 || string.IsNullOrEmpty(entry.Text))
            {
                continue;
            }

            _paths[entry.Index] = entry.Text;
        }
    }

    /// <summary>The model an index names.</summary>
    /// <param name="modelIndex">The entity's <c>m_nModelIndex</c>, already unpacked.</param>
    /// <returns>The model path, or <c>null</c> when the table cannot name one.</returns>
    /// <remarks>
    /// **Null rather than a guess, and the negative case is the reason.** A negative index is a
    /// dynamic model the recording client precached for itself; a demo of somebody else's session
    /// carries no entry for it. Treating the number as an index anyway would read an unrelated
    /// entry and place a wrong model with complete confidence, which is the failure this project
    /// keeps meeting: a plausible answer rather than an error.
    ///
    /// **No guard on the number itself**, deliberately: <see cref="Apply"/> stores nothing at zero
    /// or below, so a lookup answers those correctly on its own. An added <c>modelIndex &gt; 0</c>
    /// reads as care and is a branch no test can ever take, because no input reaches it — the kind
    /// of line that survives mutation testing forever and means nothing when it does.
    /// </remarks>
    public string? Path(int modelIndex) =>
        _paths.TryGetValue(modelIndex, out string? path) ? path : null;

    /// <summary>Undoes the packing early protocols applied to negative model indices.</summary>
    /// <param name="modelIndex">The value as the entity carried it.</param>
    /// <param name="protocol">The demo's network protocol.</param>
    /// <returns>The index the client would have used.</returns>
    /// <remarks>
    /// **Transcribed from <c>RecvProxy_IntToModelIndex16_BackCompatible</c>** in
    /// <c>src/game/client/recvproxy.cpp</c>:
    ///
    /// <code>
    /// int modelIndex = pData->m_Value.m_Int;
    /// if ( modelIndex &lt; -1 &amp;&amp; engine->GetProtocolVersion() &lt;= PROTOCOL_VERSION_20 )
    /// {
    ///     Assert( modelIndex > -20000 );
    ///     modelIndex = -2 - ( ( -2 - modelIndex ) &lt;&lt; 1 );
    /// }
    /// </code>
    ///
    /// The engine's own compatibility shim, and exactly the kind of quirk this project exists for:
    /// it applies to five of the protocols in the corpus and to none of the modern ones, and it is
    /// invisible without it — a packed index is still a number, so it resolves to *some* model.
    ///
    /// <c>-1</c> is excluded by Valve's own condition; it means "no model" rather than a packed
    /// value.
    /// </remarks>
    public static int Unpack(int modelIndex, int protocol) =>
        modelIndex < -1 && protocol <= LastPackedIndexProtocol
            ? -2 - ((-2 - modelIndex) << 1)
            : modelIndex;
}
