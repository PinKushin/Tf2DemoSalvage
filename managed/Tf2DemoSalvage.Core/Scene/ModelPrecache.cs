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

    /// <summary>The second table, which carries the models loaded during play.</summary>
    /// <remarks>
    /// **Named nowhere in the published SDK**, because the table is created engine side and the
    /// engine is closed. The demos name it themselves: every modern recording lists
    /// <c>DynamicModels</c> among its string tables, 60 entries on <c>cp_process</c>.
    /// </remarks>
    public const string DynamicTableName = "DynamicModels";

    private readonly Dictionary<int, string> _paths = [];
    private readonly Dictionary<int, string> _dynamic = [];

    /// <summary>Records a create or update message's entries.</summary>
    /// <param name="entries">Entries from the message; later ones replace earlier ones.</param>
    /// <remarks>
    /// **By the entry's own index, not by its position in this list.** An update carries only the
    /// entries that changed, each stating where it belongs, so numbering them from zero would
    /// rewrite the front of the table with whatever happened to change.
    /// </remarks>
    public void Apply(IReadOnlyList<StringTableEntry> entries) => Apply(entries, _paths);

    /// <summary>Records entries from the <c>DynamicModels</c> table.</summary>
    /// <param name="entries">Entries from the message; later ones replace earlier ones.</param>
    /// <remarks>
    /// Kept apart from the precache rather than merged into it, because the two are indexed
    /// independently: entry 7 of one and entry 7 of the other are different models, and a single
    /// dictionary would have each quietly overwrite the other.
    /// </remarks>
    public void ApplyDynamic(IReadOnlyList<StringTableEntry> entries) => Apply(entries, _dynamic);

    private static void Apply(IReadOnlyList<StringTableEntry> entries, Dictionary<int, string> into)
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

            into[entry.Index] = entry.Text;
        }
    }

    /// <summary>The model an index names.</summary>
    /// <param name="modelIndex">The entity's <c>m_nModelIndex</c>, already unpacked.</param>
    /// <returns>The model path, or <c>null</c> when the table cannot name one.</returns>
    /// <remarks>
    /// **A negative index is a dynamic model, and half of them ARE in the demo.** An earlier
    /// version of this comment said none were, on the reasoning that a dynamic model is one the
    /// recording client loaded for itself — which is true of exactly the odd ones. The engine
    /// states the split (<c>public/engine/ivmodelinfo.h:90</c>):
    ///
    /// <code>
    /// // If index &lt; -1, then the model is DYNAMIC and has a DYNAMIC INDEX of (-2 - index)
    /// // - if the dynamic index is ODD, then the model is CLIENT ONLY
    /// //   and has a m_LocalDynamicModels lookup index of (dynamic index)>>1
    /// // - if the dynamic index is EVEN, then the model is NETWORKED
    /// //   and has a dynamic model string table index of (dynamic index)>>1
    /// </code>
    ///
    /// Believing the whole range unreachable cost every cosmetic in every modern demo: measured on
    /// <c>cp_process</c>, 35 of 36 live <c>CTFWearable</c> entities carry a negative index, all of
    /// them even, all of them present in <c>DynamicModels</c>. Players drew bare-headed while
    /// every ordinary prop resolved perfectly, which is why it read as "cosmetics are not
    /// recorded" rather than as a lookup gap.
    ///
    /// The odd half stays null, and that is not laziness — it is genuinely not in the file. Naive
    /// halving would land on a real entry of the networked table and draw a wrong model with total
    /// confidence.
    ///
    /// **No guard on the number itself**, deliberately: <see cref="Apply(System.Collections.Generic.IReadOnlyList{Tf2DemoSalvage.Core.Net.StringTableEntry})"/> stores nothing at zero
    /// or below, so a lookup answers those correctly on its own. An added <c>modelIndex &gt; 0</c>
    /// reads as care and is a branch no test can ever take, because no input reaches it — the kind
    /// of line that survives mutation testing forever and means nothing when it does.
    /// </remarks>
    public string? Path(int modelIndex)
    {
        if (modelIndex >= -1)
        {
            return _paths.TryGetValue(modelIndex, out string? path) ? path : null;
        }

        return DynamicSlot(modelIndex) is { } slot &&
            _dynamic.TryGetValue(slot, out string? loaded) ? loaded : null;
    }

    /// <summary>Which <c>DynamicModels</c> entry a negative model index names, when any.</summary>
    /// <param name="modelIndex">The networked index, which is signed and 13 bits wide.</param>
    /// <returns>The table slot, or <c>null</c> when the index is ordinary or client-only.</returns>
    /// <remarks>
    /// **From <c>ivmodelinfo.h:90</c>**: an index below −1 is dynamic, the dynamic index is
    /// <c>−2 − index</c>, and its low bit decides where it lives — EVEN is networked at
    /// <c>dynamic &gt;&gt; 1</c> of the <c>DynamicModels</c> string table, ODD is client-only and a
    /// demo cannot resolve it at all.
    ///
    /// **Every TF2 cosmetic arrives this way**, so reading a negative index as an ordinary one
    /// finds nothing and draws nothing — silently, because a model that failed to resolve is
    /// indistinguishable from an entity that has none.
    ///
    /// Separated from <see cref="Path"/> so the arithmetic can be asserted on its own: three
    /// operations that are each plausible in the wrong order.
    /// </remarks>
    internal static int? DynamicSlot(int modelIndex)
    {
        if (modelIndex >= -1)
        {
            return null;
        }

        int dynamicIndex = -2 - modelIndex;

        return (dynamicIndex & 1) == 0 ? dynamicIndex >> 1 : null;
    }

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
