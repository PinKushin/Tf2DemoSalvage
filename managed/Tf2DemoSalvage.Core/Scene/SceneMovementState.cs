namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// The local player's networked movement state that <c>CPrediction</c> restores before re-running his usercmds (D205):
/// <c>DT_Local</c> (<c>playerlocaldata.cpp:30-35</c>, <c>:49</c>, <c>:59</c>), <c>DT_LocalPlayerExclusive</c>
/// (<c>c_baseplayer.cpp:236</c>, <c>:246</c>) and <c>DT_TFPlayerShared</c>'s duck timer, air ducks and air dash.
/// </summary>
/// <remarks>Only the recorder receives these tables, so only his <see cref="ScenePlayer"/> carries one.</remarks>
public readonly record struct SceneLocalMovement
{
    /// <summary><c>m_Local.m_bDucked</c>.</summary>
    public bool Ducked { get; init; }

    /// <summary><c>m_Local.m_bDucking</c>.</summary>
    public bool Ducking { get; init; }

    /// <summary><c>m_Local.m_bInDuckJump</c>.</summary>
    public bool InDuckJump { get; init; }

    /// <summary><c>m_Local.m_flDucktime</c>, milliseconds counting down from <c>GAMEMOVEMENT_DUCK_TIME</c>.</summary>
    public float DuckTime { get; init; }

    /// <summary><c>m_Local.m_flDuckJumpTime</c>.</summary>
    public float DuckJumpTime { get; init; }

    /// <summary><c>m_Local.m_flJumpTime</c>.</summary>
    public float JumpTime { get; init; }

    /// <summary><c>m_Local.m_flFallVelocity</c>.</summary>
    public float FallVelocity { get; init; }

    /// <summary><c>m_Local.m_bAllowAutoMovement</c>; true until sent, as <c>CBasePlayer::Spawn</c> sets it.</summary>
    public bool AllowAutoMovement { get; init; }

    /// <summary><c>m_vecBaseVelocity</c>.</summary>
    public (float X, float Y, float Z) BaseVelocity { get; init; }

    /// <summary><c>m_nTickBase</c>: prediction's <c>curtime</c> is this times the tick interval; null when unsent.</summary>
    public int? TickBase { get; init; }

    /// <summary><c>m_Shared.m_flDuckTimer</c>, in <c>curtime</c>'s seconds; 0 in an era that has none.</summary>
    public float DuckTimer { get; init; }

    /// <summary><c>m_Shared.m_nAirDucked</c>; 0 in an era that has none.</summary>
    public int AirDucked { get; init; }

    /// <summary><c>m_Shared.m_iAirDash</c>, or the launch era's <c>m_bAirDash</c>.</summary>
    public int AirDash { get; init; }
}

/// <summary>
/// An entity's solidity as the client receives it: <c>m_Collision.m_nSolidType</c> and <c>m_usSolidFlags</c>
/// (<c>collisionproperty.cpp:388-389</c>) and <c>m_CollisionGroup</c> (<c>c_baseentity.cpp:459</c>).
/// </summary>
/// <param name="SolidType"><c>SolidType_t</c>: <c>SOLID_BSP</c> 1, <c>SOLID_BBOX</c> 2 (<c>const.h:239-240</c>).</param>
/// <param name="SolidFlags"><c>FSOLID_*</c>; <c>FSOLID_NOT_SOLID</c> is 4 (<c>const.h:252</c>).</param>
/// <param name="CollisionGroup"><c>Collision_Group_t</c> (<c>const.h:398</c>) or TF's (<c>tf_shareddefs.h:1317</c>).</param>
public readonly record struct SceneCollision(int SolidType, int SolidFlags, int CollisionGroup)
{
    /// <summary><c>m_vecMins</c> (<c>collisionproperty.cpp:372</c>): the scaled OBB minimum, in collision space.</summary>
    public (float X, float Y, float Z) Mins { get; init; }

    /// <summary><c>m_vecMaxs</c> (<c>collisionproperty.cpp:373</c>).</summary>
    public (float X, float Y, float Z) Maxs { get; init; }

    /// <summary>
    /// <c>IsBoundsDefinedInEntitySpace</c> (<c>collisionproperty.h:340-344</c>): not <c>FSOLID_FORCE_WORLD_ALIGNED</c> (64),
    /// and neither <c>SOLID_BBOX</c> (2) nor <c>SOLID_NONE</c> (0) — the OBB turns with the entity.
    /// </summary>
    public bool BoundsInEntitySpace => (SolidFlags & 64) == 0 && SolidType is not (2 or 0);
}
