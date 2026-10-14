using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The part of a <see cref="ScenePlayer"/> that rarely changes from one tick to the next, held by reference so
/// consecutive frames can share one copy (D217). Each member is documented where it is read, on <see cref="ScenePlayer"/>.
/// </summary>
/// <remarks>
/// **Measured before the split**: `ScenePlayer` was an 800-byte struct, f12's timeline held 1,255,344 of them — one per
/// player per frame, 1,498 MB of a 2,948 MB timeline — and most of those bytes were these fields, identical to the tick
/// before. The positional half (origin, angles, conditions) changes every tick and stays inline; this half is a
/// reference, and `DemoTimeline` hands a frame the previous frame's instance whenever every value is equal, the way
/// B433's `SameAsBefore` already shares item lists.
///
/// **Record equality is unchanged**: lists still compare by reference, as they did on the struct. Only the sharing
/// test, <see cref="SameValues"/>, compares them element by element.
/// </remarks>
internal sealed record ScenePlayerDetail
{
    /// <summary>Every member at its default — what a <see cref="ScenePlayer"/> with none set reads.</summary>
    public static readonly ScenePlayerDetail Empty = new();

    public TfPosture Posture { get; init; }
    public int? MaxHealth { get; init; }
    public int? MaxHealthForBuffing { get; init; }
    public int? EntityHealth { get; init; }
    public int? HideHud { get; init; }
    public int? WeaponClip1 { get; init; }
    public int? WeaponPrimaryAmmoType { get; init; }
    public IReadOnlyList<int>? Ammo { get; init; }
    public IReadOnlyList<SceneItem>? Items { get; init; }
    public IReadOnlyList<EconAttributeValue>? OwnAttributes { get; init; }
    public int? DisguiseTarget { get; init; }
    public int? DisguiseHealth { get; init; }
    public int? PlayerState { get; init; }
    public bool CarryingObject { get; init; }
    public int? StunFlags { get; init; }
    public int? StunIndex { get; init; }
    public bool IsMiniBoss { get; init; }
    public int? ActiveWeaponClip { get; init; }
    public float Gravity { get; init; }
    public int? KillStreak { get; init; }
    public float? InvisChangeCompleteTime { get; init; }
    public string? ScreenOverlayMaterial { get; init; }
    public float? CloakMeter { get; init; }
    public int? DisguiseWeapon { get; init; }
    public SceneItem? DisguiseWeaponItem { get; init; }
    public int? PlayerSkinOverride { get; init; }
    public int? DisguiseSkinOverride { get; init; }
    public int? Decapitations { get; init; }
    public float? RageMeter { get; init; }
    public bool RageDraining { get; init; }
    public IReadOnlyList<float>? ItemChargeMeter { get; init; }
    public float? HypeMeter { get; init; }
    public int? RevengeCrits { get; init; }
    public float? RuneCharge { get; init; }
    public float? KartNextAvailableBoost { get; init; }
    public int? KartHealth { get; init; }
    public int? SpawnCounter { get; init; }
    public uint? WeaponAccountId { get; init; }
    public float DeathTime { get; init; }
    public int? WeaponQuality { get; init; }
    public bool HasTheFlag { get; init; }
    public (float Charge, int Quality, int? Definition)? Medigun { get; init; }
    public (int? HealTarget, float Charge)? ActiveMedigun { get; init; }
    public int? Fov { get; init; }
    public int? FovStart { get; init; }
    public float? FovTime { get; init; }
    public float? FovRate { get; init; }
    public int? DefaultFov { get; init; }
    public float? ViewOffsetZ { get; init; }
    public float? MovementStunTime { get; init; }
    public int? MovementStunAmount { get; init; }
    public int? MovementStunParity { get; init; }
    public bool AllowMoveDuringTaunt { get; init; }
    public float? CurrentTauntMoveSpeed { get; init; }
    public float? VehicleReverseTime { get; init; }
    public int? GrapplingHookTarget { get; init; }
    public int? TauntItemDefIndex { get; init; }
    public int? ActiveTauntSlot { get; init; }

    /// <summary>Null for a detail equal to <see cref="Empty"/>, so a player with nothing set equals one built with defaults.</summary>
    /// <param name="detail">The detail.</param>
    /// <returns>The detail, or null.</returns>
    public static ScenePlayerDetail? Normal(ScenePlayerDetail? detail) => detail == Empty ? null : detail;

    /// <summary>Whether every value equals another's, lists compared element by element — the test for sharing one copy.</summary>
    /// <param name="other">The other detail, or null.</param>
    /// <returns>Whether a reader could tell the two apart by any value.</returns>
    public bool SameValues(ScenePlayerDetail? other) =>
        other is not null
        && Same(Ammo, other.Ammo)
        && Same(Items, other.Items)
        && Same(OwnAttributes, other.OwnAttributes)
        && Same(ItemChargeMeter, other.ItemChargeMeter)
        && this with { Ammo = null, Items = null, OwnAttributes = null, ItemChargeMeter = null }
            == other with { Ammo = null, Items = null, OwnAttributes = null, ItemChargeMeter = null };

    private static bool Same<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null && left.SequenceEqual(right));
}
