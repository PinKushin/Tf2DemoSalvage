using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Prediction;

/// <summary><c>UTIL_TraceRay</c> for a player box placed by its origin and bounds; null when there is no world (D205).</summary>
/// <param name="start">The box's origin at the start.</param>
/// <param name="end">Its origin at the end.</param>
/// <param name="mins">Its low corner relative to the origin.</param>
/// <param name="maxs">Its high corner.</param>
/// <param name="mask">The contents that stop it.</param>
/// <returns>The trace, its fraction of the whole move; null with no world to trace against.</returns>
public delegate BspTrace? PlayerTraceRay(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs, int mask);

/// <summary>The local player's movement state as <c>CGameMovement</c> reads and writes it (D205).</summary>
/// <remarks>
/// C# names for <c>C_TFPlayer</c>'s fields (D163): <c>GetAbsOrigin</c>, <c>m_vecVelocity</c>, <c>m_hGroundEntity</c>
/// as <see cref="OnGround"/>, <c>m_Local.m_bDucked</c>, <c>m_bDucking</c>, <c>m_bInDuckJump</c>,
/// <c>m_flDucktime</c>, <c>m_flDuckJumpTime</c>, <c>m_flJumpTime</c>, <c>FL_DUCKING</c>, <c>m_nOldButtons</c>,
/// <c>m_flMaxspeed</c>, <c>m_surfaceFriction</c>, <c>m_flFallVelocity</c>, and <c>m_Shared</c>'s air dash, air duck
/// count and duck timer.
/// </remarks>
public record struct PredictedPlayer
{
    public PredictedPlayer()
    {
    }

    public Vector3 Origin { get; set; }

    public Vector3 Velocity { get; set; }

    public Vector3 BaseVelocity { get; set; }

    public bool OnGround { get; set; }

    public bool Ducked { get; set; }

    public bool Ducking { get; set; }

    public bool InDuckJump { get; set; }

    public bool FlDucking { get; set; }

    public float DuckTime { get; set; }

    public float DuckJumpTime { get; set; }

    public float JumpTime { get; set; }

    public uint OldButtons { get; set; }

    public float OldForwardMove { get; set; }

    /// <summary><c>m_flMaxspeed</c>: the server's <c>TeamFortress_CalculateMaxSpeed</c>, networked — class, conditions and attributes already in it.</summary>
    public float MaxSpeed { get; set; }

    public float SurfaceFriction { get; set; } = 1f;

    public float FallVelocity { get; set; }

    public int PlayerClass { get; set; }

    public PlayerConditions Conditions { get; set; }

    /// <summary><c>m_Shared.m_nPlayerState</c>; null reads as <c>TF_STATE_ACTIVE</c>.</summary>
    public int? PlayerState { get; set; }

    public bool IsDead { get; set; }

    /// <summary><c>GetWaterLevel()</c>: networked, then <c>CheckWater</c>'s; above <c>WL_Feet</c> (1) the swim code runs.</summary>
    public int WaterLevel { get; set; }

    public int AirDash { get; set; }

    public int AirDucked { get; set; }

    /// <summary><c>m_Shared.GetDuckTimer()</c>, in <see cref="CurTime"/>'s seconds.</summary>
    public float DuckTimer { get; set; }

    /// <summary><c>gpGlobals->curtime</c>: <c>m_nTickBase · TICK_INTERVAL</c> during prediction.</summary>
    public float CurTime { get; set; }

    // ---- The movement modes (B450). ----

    /// <summary><c>GetViewOffset().z</c>: <c>m_vecViewOffset[2]</c>, <c>DT_LocalPlayerExclusive</c> — <c>CheckWater</c>'s eye point.</summary>
    public float ViewOffsetZ { get; set; }

    /// <summary><c>GetWaterType()</c>: the contents <c>CheckWater</c> found at the feet.</summary>
    public int WaterType { get; set; }

    /// <summary><c>m_flWaterJumpTime</c>, milliseconds left of a jump out of water.</summary>
    public float WaterJumpTime { get; set; }

    /// <summary><c>m_vecWaterJumpVel</c>.</summary>
    public Vector3 WaterJumpVelocity { get; set; }

    /// <summary>
    /// <c>FL_WATERJUMP</c> arrived set: the client's <c>m_flWaterJumpTime</c> is a <c>DEFINE_FIELD</c>, neither sent nor
    /// restored (<c>c_baseplayer.cpp:383</c>), so how long is left is not in the demo and the move is declined.
    /// </summary>
    public bool WaterJumpUnknown { get; set; }

    /// <summary><c>GetActiveStunInfo() != NULL</c>: <c>m_iStunIndex &gt;= 0</c> on the client (<c>tf_player_shared.cpp:7475</c>).</summary>
    public bool StunActive { get; set; }

    /// <summary><c>m_iMovementStunAmount</c>, 0..255: the active stun's <c>flStunAmount</c> (<c>:7462</c>).</summary>
    public int StunAmount { get; set; }

    /// <summary><c>m_iStunFlags</c>: <c>TF_STUN_*</c>.</summary>
    public int StunFlags { get; set; }

    /// <summary><c>m_flStunEnd</c>: the client's curtime when the stun parity changed plus its duration (<c>:1440-1449</c>).</summary>
    public float StunExpireTime { get; set; }

    /// <summary><c>m_Shared.m_flStunLerpTarget</c>, client state <c>StunMove</c> keeps between commands.</summary>
    public float StunLerpTarget { get; set; }

    /// <summary><c>m_Shared.m_flLastMovementStunChange</c>; zero when no fade is running.</summary>
    public float LastMovementStunChange { get; set; }

    /// <summary><c>m_Shared.m_bStunNeedsFadeOut</c>.</summary>
    public bool StunNeedsFadeOut { get; set; }

    /// <summary>The active weapon is <c>TF_WEAPON_MINIGUN</c>, which a control-stunned heavy may still spin.</summary>
    public bool ActiveWeaponIsMinigun { get; set; }

    /// <summary><c>HasTheFlag()</c>.</summary>
    public bool HasTheFlag { get; set; }

    /// <summary><c>m_bAllowMoveDuringTaunt</c> (<c>c_tf_player.cpp:3793</c>).</summary>
    public bool AllowMoveDuringTaunt { get; set; }

    /// <summary>The taunt item's movement attributes, or null when they are not known.</summary>
    public TauntMovement? TauntMovement { get; set; }

    /// <summary><c>m_flCurrentTauntMoveSpeed</c>, networked and predicted (<c>c_tf_player.cpp:3800, 3853</c>).</summary>
    public float CurrentTauntMoveSpeed { get; set; }

    /// <summary><c>m_flVehicleReverseTime</c>, networked and predicted (<c>:3801, 3854</c>); <c>FLT_MAX</c> when unset.</summary>
    public float VehicleReverseTime { get; set; } = float.MaxValue;

    /// <summary><c>GetGrapplingHookTarget()</c>: <c>m_hGrapplingHookTarget</c> resolved, or null.</summary>
    public GrapplingTarget? GrapplingHook { get; set; }
}

/// <summary>
/// The taunt attributes <c>ParseSharedTauntDataFromEconItemView</c> reads (<c>tf_player_shared.cpp:13156</c>):
/// <c>"taunt force move forward"</c>, <c>"taunt move speed"</c>, <c>"taunt move acceleration time"</c>.
/// </summary>
/// <param name="ForceForward"><c>IsTauntForceMovingForward()</c>.</param>
/// <param name="Speed"><c>GetTauntMoveSpeed()</c>.</param>
/// <param name="Acceleration"><c>GetTauntMoveAcceleration()</c>, seconds to full speed.</param>
public sealed record TauntMovement(bool ForceForward, float Speed, float Acceleration);

/// <summary>What <c>GrapplingHookMove</c> reads of the hook's target.</summary>
/// <param name="Center">Its <c>WorldSpaceCenter()</c>.</param>
/// <param name="Origin">Its <c>GetAbsOrigin()</c>.</param>
/// <param name="IsPlayer">Whether it is a player.</param>
/// <param name="HookDirection">
/// For a player who is grappling too, the normalised direction from his centre to his own hook's target; null otherwise.
/// </param>
public sealed record GrapplingTarget(Vector3 Center, Vector3 Origin, bool IsPlayer, Vector3? HookDirection = null);

/// <summary>The replicated ConVars <c>CGameMovement</c> reads, as the server set them (D106, D205).</summary>
public sealed record MovementConVars(
    float Gravity,
    float StopSpeed,
    float Accelerate,
    float AirAccelerate,
    float Friction,
    float Bounce,
    float MaxVelocity,
    float StepSize,
    float ForwardSpeed,
    float BackSpeed,
    float SideSpeed)
{
    /// <summary>Valve's declared defaults (<c>movevars_shared.cpp</c>, <c>in_main.cpp:76-79</c>).</summary>
    public static MovementConVars Defaults { get; } = From(new ServerConVars());

    /// <summary>The values a demo's server sent, falling back to the defaults.</summary>
    /// <param name="server">The demo's ConVars.</param>
    /// <returns>The movement set.</returns>
    public static MovementConVars From(ServerConVars server)
    {
        System.ArgumentNullException.ThrowIfNull(server);

        return new MovementConVars(
            server.Number("sv_gravity"),
            server.Number("sv_stopspeed"),
            server.Number("sv_accelerate"),
            server.Number("sv_airaccelerate"),
            server.Number("sv_friction"),
            server.Number("sv_bounce"),
            server.Number("sv_maxvelocity"),
            server.Number("sv_stepsize"),
            server.Number("cl_forwardspeed"),
            server.Number("cl_backspeed"),
            server.Number("cl_sidespeed"));
    }
}
