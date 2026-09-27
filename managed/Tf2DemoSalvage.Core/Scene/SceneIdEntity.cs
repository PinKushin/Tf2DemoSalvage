namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// An entity the target ID's generic branch reads (tf_hud_target_id.cpp:926-998): a `CTFDroppedWeapon`
/// (tf_dropped_weapon.cpp:40) or a `CTFReviveMarker` (tf_revive.cpp:42), with the collision box the ID trace stops on.
/// </summary>
/// <param name="EntityIndex">Its entity slot.</param>
/// <param name="Kind">Which of the two it is.</param>
public readonly record struct SceneIdEntity(int EntityIndex, SceneIdEntityKind Kind)
{
    /// <summary>`DT_BaseEntity.m_iTeamNum`.</summary>
    public int? Team { get; init; }

    /// <summary>A revive marker's `m_iHealth`.</summary>
    public int Health { get; init; }

    /// <summary>A revive marker's `m_iMaxHealth`.</summary>
    public int MaxHealth { get; init; }

    /// <summary>A revive marker's `m_hOwner` slot: the dead player it revives.</summary>
    public int? OwnerEntityIndex { get; init; }

    /// <summary>A dropped weapon's `m_Item.m_bInitialized`: `CEconItemView::IsValid()`.</summary>
    public bool ItemValid { get; init; }

    /// <summary>A dropped weapon's `m_Item.m_iItemDefinitionIndex`.</summary>
    public int? ItemDefinition { get; init; }

    /// <summary>A dropped weapon's `m_Item.m_iEntityQuality`.</summary>
    public int ItemQuality { get; init; }

    /// <summary>A dropped weapon's `m_Item.m_iAccountID`: who owned it.</summary>
    public uint AccountId { get; init; }

    /// <summary>A dropped weapon's `m_flChargeLevel`: a medigun's banked charge, 0 to 1.</summary>
    public float ChargeLevel { get; init; }

    /// <summary>A dropped weapon's model path, from `m_nModelIndex`, or null.</summary>
    public string? Model { get; init; }

    /// <summary>`GetAbsOrigin()`, when sent.</summary>
    public (float X, float Y, float Z)? Position { get; init; }

    /// <summary>`m_angRotation`, when sent: a physics-simulated dropped weapon's box turns with it.</summary>
    public (float Pitch, float Yaw, float Roll)? Angles { get; init; }

    /// <summary>`m_Collision.m_vecMins`.</summary>
    public (float X, float Y, float Z)? Mins { get; init; }

    /// <summary>`m_Collision.m_vecMaxs`.</summary>
    public (float X, float Y, float Z)? Maxs { get; init; }

    /// <summary>`m_Collision.m_nSolidType`: `SOLID_VPHYSICS` (6) for a dropped weapon, `SOLID_BBOX` (2) for a marker.</summary>
    public int SolidType { get; init; }

    /// <summary>`m_Collision.m_usSolidFlags`.</summary>
    public int SolidFlags { get; init; }

    /// <summary>Whether a trace can stop on it: some solid type, and not `FSOLID_NOT_SOLID` (4).</summary>
    public bool IsSolid => SolidType != 0 && (SolidFlags & 4) == 0;
}

/// <summary>Which entity a <see cref="SceneIdEntity"/> is.</summary>
public enum SceneIdEntityKind
{
    /// <summary>`CTFDroppedWeapon`.</summary>
    DroppedWeapon,

    /// <summary>`CTFReviveMarker`.</summary>
    ReviveMarker,
}
