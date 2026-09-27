namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// An Engineer building — `C_BaseObject`, as `DT_BaseObject` sends it (tf_obj.cpp:158), plus the
/// per-type fields `CTargetID::UpdateID`'s object branch reads (tf_hud_target_id.cpp:719) through
/// `GetTargetIDString`/`GetTargetIDDataString` (c_baseobject.cpp:892,965).
/// </summary>
/// <param name="EntityIndex">Its entity slot.</param>
public readonly record struct SceneBuilding(int EntityIndex)
{
    /// <summary>`OBJ_DISPENSER` (tf_shareddefs.h:1368).</summary>
    public const int Dispenser = 0;

    /// <summary>`OBJ_TELEPORTER`.</summary>
    public const int Teleporter = 1;

    /// <summary>`OBJ_SENTRYGUN`.</summary>
    public const int Sentrygun = 2;

    /// <summary>`MODE_TELEPORTER_ENTRANCE` (tf_shareddefs.h:1389), a `CObjectTeleporter`'s mode.</summary>
    public const int TeleporterEntrance = 0;

    /// <summary>`MODE_TELEPORTER_EXIT`.</summary>
    public const int TeleporterExit = 1;

    /// <summary>`m_iHealth`.</summary>
    public int Health { get; init; }

    /// <summary>`m_iMaxHealth`.</summary>
    public int MaxHealth { get; init; }

    /// <summary>`m_iObjectType`: <see cref="Dispenser"/>, <see cref="Teleporter"/> or <see cref="Sentrygun"/>.</summary>
    public int ObjectType { get; init; }

    /// <summary>`m_iObjectMode`: a teleporter's <see cref="TeleporterEntrance"/> or <see cref="TeleporterExit"/>.</summary>
    public int ObjectMode { get; init; }

    /// <summary>`DT_BaseEntity.m_iTeamNum`, read the way every other entity's team is.</summary>
    public int? Team { get; init; }

    /// <summary>`m_hBuilder`'s slot: the building player, resolved through <c>EntityState.Slot</c>.</summary>
    public int? BuilderEntityIndex { get; init; }

    /// <summary>`m_bHasSapper`.</summary>
    public bool Sapped { get; init; }

    /// <summary>`m_bDisabled`: EMP'd, or (for a sentry) player-controlled shutdown.</summary>
    public bool Disabled { get; init; }

    /// <summary>`m_bBuilding`: still under construction.</summary>
    public bool Building { get; init; }

    /// <summary>`m_bPlacing`: in the not-yet-committed placement ghost.</summary>
    public bool Placing { get; init; }

    /// <summary>`m_bCarried`: picked up (a mini-sentry) and being carried.</summary>
    public bool Carried { get; init; }

    /// <summary>`m_bMiniBuilding`: a mini-sentry, per `GetTargetIDDataString`'s level-hiding check.</summary>
    public bool MiniBuilding { get; init; }

    /// <summary>`m_bDisposableBuilding`.</summary>
    public bool DisposableBuilding { get; init; }

    /// <summary>`m_iUpgradeLevel`: 1 to 3. `GetTargetIDDataString` hides this for a sentry, which has a model per level.</summary>
    public int UpgradeLevel { get; init; }

    /// <summary>`m_iUpgradeMetal`: metal banked toward the next level.</summary>
    public int UpgradeMetal { get; init; }

    /// <summary>`m_iUpgradeMetalRequired`.</summary>
    public int UpgradeMetalRequired { get; init; }

    /// <summary>`m_flPercentageConstructed`: 0 to 1 while <see cref="Building"/>.</summary>
    public float PercentageConstructed { get; init; }

    /// <summary>`DT_ObjectSentrygun.m_iAmmoShells`, or null for anything but a sentry.</summary>
    public int? SentryAmmoShells { get; init; }

    /// <summary>`DT_ObjectSentrygun.m_iAmmoRockets`, or null for anything but a (level 3) sentry.</summary>
    public int? SentryAmmoRockets { get; init; }

    /// <summary>`DT_ObjectDispenser.m_iAmmoMetal`, or null for anything but a dispenser.</summary>
    public int? DispenserAmmoMetal { get; init; }

    /// <summary>`DT_ObjectTeleporter.m_iState` (a `teleporter_state_t`), or null for anything but a teleporter.</summary>
    public int? TeleporterState { get; init; }

    /// <summary>`DT_ObjectTeleporter.m_flRechargeTime` (tf_obj_teleporter.cpp:51), on the server clock; null for anything else.</summary>
    public float? TeleporterRechargeTime { get; init; }

    /// <summary>`DT_ObjectTeleporter.m_flCurrentRechargeDuration` (:52); null for anything but a teleporter.</summary>
    public float? TeleporterRechargeDuration { get; init; }

    /// <summary>`m_Collision.m_vecMins`: the bounding box `UTIL_SetSize` set (e.g. tf_obj_sentrygun.cpp:2149), origin-relative.</summary>
    public (float X, float Y, float Z)? Mins { get; init; }

    /// <summary>`m_Collision.m_vecMaxs`.</summary>
    public (float X, float Y, float Z)? Maxs { get; init; }

    /// <summary>`m_Collision.m_nSolidType` (collisionproperty.cpp:388): `SOLID_BBOX` 2 for a standing building, 0 unsent.</summary>
    public int SolidType { get; init; }

    /// <summary>`m_Collision.m_usSolidFlags` (:389): `FSOLID_NOT_SOLID` (4) while a blueprint is placed (tf_obj.cpp:881).</summary>
    public int SolidFlags { get; init; }

    /// <summary>Whether a trace can stop on it: some solid type, and not `FSOLID_NOT_SOLID`.</summary>
    public bool IsSolid => SolidType != 0 && (SolidFlags & 4) == 0;

    /// <summary>The world position, when the entity has sent one.</summary>
    public (float X, float Y, float Z)? Position { get; init; }
}
