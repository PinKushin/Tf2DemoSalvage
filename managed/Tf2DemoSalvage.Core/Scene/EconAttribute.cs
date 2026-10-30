using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One econ attribute as an entity carries it: which definition, and its 32 raw bits.</summary>
/// <param name="DefinitionIndex">
/// <c>m_iAttributeDefinitionIndex</c> — the row in <c>items_game.txt</c>'s <c>attributes</c>
/// section. 2053 is <c>is_festivized</c>.
/// </param>
/// <param name="RawBits">
/// The value exactly as the union holds it. Valve's comment at <c>econ_item_view.cpp:62</c>: *"we
/// are networking the value as an int, even though it's a 'float', because really it isn't a
/// float. It's 32 raw bits."* Most attributes keep a float's bits here; a <c>stored_as_integer</c>
/// attribute keeps an integer, and reading it as a float produces a denormal rather than the
/// number — which is why the bits are the stored form and <see cref="Value"/> is derived.
/// </param>
public readonly record struct EconAttributeValue(int DefinitionIndex, int RawBits)
{
    /// <summary>The bits read as the float most attributes are.</summary>
    public float Value => BitConverter.Int32BitsToSingle(RawBits);

    /// <summary>The bits read as the integer a <c>stored_as_integer</c> attribute is.</summary>
    public int AsInteger => RawBits;
}

/// <summary>Everything the WIRE contributes to an entity's attribute resolution.</summary>
/// <param name="Local">Branch 1's list — <c>m_AttributeList</c>, which overrides.</param>
/// <param name="NetworkedForDemos">Branch 3's list.</param>
/// <param name="HasValidItemId">
/// Branch 3's guard: whether <c>m_iItemIDHigh</c>/<c>m_iItemIDLow</c> arrived and are not the
/// all-ones <c>INVALID_ITEM_ID</c>. Never-sent counts as invalid, which routes an era demo — no
/// econ system at all — to the definition's own attributes.
/// </param>
/// <remarks>
/// **Carried unresolved, because branch 4 lives in another layer.** The definition's attributes
/// come from <c>items_game.txt</c>, which Core cannot read — so the track carries the wire's three
/// inputs and the consumer completes <see cref="EconAttributes.Resolve"/> with the schema's list.
/// Resolving here with an empty branch 4 would bake "no definition attributes" into every prop and
/// look finished.
/// </remarks>
public sealed record EconAttributeWire(
    IReadOnlyList<EconAttributeValue> Local,
    IReadOnlyList<EconAttributeValue> NetworkedForDemos,
    bool HasValidItemId)
{
    /// <summary>Structural equality, because consumers dedup on it.</summary>
    /// <remarks>
    /// The record default compares the LISTS by reference, and `RecordViewmodels` keeps a sample
    /// only when `before == weapon` says something changed — fresh lists every tick would make
    /// every tick "changed" and record the entire demo as viewmodel samples. Sequence comparison
    /// over lists of at most twenty is what the dedup actually means.
    /// </remarks>
    public bool Equals(EconAttributeWire? other) =>
        other is not null
        && HasValidItemId == other.HasValidItemId
        && SameValues(Local, other.Local)
        && SameValues(NetworkedForDemos, other.NetworkedForDemos);

    /// <summary>Element-wise comparison, in the parse path where LINQ does not belong.</summary>
    private static bool SameValues(
        IReadOnlyList<EconAttributeValue> left, IReadOnlyList<EconAttributeValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(HasValidItemId);

        foreach (EconAttributeValue attribute in Local)
        {
            hash.Add(attribute);
        }

        foreach (EconAttributeValue attribute in NetworkedForDemos)
        {
            hash.Add(attribute);
        }

        return hash.ToHashCode();
    }
}

/// <summary>One item a player carries or wears — an attribute provider.</summary>
/// <param name="EntityIndex">Its entity slot.</param>
/// <param name="ClassName">Its server class, such as `CTFScatterGun`, or null when the class table did not name it.</param>
/// <param name="DefinitionIndex">`m_iItemDefinitionIndex`, or null when unsent.</param>
/// <param name="Wire">Its networked attribute inputs.</param>
/// <param name="IsWeapon">Whether it came from `m_hMyWeapons` — `PROVIDER_WEAPON`, which never provides to another weapon.</param>
public sealed record SceneItem(int EntityIndex, string? ClassName, int? DefinitionIndex, EconAttributeWire Wire, bool IsWeapon)
{
    /// <summary>`m_iEntityQuality` (econ_item_view.cpp:188), or null when unsent.</summary>
    public int? Quality { get; init; }

    /// <summary>`CTFWearable::m_bDisguiseWearable` (tf_item_wearable.cpp:33): worn only while disguised.</summary>
    public bool IsDisguiseWearable { get; init; }

    /// <summary>
    /// A pipebomb launcher's (and so a bow's) `m_flChargeBeginTime`, `DT_PipebombLauncherLocalData`
    /// (tf_weapon_pipebomblauncher.cpp:44) — its owner's alone; 0 when unsent or not charging.
    /// </summary>
    public float ChargeBeginTime { get; init; }

    /// <summary>`m_iPrimaryAmmoType` (`DT_LocalWeaponData`, its owner's alone), or null when unsent.</summary>
    public int? PrimaryAmmoType { get; init; }

    /// <summary>`m_flEffectBarRegenTime` (tf_weaponbase.cpp:180, `DT_LocalTFWeaponData`): when the effect bar is full, server clock.</summary>
    public float EffectBarRegenTime { get; init; }

    /// <summary>
    /// `m_flLastFireTime` (tf_weaponbase.cpp:179, `DT_LocalTFWeaponData`, its owner's alone): `FireProjectile`'s
    /// `gpGlobals->curtime` (tf_weaponbase_gun.cpp:354), server clock; 0 when unsent.
    /// </summary>
    public float LastFireTime { get; init; }

    /// <summary>`m_flEnergy` (tf_weaponbase.cpp:219): an energy weapon's charge.</summary>
    public float Energy { get; init; }

    /// <summary>`m_nKillComboClass` (tf_weaponbase.cpp:223): the class of the last kills in a combo.</summary>
    public int KillComboClass { get; init; }

    /// <summary>`m_nKillComboCount` (tf_weaponbase.cpp:224): how many kills of that class, 0..3.</summary>
    public int KillComboCount { get; init; }

    /// <summary>A knife's `m_bKnifeExists` (tf_weapon_knife.cpp:38): false while an icicle has melted.</summary>
    public bool KnifeExists { get; init; }

    /// <summary>A knife's `m_flKnifeMeltTimestamp` (:40), server clock.</summary>
    public float KnifeMeltTimestamp { get; init; }

    /// <summary>A knife's `m_flKnifeRegenerateDuration` (:39).</summary>
    public float KnifeRegenerateDuration { get; init; }

    /// <summary>A charged SMG's `m_flMinicritCharge` (tf_weapon_smg.cpp:42), 0..100.</summary>
    public float MinicritCharge { get; init; }

    /// <summary>A rocket pack's `m_bEnabled` (tf_weapon_rocketpack.cpp:44): `IsEnabled()`.</summary>
    public bool RocketPackEnabled { get; init; }

    /// <summary>A powerup bottle's `m_usNumCharges` (tf_item_powerup_bottle.cpp:33): `GetNumCharges()`.</summary>
    public int NumCharges { get; init; }

    /// <summary>
    /// A flamethrower's `m_iWeaponState` (tf_weapon_flamethrower.cpp:206), `FlameThrowerState_t`: 0 idle, 1 starting, 2
    /// firing, 3 secondary (tf_weapon_flamethrower.h:31-38); 0 when unsent.
    /// </summary>
    public int FlameThrowerState { get; init; }

    /// <summary>
    /// A medigun's `m_flChargeLevel`, 0..1: `DT_LocalTFWeaponMedigunData` to its owner (tf_weapon_medigun.cpp:128), else
    /// `DT_TFWeaponMedigunDataNonLocal` (:139); 0 when unsent.
    /// </summary>
    public float ChargeLevel { get; init; }

    /// <summary>A medigun's `m_bChargeRelease` (tf_weapon_medigun.cpp:152): `IsReleasingCharge()`.</summary>
    public bool ChargeRelease { get; init; }

    /// <summary>A medigun's `m_nChargeResistType` (tf_weapon_medigun.cpp:154): the Vaccinator's resist.</summary>
    public int ChargeResistType { get; init; }
}

/// <summary>Resolving which attribute list answers, as the engine resolves it.</summary>
public static class EconAttributes
{
    /// <summary>Merges the sources in <c>CEconItemView::IterateAttributes</c>'s order.</summary>
    /// <param name="local">Branch 1 — <c>m_AttributeList</c>, which always runs and overrides.</param>
    /// <param name="networkedForDemos">Branch 3's list.</param>
    /// <param name="hasValidItemId">
    /// Branch 3's other guard: <c>GetItemID() != INVALID_ITEM_ID</c>, where invalid is
    /// <c>(itemid_t)-1</c>. An item that never sent an id counts as invalid.
    /// </param>
    /// <param name="definitionAttributes">Branch 4 — the item definition's own attributes.</param>
    /// <returns>The resolved attributes, first-writer-wins per definition index.</returns>
    /// <remarks>
    /// **The chain, `econ_item_view.cpp:523`, with branch 2 deliberately absent** — SOC data is the
    /// local player's own inventory and cannot exist in a recording, which is the whole reason
    /// branch 3's list is networked. The wrapper
    /// (`CEconItemAttributeIterator_EconItemViewWrapper`) suppresses definitions branch 1 already
    /// produced, so this is first-writer-wins per definition index. Branches 3 and 4 are an
    /// `else if`: taking the demo list forecloses the definition's attributes even for indices the
    /// demo list does not carry.
    /// </remarks>
    public static IReadOnlyList<EconAttributeValue> Resolve(
        IReadOnlyList<EconAttributeValue> local,
        IReadOnlyList<EconAttributeValue> networkedForDemos,
        bool hasValidItemId,
        IReadOnlyList<EconAttributeValue> definitionAttributes)
    {
        List<EconAttributeValue> resolved = [];

        foreach (EconAttributeValue attribute in Iterate(local, networkedForDemos, hasValidItemId, definitionAttributes))
        {
            resolved.Add(attribute);
        }

        return resolved;
    }

    /// <summary>The same attributes in the same order as <see cref="Resolve"/>, walked without allocating.</summary>
    /// <param name="local">Branch 1.</param>
    /// <param name="networkedForDemos">Branch 3's list.</param>
    /// <param name="hasValidItemId">Branch 3's other guard.</param>
    /// <param name="definitionAttributes">Branch 4.</param>
    /// <returns>A walk to <c>foreach</c> over; its enumerator is a struct, so nothing is boxed.</returns>
    /// <remarks>
    /// **The engine's own shape: `IterateAttributes` streams into an iterator and allocates nothing**
    /// (`econ_item_view.cpp:523-600`). Its wrapper suppresses a definition branch 1 already produced by asking the local
    /// list, `GetAttributeList()->GetAttributeByID`, a linear search — so the duplicate test here is a linear search of
    /// what came before rather than a set (B599). Lists hold at most a few dozen entries.
    /// </remarks>
    public static ResolvedAttributes Iterate(
        IReadOnlyList<EconAttributeValue> local,
        IReadOnlyList<EconAttributeValue> networkedForDemos,
        bool hasValidItemId,
        IReadOnlyList<EconAttributeValue> definitionAttributes)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(networkedForDemos);
        ArgumentNullException.ThrowIfNull(definitionAttributes);

        // `bHasNetworkedAttribsForDemos` is noted from the list's own count; the else-if chain
        // means exactly one of the two fallbacks runs.
        return new ResolvedAttributes(
            local,
            hasValidItemId && networkedForDemos.Count > 0 ? networkedForDemos : definitionAttributes);
    }
}

/// <summary>An item's attributes, first-writer-wins per definition index: <see cref="EconAttributes.Iterate"/>.</summary>
/// <param name="Local">Branch 1, which overrides.</param>
/// <param name="Fallback">Whichever of branch 3 or 4 the else-if chose.</param>
public readonly record struct ResolvedAttributes(IReadOnlyList<EconAttributeValue> Local, IReadOnlyList<EconAttributeValue> Fallback)
    : IEnumerable<EconAttributeValue>
{
    /// <summary>The walk, for <c>foreach</c>.</summary>
    /// <returns>A struct enumerator; through the interface it is boxed, so hot callers take this type.</returns>
    public Enumerator GetEnumerator() => new(Local, Fallback);

    IEnumerator<EconAttributeValue> IEnumerable<EconAttributeValue>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Walks the local list, then the fallback, skipping any definition index already produced.</summary>
    public struct Enumerator : IEnumerator<EconAttributeValue>
    {
        private readonly IReadOnlyList<EconAttributeValue> _local;
        private readonly IReadOnlyList<EconAttributeValue> _fallback;
        private int _index;

        internal Enumerator(IReadOnlyList<EconAttributeValue> local, IReadOnlyList<EconAttributeValue> fallback)
        {
            _local = local;
            _fallback = fallback;
            _index = -1;
            Current = default;
        }

        /// <summary>The attribute the walk is on.</summary>
        public EconAttributeValue Current { get; private set; }

        readonly object System.Collections.IEnumerator.Current => Current;

        /// <summary>Back to before the first attribute.</summary>
        public void Reset() => _index = -1;

        /// <summary>Nothing to release.</summary>
        public readonly void Dispose()
        {
            // A walk over two lists holds nothing.
        }

        /// <summary>Steps to the next attribute whose definition nothing before it produced.</summary>
        /// <returns>False when both lists are done.</returns>
        public bool MoveNext()
        {
            int locals = _local?.Count ?? 0;
            int total = locals + (_fallback?.Count ?? 0);

            while (++_index < total)
            {
                bool local = _index < locals;
                int at = local ? _index : _index - locals;
                EconAttributeValue candidate = local ? _local![at] : _fallback![at];
                int definition = candidate.DefinitionIndex;

                if (!Holds(_local!, local ? at : locals, definition) && (local || !Holds(_fallback!, at, definition)))
                {
                    Current = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the first <paramref name="count"/> entries name <paramref name="definition"/>.</summary>
        private static bool Holds(IReadOnlyList<EconAttributeValue> list, int count, int definition)
        {
            for (int i = 0; i < count; i++)
            {
                if (list[i].DefinitionIndex == definition)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>Which of an item's two networked attribute lists is being asked about.</summary>
/// <remarks>
/// **<c>DT_ScriptCreatedItem</c> embeds <c>DT_AttributeList</c> twice**
/// (<c>econ_item_view.cpp:191,193</c>), and <c>CEconItemView::IterateAttributes</c> (<c>:523</c>)
/// fixes their precedence: the local list is read first and overrides; the networked-for-demos
/// list is the fallback when there is no SOC data — which, in a recording, is always.
/// </remarks>
public enum EconAttributeList
{
    /// <summary><c>m_AttributeList</c> — local copies, which override everything else.</summary>
    Local,

    /// <summary>
    /// <c>m_NetworkedDynamicAttributesForDemos</c> — networked so a DEMO can resolve attributes
    /// without the inventory backend the live client asks. The name is Valve's, and it is this
    /// project's whole reason for reading the list.
    /// </summary>
    NetworkedForDemos,
}
