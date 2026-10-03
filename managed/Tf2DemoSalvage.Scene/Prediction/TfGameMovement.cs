using System;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Prediction;

/// <summary>
/// <c>CTFGameMovement</c> over <c>CGameMovement</c>, ported for prediction of the POV recorder's velocity (D205).
/// </summary>
/// <remarks>
/// **Read in full:** <c>src/game/shared/gamemovement.cpp</c> and <c>src/game/shared/tf/tf_gamemovement.cpp</c>. Every
/// method keeps Valve's name and order, merged into one class because the TF overrides replace most of the base.
///
/// **Ported:** <c>ProcessMovement</c>, <c>ChargeMove</c>, <c>HighMaxSpeedMove</c>, <c>PlayerMove</c>,
/// <c>CheckParameters</c>, <c>ReduceTimers</c>, <c>CategorizePosition</c> (TF's, with <c>TracePlayerBBoxForGround</c>
/// and the stair snap), <c>Duck</c> (TF's <c>DuckOverrides</c>, <c>OnDuck</c>, <c>OnUnDuck</c> with the base
/// <c>FinishDuck</c>, <c>FinishUnDuck</c>, <c>CanUnduck</c>, <c>FixPlayerCrouchStuck</c>,
/// <c>HandleDuckingSpeedCrop</c>), <c>FullWalkMove</c>, <c>CheckJumpButton</c> with <c>AirDash</c> and
/// <c>PreventBunnyJumping</c>, <c>Friction</c>, <c>WalkMove</c>, <c>AirMove</c>, <c>Accelerate</c>,
/// <c>AirAccelerate</c>, <c>GetAirSpeedCap</c>, <c>TryPlayerMove</c>, <c>ClipVelocity</c>, <c>StepMove</c>,
/// <c>StartGravity</c>, <c>FinishGravity</c>, <c>CheckVelocity</c>, <c>SetGroundEntity</c> with its base velocity,
/// <c>PlayerSolidMask</c>, and <c>CheckStuck</c> (TF's over the base's, with <c>CheckInterval</c> and the stuck table).
///
/// **The modes besides walking** (B450): <c>StunMove</c>, <c>TauntMove</c> with <c>VehicleMove</c>,
/// <c>GrapplingHookMove</c>, <c>CheckWater</c>, <c>FullWalkMoveUnderwater</c>, <c>WaterMove</c>, <c>CheckWaterJump</c>,
/// <c>WaterJump</c>, <c>CheckWaterJumpButton</c>, the ghost and parachute branches, <c>CheckKartWallBumping</c>'s clamp and
/// <c>PlayerSolidMask</c>. **Declined, so the networked velocity stands** (<see cref="ProcessMovement"/> returns false):
/// a water jump already running, whose clock the demo does not carry, and a moving taunt whose item attributes are not
/// known. What else is taken as a default is filed under B450.
///
/// **Read off the player** (B450): item attributes through <see cref="Items"/> (<c>mod_jump_height</c> and its weapon form,
/// <c>mod_air_control</c> and its blast-jump form, <c>CanAirDash</c>, <c>CanJump</c>, <c>CanDuck</c>), the ground's surfaceprop
/// through <see cref="GroundSurface"/>, and <c>m_flGravity</c>.
///
/// **Read off the player too** (B450): <c>GetMovementForwardPull</c> from the active weapon's fire state, the Atomizer's
/// deploy-time dash test, <c>hype_resets_on_jump</c> with the Baby Face's speed term, <c>IsLoser</c>'s duck crop, and
/// <c>tf_clamp_airducks</c>. **Taken as a default:** the game rules' gravity multiplier (1, never on the wire). A moving
/// ground's velocity is zero because the client's is (<see cref="SetGroundEntity"/>).
/// </remarks>
public sealed class TfGameMovement
{
    private const float TfMaxSpeed = 400f * 1.3f;
    private const float DistEpsilon = 0.03125f;
    private const float CoordResolution = 1f / 32f;
    private const float GameMovementDuckTime = 1000f;
    private const float TimeToDuck = 0.2f;
    private const float TimeToUnduck = 0.2f;
    private const float TfTimeToDuck = 0.3f;
    private const int TfAirDuckedCount = 2;
    private const float AirDashZ = 268.3281572999747f;
    private const float JumpImpulse = 289f;
    private const float BunnyJumpMaxSpeedFactor = 1.2f;
    private const float LostFootingRestick = 50f;
    private const float LostFootingFriction = 0.1f;
    private const float AirCurrentFrictionMult = 0.75f;
    private const float AirCurrentAirControlMult = 0.25f;
    private const float MaxChargeSpeed = 750f;
    private const int MaxClipPlanes = 5;

    private const uint InAttack = 1 << 0;
    private const uint InJump = 1 << 1;
    private const uint InDuck = 1 << 2;
    private const uint InAttack2 = 1 << 11;

    private const int ClassScout = 1;
    private const int CondTaunting = 7;
    private const int CondStunned = 15;
    private const int CondShieldCharge = 17;
    private const int CondSpeedBoost = 32;
    private const int CondSodaPopperHype = 36;
    private const int CondHalloweenSpeedBoost = 72;
    private const int CondBlastJumping = 81;
    private const int CondRuneAgility = 97;
    private const int CondGhost = 77;
    private const int CondParachute = 80;
    private const int CondKart = 82;
    private const int CondSwimmingCurse = 86;
    private const int CondSwimmingNoEffects = 107;
    private const int CondRocketPack = 125;
    private const int CondLostFooting = 126;
    private const int CondAirCurrent = 127;
    private const int CondBurning = 22;
    private const int CondBombHead = 53;
    private const int CondThriller = 54;
    private const int CondKartDash = 83;
    private const int CondGrappledToPlayer = 120;

    /// <summary><c>GetConditionFromRuneType</c>'s conditions in <c>RuneTypes_t</c> order (<c>tf_shareddefs.h:2659</c>).</summary>
    private static readonly int[] RuneConditions = [90, 91, 92, 93, 94, 95, 96, 97, 103, 109, 110, 111];

    private const int ClassSoldier = 3;
    private const int ClassHeavy = 6;
    private const int ClassPyro = 7;

    private const int StunMovement = 1 << 0;
    private const int StunControls = 1 << 1;
    private const int StunForwardOnly = 1 << 2;
    private const int StunLoserState = 1 << 6;

    private const int WaterLevelFeet = 1;
    private const int WaterLevelWaist = 2;
    private const int WaterLevelEyes = 3;
    private const int ContentsSlime = 0x10;
    private const int ContentsWater = 0x20;

    /// <summary><c>MASK_WATER</c> (<c>bspflags.h:112</c>).</summary>
    private const int MaskWater = ContentsWater | 0x4000 | ContentsSlime;


    private const float WaterJumpHeight = 8f;
    private const float TfWaterJumpForward = 30f;
    private const float TfWaterJumpUp = 300f;

    // The replicated tf_* ConVars at their declared defaults (tf_gamemovement.cpp:53-70, :718-733).
    private const float ParachuteMaxSpeedXy = 300f;
    private const float ParachuteMaxSpeedZ = -100f;
    private const float ParachuteMaxSpeedOnFireZ = -100f;
    private const float ParachuteAirControl = 2.5f;
    private const float KartAirControl = 1.2f;
    private const float GhostUpSpeed = 300f;
    private const float GhostXySpeed = 300f;
    private const float GrapplingHookMoveSpeed = 750f;
    private const float GrapplingHookFollowDistance = 64f;
    private const float GrapplingHookJumpUpSpeed = 375f;
    private const float KartDashSpeed = 1000f;
    private const float KartNormalSpeed = 650f;
    private const float KartNormalAccel = 300f;
    private const float KartSlowMovingAccel = 500f;
    private const float KartSlowMovingThreshold = 300f;
    private const float KartReverseSpeed = -50f;
    private const float KartBrakeSpeed = 0f;
    private const float KartBrakeAccel = 500f;
    private const float KartIdleSpeed = 0f;
    private const float KartCoastAccel = 300f;
    private const float KartBombHeadScale = 1.5f;

    /// <summary><c>g_TFViewVectors</c> (<c>tf_gamerules.cpp:1313-1317</c>).</summary>
    private static readonly Vector3 HullMin = new(-24f, -24f, 0f);
    private static readonly Vector3 HullMax = new(24f, 24f, 82f);
    private static readonly Vector3 DuckHullMin = new(-24f, -24f, 0f);
    private static readonly Vector3 DuckHullMax = new(24f, 24f, 62f);

    private readonly PlayerTraceRay _trace;
    private readonly MovementConVars _convars;

    private PredictedPlayer _player;
    private float _frametime;
    private bool _noWorld;
    private bool _declined;

    // CMoveData.
    private float _forwardMove;
    private float _sideMove;
    private float _upMove;
    private uint _buttons;
    private Vector3 _viewAngles;
    private float _maxSpeed;
    private float _clientMaxSpeed;
    private bool _speedCropped;

    private readonly int _maxClients;

    /// <summary><c>m_flStuckCheckTime</c>'s gate (<c>gamemovement.cpp:3455</c>): one frame runs one random-offset try.</summary>
    private bool _stuckCheckedThisFrame;

    private int _commandNumber;

    /// <summary>A movement simulation over a world.</summary>
    /// <param name="trace">The world, with whatever boxes block the player.</param>
    /// <param name="convars">The server's movement ConVars.</param>
    /// <param name="maxClients">
    /// <c>gpGlobals->maxClients</c>: players are entities 1 to it, and it picks <c>CheckStuck</c>'s interval.
    /// </param>
    public TfGameMovement(PlayerTraceRay trace, MovementConVars convars, int maxClients)
    {
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _convars = convars ?? throw new ArgumentNullException(nameof(convars));
        _maxClients = maxClients;
    }

    /// <summary>
    /// <c>enginetrace-&gt;GetPointContents</c>, which <c>CheckWater</c> asks at the feet, waist and eyes; null keeps the
    /// networked water level and type.
    /// </summary>
    public Func<Vector3, int>? PointContents { get; init; }

    /// <summary>The player's items, as the movement hooks their attributes; none by default. Set again when a command switches weapon.</summary>
    public MovementItems Items { get; set; } = MovementItems.None;

    /// <summary>
    /// <c>GetSurfaceData( pm.surface.surfaceProps )</c> for a ground trace (<c>CategorizeGroundSurface</c>); null, or a null
    /// answer, for no surface data — friction, jump and speed factors of 1.
    /// </summary>
    public Func<BspTrace, VphysicsSurface?>? GroundSurface { get; init; }

    /// <summary><c>CPrediction::RunCommand</c>'s movement half: <c>SetupMove</c>, <c>ProcessMovement</c>, <c>FinishMove</c>.</summary>
    /// <param name="player">The player, advanced in place.</param>
    /// <param name="command">The usercmd.</param>
    /// <param name="frametime">The tick interval.</param>
    /// <param name="first">
    /// Whether this is the first command after the networked state was restored — <c>m_bGameCodeMovedPlayer</c>, which
    /// is true when the network origin differs from the last predicted one, and asks for a full <c>CategorizePosition</c>.
    /// </param>
    /// <param name="commandNumber"><c>CurrentCommandNumber()</c>, which <c>CheckInterval</c> staggers <c>CheckStuck</c> by.</param>
    /// <returns>False when the move is one this port declines, or there is no world; the player is then untouched.</returns>
    public bool ProcessMovement(ref PredictedPlayer player, UserCommand command, float frametime, bool first, int commandNumber)
    {
        ArgumentNullException.ThrowIfNull(command);

        _commandNumber = commandNumber;

        if (player.WaterJumpUnknown)
        {
            return false;
        }

        _player = player;
        _declined = false;
        _frametime = frametime;

        // SetupMove (prediction.cpp:610).
        _forwardMove = command.ForwardMove;
        _sideMove = command.SideMove;
        _upMove = command.UpMove;
        _buttons = command.Buttons;
        _viewAngles = new Vector3(command.Pitch, command.Yaw, command.Roll);
        _clientMaxSpeed = _player.MaxSpeed;

        // CTFGameMovement::ProcessMovement (tf_gamemovement.cpp:289).
        _speedCropped = false;
        _maxSpeed = TfMaxSpeed;

        ChargeMove();
        StunMove();
        TauntMove();
        GrapplingHookMove();

        if (_declined)
        {
            return false;
        }

        HighMaxSpeedMove();
        PlayerMove(first);

        // FinishMove (gamemovement.cpp:1197) and CPrediction::FinishMove's m_nOldButtons. Every write the move makes to
        // m_nOldButtons before this — Duck's IN_DUCK, the IN_JUMP set by CheckJumpButton and CheckWaterJump and cleared by
        // FullWalkMove — is overwritten here with nothing reading it between, so none of them is ported (D180).
        _player.OldButtons = _buttons;
        _player.OldForwardMove = _forwardMove;
        _player.CurTime += frametime;

        if (_noWorld)
        {
            // Cleared for the next command, which starts from a world that may have arrived since.
            _noWorld = false;
            return false;
        }

        if (_declined)
        {
            // A step inside the move this port cannot follow (TeamFortressSetSpeed).
            return false;
        }

        player = _player;
        return true;
    }

    private void ChargeMove()
    {
        if (!_player.Conditions.Has(CondShieldCharge))
        {
            // The Quick-Fix medic copying a charging patient is not ported: it needs the heal target.
            return;
        }

        _maxSpeed = MaxChargeSpeed;

        uint oldButtons = _buttons;

        _forwardMove = MaxChargeSpeed;
        _sideMove = 0f;
        _upMove = 0f;
        _buttons = (_buttons & InAttack2) != 0 ? InAttack2 : 0;

        if ((oldButtons & InAttack) != 0)
        {
            _buttons |= InAttack;
        }
    }

    private void HighMaxSpeedMove()
    {
        float maxSpeed = _player.MaxSpeed;

        if (MathF.Abs(_forwardMove) < maxSpeed)
        {
            if (AlmostEqual(_forwardMove, _convars.ForwardSpeed))
            {
                _forwardMove = maxSpeed;
            }
            else if (AlmostEqual(_forwardMove, -_convars.BackSpeed))
            {
                _forwardMove = -maxSpeed;
            }
        }

        if (MathF.Abs(_sideMove) < maxSpeed)
        {
            if (AlmostEqual(_sideMove, _convars.SideSpeed))
            {
                _sideMove = maxSpeed;
            }
            else if (AlmostEqual(_sideMove, -_convars.SideSpeed))
            {
                _sideMove = -maxSpeed;
            }
        }
    }

    /// <summary><c>AlmostEqual</c>: within a few ULPs. *Interpolated:* the tolerance is taken as 2; a recorded move is exactly ±450.</summary>
    private static bool AlmostEqual(float a, float b)
    {
        int ia = BitConverter.SingleToInt32Bits(a);
        int ib = BitConverter.SingleToInt32Bits(b);

        if ((ia < 0) != (ib < 0))
        {
            // Opposite signs are equal only as +0 and −0.
            return a - b == 0f;
        }

        return Math.Abs(ia - ib) <= 2;
    }

    private void PlayerMove(bool first)
    {
        // CTFGameMovement::PlayerMove: lost footing may exceed the run speed.
        if (_player.Conditions.Has(CondLostFooting))
        {
            _clientMaxSpeed = _maxSpeed;
        }

        CheckParameters();
        ReduceTimers();

        // gamemovement.cpp:4587-4601: MOVETYPE_WALK and alive, so always tried on its interval; stuck skips the move.
        if (!_player.IsDead && CheckInterval() && CheckStuck())
        {
            return;
        }

        if (first)
        {
            CategorizePosition();
        }
        else if (_player.Velocity.Z > 250f)
        {
            SetGroundEntity(null);
        }

        if (!_player.OnGround)
        {
            _player.FallVelocity = -_player.Velocity.Z;
        }

        Duck();
        FullWalkMove();
    }

    private void CheckParameters()
    {
        float spd = (_forwardMove * _forwardMove) + (_sideMove * _sideMove) + (_upMove * _upMove);

        if (_clientMaxSpeed != 0f)
        {
            _maxSpeed = MathF.Min(_clientMaxSpeed, _maxSpeed);
        }

        // gamemovement.cpp:1002-1014: the ground's speed factor; a TF player has no constraint, whose factor is 1.
        _maxSpeed *= _player.Surface?.MaxSpeedFactor ?? 1f;

        if (spd != 0f && spd > _maxSpeed * _maxSpeed)
        {
            float ratio = _maxSpeed / MathF.Sqrt(spd);
            _forwardMove *= ratio;
            _sideMove *= ratio;
            _upMove *= ratio;
        }

        if (_player.IsDead)
        {
            _forwardMove = 0f;
            _sideMove = 0f;
            _upMove = 0f;
        }
    }

    private void ReduceTimers()
    {
        float frameMsec = 1000f * _frametime;

        _player.DuckTime = Reduce(_player.DuckTime, frameMsec);
        _player.DuckJumpTime = Reduce(_player.DuckJumpTime, frameMsec);
        _player.JumpTime = Reduce(_player.JumpTime, frameMsec);
    }

    private static float Reduce(float timer, float by) => timer > 0f ? MathF.Max(0f, timer - by) : timer;

    private Vector3 PlayerMins => _player.Ducked ? DuckHullMin : HullMin;

    private Vector3 PlayerMaxs => _player.Ducked ? DuckHullMax : HullMax;

    /// <summary><c>TracePlayerBBox</c> with <c>PlayerSolidMask()</c>; a missing world ends the prediction.</summary>
    private BspTrace TracePlayerBBox(Vector3 start, Vector3 end) => TraceBox(start, end, PlayerMins, PlayerMaxs);

    private BspTrace TraceBox(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs)
    {
        if (_trace(start, end, mins, maxs, PlayerSolidMask()) is { } trace)
        {
            return trace;
        }

        _noWorld = true;
        return new BspTrace(1f, -1, default, false);
    }

    /// <summary>
    /// <c>CTFGameMovement::PlayerSolidMask</c> (<c>tf_gamemovement.cpp:259-284</c>): <c>MASK_PLAYERSOLID</c> and the enemy
    /// team's contents, unless passing through enemies; a ghost collides with the world alone (:264,
    /// <c>MASK_PLAYERSOLID_BRUSHONLY</c>).
    /// </summary>
    private int PlayerSolidMask()
    {
        if (_player.Conditions.Has(CondGhost) || _player.PassingThroughEnemies)
        {
            return BspLeafTree.MaskPlayerSolid;
        }

        return _player.Team switch
        {
            TeamRed => MovementWorld.ContentsBlueTeam | BspLeafTree.MaskPlayerSolid,
            TeamBlue => MovementWorld.ContentsRedTeam | BspLeafTree.MaskPlayerSolid,
            _ => BspLeafTree.MaskPlayerSolid,
        };
    }

    private const int TeamRed = 2;
    private const int TeamBlue = 3;

    /// <summary>
    /// <c>CheckInterval( STUCK )</c> (<c>gamemovement.cpp:648-701</c>): every command while being unstuck, otherwise when
    /// the command number plus the entity index is a multiple of <c>CHECK_STUCK_INTERVAL</c> 1 s in ticks — 0.2 s alone.
    /// </summary>
    private bool CheckInterval()
    {
        float seconds = _maxClients == 1 ? 0.2f : 1f;
        int interval = _player.StuckLast != 0 ? 1 : (int)(seconds / _frametime);

        return interval <= 0 || (_commandNumber + _player.EntityIndex) % interval == 0;
    }

    /// <summary>
    /// <c>CTFGameMovement::CheckStuck</c> (<c>tf_gamemovement.cpp:1352-1447</c>), <c>tf_resolve_stuck_players</c> 1 (<c>:50</c>).
    /// </summary>
    /// <returns>True when he is stuck and the move is skipped.</returns>
    /// <remarks>
    /// The <c>func_tracktrain</c> branch (<c>:1417</c>) never runs on the client: it needs the train's
    /// <c>GetAbsVelocity().z</c>, and <c>DT_FuncTrackTrain</c> sends no velocity (<c>c_func_tracktrain.cpp:41-42</c>).
    /// </remarks>
    private bool CheckStuck()
    {
        _player.PassingThroughEnemies = false;

        BspTrace trace = TracePlayerBBox(_player.Origin, _player.Origin);

        if (trace.StartSolid && trace.BrushEntity >= 0 && IsPlayer(trace.BrushEntity))
        {
            _player.PassingThroughEnemies = true;

            if (!DidHit(TracePlayerBBox(_player.Origin, _player.Origin)))
            {
                return false;
            }
        }

        return BaseCheckStuck();
    }

    /// <summary><c>CBaseEntity::IsPlayer</c> by index: players are entities 1 to <c>maxClients</c>.</summary>
    private bool IsPlayer(int entity) => entity >= 1 && entity <= _maxClients;

    /// <summary><c>trace_t::DidHit</c>: <c>fraction &lt; 1 || allsolid || startsolid</c>.</summary>
    private static bool DidHit(BspTrace trace) => trace.Fraction < 1f || trace.AllSolid || trace.StartSolid;

    /// <summary>
    /// <c>CGameMovement::CheckStuck</c> (<c>gamemovement.cpp:3384-3473</c>) on the client.
    /// </summary>
    /// <remarks>
    /// <c>TestPlayerPosition</c> answers an entity when the box at the point starts solid. The world — a static prop too,
    /// whose trace names the world entity (*interpolated*: engine trace code the SDK omits) — gets the 54 small nudges;
    /// anything else, or a world that none frees, one table entry per frame behind <c>CHECKSTUCK_MINTIME</c>. The client's
    /// <c>m_flStuckCheckTime</c> is a member nothing networks; prediction is one frame here, so the first check passes the
    /// gate and any later one this frame does not.
    /// </remarks>
    private bool BaseCheckStuck()
    {
        BspTrace hit = TracePlayerBBox(_player.Origin, _player.Origin);

        if (!hit.StartSolid)
        {
            _player.StuckLast = 0;
            return false;
        }

        Vector3 origin = _player.Origin;

        if (hit.BrushEntity < 0)
        {
            _player.StuckLast = 0;

            for (int reps = 0; reps < StuckTable.Length; reps++)
            {
                if (TryStuckOffset(origin))
                {
                    return false;
                }
            }
        }

        if (_stuckCheckedThisFrame)
        {
            return true;
        }

        _stuckCheckedThisFrame = true;

        return !TryStuckOffset(origin);
    }

    /// <summary><c>GetRandomStuckOffsets</c> then <c>TestPlayerPosition</c>: moves him there and resets when it is clear.</summary>
    private bool TryStuckOffset(Vector3 origin)
    {
        Vector3 test = origin + StuckTable[_player.StuckLast++ % StuckTable.Length];

        if (TracePlayerBBox(test, test).StartSolid)
        {
            return false;
        }

        _player.StuckLast = 0;
        _player.Origin = test;
        return true;
    }

    /// <summary><c>rgv3tStuckTable</c> as <c>CreateStuckTable</c> fills it (<c>gamemovement.cpp:3233-3346</c>); the last entry stays zero.</summary>
    private static readonly Vector3[] StuckTable = CreateStuckTable();

    private static Vector3[] CreateStuckTable()
    {
        Vector3[] table = new Vector3[54];
        int index = 0;

        // Little moves along z, y, x, then the eight corners an eighth out.
        for (float z = -0.125f; z <= 0.125f; z += 0.125f)
        {
            table[index++] = new Vector3(0f, 0f, z);
        }

        for (float y = -0.125f; y <= 0.125f; y += 0.125f)
        {
            table[index++] = new Vector3(0f, y, 0f);
        }

        for (float x = -0.125f; x <= 0.125f; x += 0.125f)
        {
            table[index++] = new Vector3(x, 0f, 0f);
        }

        for (float x = -0.125f; x <= 0.125f; x += 0.250f)
        {
            for (float y = -0.125f; y <= 0.125f; y += 0.250f)
            {
                for (float z = -0.125f; z <= 0.125f; z += 0.250f)
                {
                    table[index++] = new Vector3(x, y, z);
                }
            }
        }

        // Big moves: z by 0, 1 and 6, then y and x by two, then every combination.
        float[] zi = [0f, 1f, 6f];

        foreach (float z in zi)
        {
            table[index++] = new Vector3(0f, 0f, z);
        }

        for (float y = -2f; y <= 2f; y += 2f)
        {
            table[index++] = new Vector3(0f, y, 0f);
        }

        for (float x = -2f; x <= 2f; x += 2f)
        {
            table[index++] = new Vector3(x, 0f, 0f);
        }

        foreach (float z in zi)
        {
            for (float x = -2f; x <= 2f; x += 2f)
            {
                for (float y = -2f; y <= 2f; y += 2f)
                {
                    table[index++] = new Vector3(x, y, z);
                }
            }
        }

        return table;
    }

    private static Vector3 EndPos(Vector3 start, Vector3 end, BspTrace trace) => start + ((end - start) * trace.Fraction);

    private static bool Hit(BspTrace trace) => trace.Fraction < 1f || trace.StartSolid;

    private static Vector3 Normal(BspTrace trace) => new(trace.Normal.X, trace.Normal.Y, trace.Normal.Z);

    private void CategorizePosition()
    {
        _player.SurfaceFriction = 1f;

        CheckWater();

        if (_player.Velocity.Z > 250f)
        {
            SetGroundEntity(null);
            return;
        }

        Vector3 start = _player.Origin;
        Vector3 end = start with { Z = start.Z - 2f };
        bool moveToEndPos = false;

        if (_player.OnGround && _player.WaterLevel < WaterLevelEyes)
        {
            end.Z -= _convars.StepSize;
            moveToEndPos = true;
        }

        BspTrace trace = TracePlayerBBox(start, end);

        bool inAir = false;
        float groundFrictionMult = 1f;
        float airFrictionMult = _player.Conditions.Has(CondAirCurrent) ? AirCurrentFrictionMult : 1f;

        if (_player.Conditions.Has(CondLostFooting))
        {
            float away = Vector3.Dot(_player.Velocity, Normal(trace));

            if (away > 0f)
            {
                inAir = true;
            }
            else if ((_player.Velocity - (Normal(trace) * away)).Length() >= LostFootingRestick)
            {
                groundFrictionMult *= LostFootingFriction;
            }
            else
            {
                // RemoveCond( TF_COND_LOST_FOOTING ): bit 30 of m_nPlayerCondEx3 (conditions 96 to 127).
                _player.Conditions = _player.Conditions with { Ex3 = _player.Conditions.Ex3 & ~(1 << (CondLostFooting - 96)) };
            }
        }

        if (!inAir && trace.Normal.Z < 0.7f)
        {
            trace = TracePlayerBBoxForGround(start, end, trace);

            if (trace.Normal.Z < 0.7f)
            {
                inAir = true;

                if (_player.Velocity.Z > 0f)
                {
                    _player.SurfaceFriction = 0.25f;
                }
            }
        }
        else if (!inAir && moveToEndPos && !trace.StartSolid && trace.Fraction > 0f && trace.Fraction < 1f)
        {
            Vector3 endPos = EndPos(start, end, trace);

            if (MathF.Abs(_player.Origin.Z - endPos.Z) > 0.5f * CoordResolution)
            {
                _player.Origin = _player.Origin with { Z = endPos.Z };
            }
        }

        SetGroundEntity(inAir || !Hit(trace) ? null : trace);
        _player.SurfaceFriction *= inAir ? airFrictionMult : groundFrictionMult;
    }

    /// <summary><c>TracePlayerBBoxForGround</c> (<c>gamemovement.cpp:3660</c>): the four quadrant boxes.</summary>
    private BspTrace TracePlayerBBoxForGround(Vector3 start, Vector3 end, BspTrace original)
    {
        Vector3 mins = PlayerMins;
        Vector3 maxs = PlayerMaxs;

        (Vector3 Min, Vector3 Max)[] quadrants =
        [
            (mins, new Vector3(MathF.Min(0f, maxs.X), MathF.Min(0f, maxs.Y), maxs.Z)),
            (new Vector3(MathF.Max(0f, mins.X), MathF.Max(0f, mins.Y), mins.Z), maxs),
            (new Vector3(mins.X, MathF.Max(0f, mins.Y), mins.Z), new Vector3(MathF.Min(0f, maxs.X), maxs.Y, maxs.Z)),
            (new Vector3(MathF.Max(0f, mins.X), mins.Y, mins.Z), new Vector3(maxs.X, MathF.Min(0f, maxs.Y), maxs.Z)),
        ];

        BspTrace last = original;

        foreach ((Vector3 min, Vector3 max) in quadrants)
        {
            last = TraceBox(start, end, min, max);

            if (Hit(last) && last.Normal.Z >= 0.7f)
            {
                // The plane of the quadrant that found ground, with the original fraction and endpos.
                return last with { Fraction = original.Fraction, StartSolid = original.StartSolid };
            }
        }

        return last with { Fraction = original.Fraction, StartSolid = original.StartSolid };
    }

    private void SetGroundEntity(BspTrace? trace)
    {
        // CGameMovement::SetGroundEntity (gamemovement.cpp:3611-3632): landing subtracts the new ground's GetAbsVelocity and
        // takes its z, leaving adds the old one's. On the client that velocity is zero for anything but a player: no
        // DT_BaseEntity, DT_BaseDoor or DT_FuncTrackTrain field carries m_vecVelocity (c_baseentity.cpp:438-485,
        // c_basedoor.cpp:17-19), and nothing predicts them. *Interpolated:* a player as ground is taken as zero too.
        bool newGround = trace is not null;

        if (newGround != _player.OnGround)
        {
            _player.BaseVelocity = _player.BaseVelocity with { Z = 0f };
        }

        _player.OnGround = newGround;

        if (trace is null)
        {
            return;
        }

        CategorizeGroundSurface(trace.Value);
        _player.Velocity = _player.Velocity with { Z = 0f };

        // CTFGameMovement::SetGroundEntity.
        _player.AirDash = 0;
        _player.AirDucked = 0;
    }

    /// <summary><c>CategorizeGroundSurface</c> (<c>gamemovement.cpp:919-930</c>): the surface's data, and its friction · 1.25 up to 1.</summary>
    private void CategorizeGroundSurface(BspTrace trace)
    {
        _player.Surface = GroundSurface?.Invoke(trace);
        _player.SurfaceFriction = MathF.Min((_player.Surface?.Physics.Friction ?? 0.8f) * 1.25f, 1f);
    }

    private void Duck()
    {
        DuckOverrides();

        uint changed = _player.OldButtons ^ _buttons;
        uint pressed = changed & _buttons;
        uint released = changed & _player.OldButtons;

        if (_player.IsDead)
        {
            return;
        }

        HandleDuckingSpeedCrop();

        if ((_buttons & InDuck) != 0 || _player.Ducking || _player.FlDucking)
        {
            if ((_buttons & InDuck) != 0 && CanDuck())
            {
                OnDuck(pressed);
            }
            else
            {
                OnUnDuck(released);
            }
        }
    }

    /// <summary><c>CTFPlayer::CanDuck</c> (<c>tf_player_shared.cpp:12298</c>): <c>CALL_ATTRIB_HOOK_INT( iNoDuck, no_duck )</c> is 0.</summary>
    private bool CanDuck() => HookInt(Items.OnPlayer("no_duck", 0f)) == 0;

    /// <summary><c>CALL_ATTRIB_HOOK_INT</c>'s rounding of the float result.</summary>
    private static int HookInt(float value) => AttributeHooks.RoundFloatToInt(value);

    private void DuckOverrides()
    {
        bool onGround = _player.OnGround;

        // No ducking in water (tf_gamemovement.cpp:3184): WL_Feet (1) off the ground, or WL_Eyes (3).
        if ((_player.WaterLevel >= 1 && !onGround) || _player.WaterLevel >= 3)
        {
            _buttons &= ~InDuck;
        }

        // tf_clamp_airducks (:49, :3190-3191).
        if (!_convars.ClampAirDucks)
        {
            return;
        }

        if (_player.CurTime < _player.DuckTimer && onGround)
        {
            _buttons &= ~InDuck;
        }

        if (_player.Ducked && _player.Ducking)
        {
            _buttons &= ~InDuck;
        }

        if (!onGround && _player.AirDucked >= TfAirDuckedCount)
        {
            _buttons &= ~InDuck;
        }
    }

    private void HandleDuckingSpeedCrop()
    {
        if (!_speedCropped && _player.FlDucking && _player.OnGround)
        {
            const float Fraction = 0.33333333f;
            _forwardMove *= Fraction;
            _sideMove *= Fraction;
            _upMove *= Fraction;
            _speedCropped = true;
        }

        // CTFGameMovement::HandleDuckingSpeedCrop (tf_gamemovement.cpp:3375-3383): a loser ducking cannot move.
        if (_speedCropped && _player.IsLoser)
        {
            _forwardMove = 0f;
            _sideMove = 0f;
            _upMove = 0f;
        }
    }

    private void OnDuck(uint pressed)
    {
        bool inAir = !_player.OnGround;
        bool inDuck = _player.FlDucking;

        if ((pressed & InDuck) != 0 && !inDuck)
        {
            _player.DuckTime = GameMovementDuckTime;
            _player.Ducking = true;
        }

        if (_player.Ducking)
        {
            float duckSeconds = MathF.Max(0f, GameMovementDuckTime - _player.DuckTime) * 0.001f;

            if (duckSeconds > TimeToDuck || inDuck || inAir)
            {
                FinishDuck();
            }
        }
    }

    private void OnUnDuck(uint released)
    {
        bool inAir = !_player.OnGround;
        bool inDuck = _player.FlDucking;

        if ((released & InDuck) != 0)
        {
            _player.DuckTimer = _player.CurTime + TfTimeToDuck;

            if (inAir)
            {
                _player.AirDucked++;
            }
        }

        // Try to unduck unless automovement is not allowed; off the ground or mid-transition he always may (:3306).
        if (!_player.AllowAutoMovement && !inAir && !_player.Ducking)
        {
            return;
        }

        if ((released & InDuck) != 0)
        {
            if (inDuck)
            {
                _player.DuckTime = GameMovementDuckTime;
            }
            else if (_player.Ducking && !_player.Ducked)
            {
                const float UnduckMilliseconds = 1000f * TimeToUnduck;
                const float DuckMilliseconds = 1000f * TimeToDuck;
                float elapsed = GameMovementDuckTime - _player.DuckTime;
                float remaining = elapsed / DuckMilliseconds * UnduckMilliseconds;

                _player.DuckTime = GameMovementDuckTime - UnduckMilliseconds + remaining;
            }
        }

        if (CanUnduck())
        {
            if (_player.Ducking || _player.Ducked)
            {
                float duckSeconds = MathF.Max(0f, GameMovementDuckTime - _player.DuckTime) * 0.001f;

                if (duckSeconds > TimeToUnduck || inAir)
                {
                    FinishUnDuck();
                }
                else
                {
                    _player.Ducking = true;
                }
            }
        }
        else if (_player.DuckTime - GameMovementDuckTime != 0f)
        {
            _player.DuckTime = GameMovementDuckTime;
            _player.Ducked = true;
            _player.Ducking = false;
            _player.FlDucking = true;
        }
    }

    /// <summary>How far the origin moves when the hull changes in the air: the standing hull's height less the ducked one's.</summary>
    private static Vector3 ViewDelta => (HullMax - HullMin) - (DuckHullMax - DuckHullMin);

    private bool CanUnduck()
    {
        Vector3 newOrigin = _player.OnGround
            ? _player.Origin + (DuckHullMin - HullMin)
            : _player.Origin - ViewDelta;

        BspTrace trace = TraceBox(_player.Origin, newOrigin, HullMin, HullMax);

        return !trace.StartSolid && trace.Fraction >= 1f;
    }

    private void FinishUnDuck()
    {
        Vector3 newOrigin = _player.OnGround
            ? _player.Origin + (DuckHullMin - HullMin)
            : _player.Origin - ViewDelta;

        _player.Ducked = false;
        _player.FlDucking = false;
        _player.Ducking = false;
        _player.InDuckJump = false;
        _player.DuckTime = 0f;
        _player.Origin = newOrigin;

        CategorizePosition();
    }

    private void FinishDuck()
    {
        if (_player.FlDucking)
        {
            return;
        }

        _player.FlDucking = true;
        _player.Ducked = true;
        _player.Ducking = false;

        _player.Origin = _player.OnGround
            ? _player.Origin - (DuckHullMin - HullMin)
            : _player.Origin + ViewDelta;

        FixPlayerCrouchStuck(upward: true);
        CategorizePosition();
    }

    private void FixPlayerCrouchStuck(bool upward)
    {
        if (!TracePlayerBBox(_player.Origin, _player.Origin).StartSolid)
        {
            return;
        }

        Vector3 test = _player.Origin;
        float direction = upward ? 1f : 0f;

        for (int i = 0; i < 36; i++)
        {
            _player.Origin = _player.Origin with { Z = _player.Origin.Z + direction };

            if (!TracePlayerBBox(_player.Origin, _player.Origin).StartSolid)
            {
                return;
            }
        }

        _player.Origin = test;
    }

    private void FullWalkMove()
    {
        if (!InWater())
        {
            ParachuteClamp();
            StartGravity();
        }

        if (_player.WaterJumpTime != 0f)
        {
            WaterJump();
            TryPlayerMove(null, null, 0f);
            CheckWater();
            return;
        }

        if (InWater() || _player.Conditions.Has(CondGhost) || _player.Conditions.Has(CondSwimmingNoEffects))
        {
            FullWalkMoveUnderwater();
            return;
        }

        if ((_buttons & InJump) != 0)
        {
            CheckJumpButton();
        }

        CheckVelocity();

        if (_player.OnGround)
        {
            _player.Velocity = _player.Velocity with { Z = 0f };
            Friction();
            WalkMove();
        }
        else
        {
            AirMove();
        }

        CategorizePosition();

        if (!InWater())
        {
            FinishGravity();
        }

        if (_player.OnGround)
        {
            _player.Velocity = _player.Velocity with { Z = 0f };
        }

        // CheckFalling clears m_flFallVelocity on landing and changes no velocity.
        if (_player.OnGround)
        {
            _player.FallVelocity = 0f;
        }

        CheckVelocity();
    }

    private void CheckJumpButton()
    {
        if (_player.IsDead || !CheckWaterJumpButton())
        {
            return;
        }

        // tf_gamemovement.cpp:1170: a grappling player's jump climbs the rope.
        if (_player.GrapplingHook is not null && _player.PlayerClass != ClassHeavy)
        {
            int rune = CarryingRune();
            float z = _player.Velocity.Z + GrapplingHookJumpUpSpeed;

            if (rune != CondRuneAgility && rune >= 0 && _player.HasTheFlag)
            {
                z *= 0.8f;
            }

            _player.Velocity = _player.Velocity with { Z = MathF.Min(z, GetAirSpeedCap()) };
            FinishGravity();
            return;
        }

        // :1195: holding jump makes a ghost fly.
        if (_player.Conditions.Has(CondGhost))
        {
            _player.Velocity = _player.Velocity with { Z = GhostUpSpeed };
            FinishGravity();
            return;
        }

        bool scout = _player.PlayerClass == ClassScout;
        bool onGround = _player.OnGround;

        // tf_gamemovement.cpp:1210: CanJump comes before every ducking test.
        if (_player.IsDead || !CanJump() ||
            (_player.FlDucking && !(scout && !onGround)) ||
            (_player.Ducking && _player.FlDucking) || _player.DuckJumpTime > 0f ||
            (_player.OldButtons & InJump) != 0)
        {
            return;
        }

        if (!onGround)
        {
            if (CanAirDash())
            {
                AirDash();
                _player.AirDucked = 0;
            }

            return;
        }

        PreventBunnyJumping();
        SetGroundEntity(null);

        // :1277-1315: m_pSurfaceData survives SetGroundEntity( NULL ), so this is the ground just left.
        float mul = JumpImpulse * JumpMod() * (_player.Surface?.JumpFactor ?? 1f);

        if (_player.Ducking || _player.FlDucking)
        {
            _player.Velocity = _player.Velocity with { Z = mul };
        }
        else
        {
            _player.Velocity = _player.Velocity with { Z = _player.Velocity.Z + mul };
        }

        FinishGravity();
    }

    private void AirDash()
    {
        float jumpMod = JumpMod();

        // Lose hype on airdash (tf_gamemovement.cpp:1007-1016).
        int hypeResetsOnJump = HookInt(Items.OnPlayer("hype_resets_on_jump", 0f));

        if (hypeResetsOnJump != 0)
        {
            float before = _player.HypeMeter;
            SetScoutHypeMeter(_player.HypeMeter - hypeResetsOnJump);
            TeamFortressSetSpeed(before);
        }

        (Vector3 forward, Vector3 right) = FlatAxes();

        Vector3 wish = new(
            (forward.X * _forwardMove) + (right.X * _sideMove),
            (forward.Y * _forwardMove) + (right.Y * _sideMove),
            0f);

        _player.Velocity = wish with { Z = AirDashZ * jumpMod };
        _player.AirDash++;
    }

    /// <summary><c>SetScoutHypeMeter</c> (<c>tf_player_shared.cpp:14124-14129</c>): not while hype-buffed, clamped to 0..100.</summary>
    private void SetScoutHypeMeter(float value)
    {
        if (!_player.Conditions.Has(CondSodaPopperHype))
        {
            _player.HypeMeter = Math.Clamp(value, 0f, 100f);
        }
    }

    /// <summary><c>TeamFortress_SetSpeed</c> (<c>tf_player_shared.cpp:11130-11147</c>) after the hype meter moved.</summary>
    /// <param name="hypeBefore">The hype the networked max speed was computed with.</param>
    /// <remarks>
    /// The max speed's only hype term is the Baby Face's <c>RemapValClamped( hype, 0, 100, 1, 1.45 )</c> (<c>:11080-11086</c>),
    /// a factor near the end of <c>TeamFortress_CalculateMaxSpeed</c> with only factors after it, so the networked
    /// <c>m_flMaxspeed</c> divided by the old term and multiplied by the new is the recompute. Not under
    /// <c>TF_COND_SPEED_BOOST</c>, which adds to the speed under <c>GAME_DLL</c> only (<c>:10918-10928</c>): the client's
    /// recompute drops it, which no division undoes, so that move is declined.
    /// </remarks>
    private void TeamFortressSetSpeed(float hypeBefore)
    {
        if (_player.Conditions.Has(CondSpeedBoost))
        {
            _declined = true;
            return;
        }

        if (_player.OwnsPepBrawlerBlaster)
        {
            _player.MaxSpeed = _player.MaxSpeed / BabyFaceSpeed(hypeBefore) * BabyFaceSpeed(_player.HypeMeter);
        }
    }

    private static float BabyFaceSpeed(float hype) => 1f + (0.45f * Math.Clamp(hype / 100f, 0f, 1f));

    /// <summary>
    /// <c>flJumpMod</c> (<c>tf_gamemovement.cpp:997-1020</c>, <c>:1287-1313</c>): <c>mod_jump_height</c> on the player, then
    /// <c>mod_jump_height_from_weapon</c> on the active weapon, then 1.8 for the agility rune.
    /// </summary>
    private float JumpMod()
    {
        float mod = Items.OnWeapon("mod_jump_height_from_weapon", Items.OnPlayer("mod_jump_height", 1f));

        // GetCarryingRuneType() == RUNE_AGILITY: the rune's condition (tf_shareddefs.h:2671).
        return _player.Conditions.Has(CondRuneAgility) ? mod * 1.8f : mod;
    }

    /// <summary>
    /// <c>CTFPlayer::GetMovementForwardPull</c> (<c>tf_player_shared.cpp:10767-10779</c>): <c>firing_forward_pull</c> on the active
    /// weapon while it <c>IsFiring()</c>, else 0.
    /// </summary>
    private float GetMovementForwardPull() =>
        Items.OnActiveWeapon is not null && _player.ActiveWeaponFiring ? Items.OnWeapon("firing_forward_pull", 0f) : 0f;

    private static float Length2D(Vector3 value) => MathF.Sqrt((value.X * value.X) + (value.Y * value.Y));

    /// <summary><c>CTFPlayer::CanJump</c> (<c>tf_player_shared.cpp:12276</c>): not while taunting, then the weapon and <c>no_jump</c>.</summary>
    private bool CanJump() =>
        !_player.Conditions.Has(CondTaunting) && Items.OwnerCanJump && HookInt(Items.OnPlayer("no_jump", 0f)) == 0;

    /// <summary><c>CTFPlayer::CanAirDash</c> (<c>tf_player_shared.cpp:12840</c>).</summary>
    private bool CanAirDash()
    {
        PlayerConditions conditions = _player.Conditions;

        if (conditions.Has(CondKart))
        {
            return false;
        }

        if (conditions.Has(CondHalloweenSpeedBoost))
        {
            return true;
        }

        if (_player.PlayerClass != ClassScout)
        {
            return false;
        }

        if (conditions.Has(CondSodaPopperHype))
        {
            return _player.AirDash < 5;
        }

        // tf_scout_air_dash_count, FCVAR_DEVELOPMENTONLY with a default of 1 (tf_player_shared.cpp:113).
        int dashCount = HookInt(Items.OnWeapon("air_dash_count", 1f));

        if (_player.AirDash >= dashCount)
        {
            return false;
        }

        // The Atomizer's third jump (:12867-12873): not within 0.7 s of the active weapon's GetLastDeployTime().
        if (Items.OnActiveWeapon is not null && dashCount >= 2 && _player.AirDash == 1 &&
            _player.LastDeployTime is { } deployed && _player.CurTime - deployed < 0.7f)
        {
            return false;
        }

        return HookInt(Items.OnPlayer("set_scout_doublejump_disabled", 0f)) != 1;
    }

    private void PreventBunnyJumping()
    {
        float maxScaledSpeed = BunnyJumpMaxSpeedFactor * _player.MaxSpeed;

        if (_player.Conditions.Has(CondKart) || maxScaledSpeed <= 0f)
        {
            return;
        }

        float speed = _player.Velocity.Length();

        if (speed > maxScaledSpeed)
        {
            _player.Velocity *= maxScaledSpeed / speed;
        }
    }

    private void Friction()
    {
        float speed = _player.Velocity.Length();

        if (speed < 0.1f)
        {
            return;
        }

        float drop = 0f;

        if (_player.OnGround)
        {
            float friction = _convars.Friction * _player.SurfaceFriction;
            float control = speed < _convars.StopSpeed ? _convars.StopSpeed : speed;

            drop += control * friction * _frametime;
        }

        float newSpeed = MathF.Max(0f, speed - drop);

        if (newSpeed - speed != 0f)
        {
            _player.Velocity *= newSpeed / speed;
        }
    }

    /// <summary>The view's forward and right, flattened and normalised, as WalkMove and AirMove use them.</summary>
    private (Vector3 Forward, Vector3 Right) FlatAxes()
    {
        (Vector3 forward, Vector3 right) = AngleVectors(_viewAngles);

        forward.Z = 0f;
        right.Z = 0f;

        return (Normalize(forward), Normalize(right));
    }

    private static Vector3 Normalize(Vector3 vector)
    {
        float length = vector.Length();

        return length > 0f ? vector / length : vector;
    }

    /// <summary><c>AngleVectors</c> (<c>mathlib_base.cpp</c>): forward and right from pitch, yaw, roll in degrees.</summary>
    private static (Vector3 Forward, Vector3 Right) AngleVectors(Vector3 angles)
    {
        (float sp, float cp) = MathF.SinCos(angles.X * (MathF.PI / 180f));
        (float sy, float cy) = MathF.SinCos(angles.Y * (MathF.PI / 180f));
        (float sr, float cr) = MathF.SinCos(angles.Z * (MathF.PI / 180f));

        Vector3 forward = new(cp * cy, cp * sy, -sp);
        Vector3 right = new((-1f * sr * sp * cy) + (-1f * cr * -sy), (-1f * sr * sp * sy) + (-1f * cr * cy), -1f * sr * cp);

        return (forward, right);
    }

    private void WalkMove()
    {
        (Vector3 forward, Vector3 right) = FlatAxes();

        Vector3 wishDirection = new(
            (forward.X * _forwardMove) + (right.X * _sideMove),
            (forward.Y * _forwardMove) + (right.Y * _sideMove),
            0f);

        float wishSpeed = wishDirection.Length();
        wishDirection = Normalize(wishDirection);
        wishSpeed = Math.Clamp(wishSpeed, 0f, _maxSpeed);

        _player.Velocity = _player.Velocity with { Z = 0f };

        float accelerate = _convars.Accelerate;
        float friction = _convars.Friction * _player.SurfaceFriction;
        float wishSpeedThreshold = 100f * friction / _convars.Accelerate;

        if (wishSpeed > 0f && wishSpeed < wishSpeedThreshold && !_player.Conditions.Has(CondLostFooting))
        {
            float speed = _player.Velocity.Length();
            float control = speed < _convars.StopSpeed ? _convars.StopSpeed : speed;
            accelerate = (control * friction / wishSpeed) + 1f;
        }

        Accelerate(wishDirection, wishSpeed, accelerate);

        float newSpeed = _player.Velocity.Length();

        if (newSpeed > _maxSpeed)
        {
            float scale = _maxSpeed / newSpeed;
            _player.Velocity = new Vector3(_player.Velocity.X * scale, _player.Velocity.Y * scale, _player.Velocity.Z);
        }

        // tf_gamemovement.cpp:1817-1828: z is 0 here, so the whole vector is normalised.
        float forwardPull = GetMovementForwardPull();

        if (forwardPull > 0f)
        {
            _player.Velocity += forward * forwardPull;

            if (Length2D(_player.Velocity) > _maxSpeed)
            {
                _player.Velocity = Normalize(_player.Velocity) * _maxSpeed;
            }
        }

        // tf_clamp_back_speed 0.9 above tf_clamp_back_speed_min 100 (tf_gamemovement.cpp:47-48, :1832).
        if (_player.Velocity.Length() > 100f)
        {
            float dot = Vector3.Dot(forward, _player.Velocity);

            if (dot < 0f)
            {
                Vector3 backMove = forward * dot;
                Vector3 rightMove = right * Vector3.Dot(right, _player.Velocity);
                float backSpeed = backMove.Length();
                float maxBackSpeed = _maxSpeed * 0.9f;

                if (backSpeed > maxBackSpeed)
                {
                    backMove *= maxBackSpeed / backSpeed;
                }

                _player.Velocity = backMove + rightMove;

                newSpeed = _player.Velocity.Length();

                if (newSpeed > _maxSpeed)
                {
                    float scale = _maxSpeed / newSpeed;
                    _player.Velocity = new Vector3(_player.Velocity.X * scale, _player.Velocity.Y * scale, _player.Velocity.Z);
                }
            }
        }

        _player.Velocity += _player.BaseVelocity;

        if (_player.Velocity.Length() < 1f)
        {
            _player.Velocity = Vector3.Zero;
            return;
        }

        Vector3 destination = new(
            _player.Origin.X + (_player.Velocity.X * _frametime),
            _player.Origin.Y + (_player.Velocity.Y * _frametime),
            _player.Origin.Z);

        BspTrace trace = TracePlayerBBox(_player.Origin, destination);

        if (trace.Fraction >= 1f)
        {
            _player.Origin = EndPos(_player.Origin, destination, trace);
            _player.Velocity -= _player.BaseVelocity;
            return;
        }

        StepMove(destination, trace);
        _player.Velocity -= _player.BaseVelocity;
        CheckKartWallBumping();
    }

    private void Accelerate(Vector3 wishDirection, float wishSpeed, float accel)
    {
        if (!CanAccelerate())
        {
            return;
        }

        float currentSpeed = Vector3.Dot(_player.Velocity, wishDirection);
        float addSpeed = wishSpeed - currentSpeed;

        if (addSpeed <= 0f)
        {
            return;
        }

        float accelSpeed = MathF.Min(accel * _frametime * wishSpeed * _player.SurfaceFriction, addSpeed);

        _player.Velocity += accelSpeed * wishDirection;
    }

    /// <summary><c>CTFGameMovement::CanAccelerate</c>: only <c>TF_STATE_ACTIVE</c>, and not while water jumping.</summary>
    private bool CanAccelerate() => _player.PlayerState is null or 0 && _player.WaterJumpTime == 0f;

    private float GetAirSpeedCap()
    {
        if (_player.GrapplingHook is not null)
        {
            if (CarryingRune() == CondRuneAgility)
            {
                return _player.PlayerClass is ClassSoldier or ClassHeavy ? 850f : 950f;
            }

            return GrapplingHookMoveSpeed;
        }

        if (_player.Conditions.Has(CondShieldCharge))
        {
            return MaxChargeSpeed;
        }

        float cap = 30f;

        if (_player.Conditions.Has(CondParachute))
        {
            cap *= ParachuteAirControl;
        }

        if (_player.Conditions.Has(CondKart))
        {
            if (_player.Conditions.Has(CondKartDash))
            {
                return KartDashSpeed;
            }

            cap *= KartAirControl;
        }

        // tf_gamemovement.cpp:2081-2094.
        float airControl = Items.OnPlayer("mod_air_control", 1f);

        if (_player.Conditions.Has(CondBlastJumping))
        {
            airControl = Items.OnPlayer("mod_air_control_blast_jump", airControl);
        }

        if (_player.Conditions.Has(CondRocketPack))
        {
            cap *= 0.5f;
        }

        return cap * airControl;
    }

    private void AirMove()
    {
        // tf_gamemovement.cpp:2104: a grappling player steps along his pull when it meets something.
        if (_player.GrapplingHook is not null)
        {
            Vector3 destination = _player.Origin + (_player.Velocity * _frametime);
            BspTrace pull = TracePlayerBBox(_player.Origin, destination);

            if (pull.Fraction < 1f)
            {
                StepMove(destination, pull);
                return;
            }
        }

        (Vector3 forward, Vector3 right) = FlatAxes();

        Vector3 wishVelocity = new(
            (forward.X * _forwardMove) + (right.X * _sideMove),
            (forward.Y * _forwardMove) + (right.Y * _sideMove),
            0f);

        float wishSpeed = wishVelocity.Length();
        Vector3 wishDirection = Normalize(wishVelocity);

        if (wishSpeed != 0f && wishSpeed > _maxSpeed)
        {
            wishSpeed = _maxSpeed;
        }

        float airAccel = _convars.AirAccelerate;
        float wallSlideCoeff = 0f;

        if (_player.Conditions.Has(CondAirCurrent))
        {
            airAccel *= AirCurrentAirControlMult;
            wallSlideCoeff = Math.Clamp(1f - AirCurrentFrictionMult, 0f, 1f);
        }

        AirAccelerate(wishDirection, wishSpeed, airAccel);

        // tf_gamemovement.cpp:2169-2183: cut back in the plane, the fall speed kept.
        float forwardPull = GetMovementForwardPull();

        if (forwardPull > 0f)
        {
            _player.Velocity += forward * forwardPull;

            if (Length2D(_player.Velocity) > _maxSpeed)
            {
                float z = _player.Velocity.Z;
                _player.Velocity = (Normalize(_player.Velocity with { Z = 0f }) * _maxSpeed) with { Z = z };
            }
        }

        _player.Velocity += _player.BaseVelocity;

        if ((TryPlayerMove(null, null, wallSlideCoeff) & 2) != 0)
        {
            CheckKartWallBumping();
        }

        _player.Velocity -= _player.BaseVelocity;
    }

    private void AirAccelerate(Vector3 wishDirection, float wishSpeed, float accel)
    {
        if (_player.IsDead)
        {
            return;
        }

        float wishSpd = MathF.Min(wishSpeed, GetAirSpeedCap());
        float currentSpeed = Vector3.Dot(_player.Velocity, wishDirection);
        float addSpeed = wishSpd - currentSpeed;

        if (addSpeed <= 0f)
        {
            return;
        }

        float accelSpeed = MathF.Min(accel * wishSpeed * _frametime * _player.SurfaceFriction, addSpeed);

        _player.Velocity += accelSpeed * wishDirection;
    }

    /// <returns>The blocked bits: 1 a floor, 2 a wall or step, 4 all solid (<c>gamemovement.cpp:2558</c>).</returns>
    private int TryPlayerMove(Vector3? firstDest, BspTrace? firstTrace, float slideMultiplier)
    {
        const int NumBumps = 4;

        int blocked = 0;
        int numPlanes = 0;
        Span<Vector3> planes = stackalloc Vector3[MaxClipPlanes];
        Vector3 originalVelocity = _player.Velocity;
        Vector3 primalVelocity = _player.Velocity;
        float allFraction = 0f;
        float timeLeft = _frametime;
        Vector3 newVelocity = Vector3.Zero;

        for (int bump = 0; bump < NumBumps; bump++)
        {
            if (_player.Velocity.Length() == 0f)
            {
                break;
            }

            Vector3 end = _player.Origin + (timeLeft * _player.Velocity);
            BspTrace pm = firstDest is { } dest && dest == end && firstTrace is { } given
                ? given
                : TracePlayerBBox(_player.Origin, end);

            allFraction += pm.Fraction;

            if (pm.AllSolid)
            {
                _player.Velocity = Vector3.Zero;
                return 4;
            }

            if (pm.Fraction > 0f)
            {
                Vector3 endPos = EndPos(_player.Origin, end, pm);

                if (pm.Fraction >= 1f)
                {
                    BspTrace stuck = TracePlayerBBox(endPos, endPos);

                    if (stuck.StartSolid || stuck.Fraction < 1f)
                    {
                        _player.Velocity = Vector3.Zero;
                        break;
                    }
                }

                _player.Origin = endPos;
                originalVelocity = _player.Velocity;
                numPlanes = 0;
            }

            if (pm.Fraction >= 1f)
            {
                break;
            }

            // The floor and wall bits feed CheckKartWallBumping.
            Vector3 normal = Normal(pm);

            if (normal.Z > 0.7f)
            {
                blocked |= 1;
            }

            if (normal.Z == 0f)
            {
                blocked |= 2;
            }

            timeLeft -= timeLeft * pm.Fraction;

            if (numPlanes >= MaxClipPlanes)
            {
                _player.Velocity = Vector3.Zero;
                break;
            }

            planes[numPlanes++] = normal;

            if (numPlanes == 1 && !_player.OnGround)
            {
                for (int i = 0; i < numPlanes; i++)
                {
                    float overbounce = planes[i].Z > 0.7f ? 1f : 1f + (_convars.Bounce * (1f - _player.SurfaceFriction));
                    newVelocity = ClipVelocity(originalVelocity, planes[i], overbounce, slideMultiplier);
                    originalVelocity = newVelocity;
                }

                _player.Velocity = newVelocity;
                originalVelocity = newVelocity;
            }
            else
            {
                int i;

                for (i = 0; i < numPlanes; i++)
                {
                    _player.Velocity = ClipVelocity(originalVelocity, planes[i], 1f, slideMultiplier);

                    int j;

                    for (j = 0; j < numPlanes; j++)
                    {
                        if (j != i && Vector3.Dot(_player.Velocity, planes[j]) < 0f)
                        {
                            break;
                        }
                    }

                    if (j == numPlanes)
                    {
                        break;
                    }
                }

                if (i == numPlanes)
                {
                    if (numPlanes != 2)
                    {
                        _player.Velocity = Vector3.Zero;
                        break;
                    }

                    Vector3 direction = Normalize(Vector3.Cross(planes[0], planes[1]));
                    _player.Velocity = direction * Vector3.Dot(direction, _player.Velocity);
                }

                if (Vector3.Dot(_player.Velocity, primalVelocity) <= 0f)
                {
                    _player.Velocity = Vector3.Zero;
                    break;
                }
            }
        }

        if (allFraction == 0f)
        {
            _player.Velocity = Vector3.Zero;
        }

        return blocked;
    }

    private static Vector3 ClipVelocity(Vector3 velocity, Vector3 normal, float overbounce, float redirectCoeff)
    {
        float blocked = Vector3.Dot(velocity, normal);
        Vector3 output = velocity - (normal * (blocked * overbounce));

        float adjust = Vector3.Dot(output, normal);

        if (adjust < 0f)
        {
            output -= normal * adjust;
        }

        if (redirectCoeff > 0f)
        {
            float length = output.Length();
            output *= ((-1f * blocked * redirectCoeff) + length) / length;
        }

        return output;
    }

    private void StepMove(Vector3 destination, BspTrace trace)
    {
        BspTrace saveTrace = trace;
        Vector3 position = _player.Origin;
        Vector3 velocity = _player.Velocity;

        // tf_gamemovement.cpp:2845, :2875-2878: without m_bAllowAutoMovement there is no high road, only the low.
        if (!_player.AllowAutoMovement)
        {
            TryPlayerMove(destination, saveTrace, 0f);
            return;
        }

        // The high road: up a step, across, back down.
        Vector3 endPos = _player.Origin with { Z = _player.Origin.Z + _convars.StepSize + DistEpsilon };
        trace = TracePlayerBBox(_player.Origin, endPos);

        if (!trace.StartSolid && !trace.AllSolid)
        {
            _player.Origin = EndPos(_player.Origin, endPos, trace);
        }

        TryPlayerMove(null, null, 0f);

        endPos = _player.Origin with { Z = _player.Origin.Z - _convars.StepSize - DistEpsilon };
        Vector3 from = _player.Origin;
        trace = TracePlayerBBox(from, endPos);

        if (!trace.StartSolid && !trace.AllSolid)
        {
            _player.Origin = EndPos(from, endPos, trace);
        }

        // Off standable ground, or the high road went nowhere: the low road instead. bUpRoad is false whenever
        // bLowRoad is true here (tf_gamemovement.cpp:2870-2875), so the comparison after it never runs.
        bool lowRoad = (trace.Fraction < 1f && trace.Normal.Z < 0.7f) || _player.Origin == position;

        if (!lowRoad)
        {
            return;
        }

        _player.Origin = position;
        _player.Velocity = velocity;
        TryPlayerMove(destination, saveTrace, 0f);
    }

    /// <summary><c>InWater</c> (<c>gamemovement.cpp:3479</c>): above <c>WL_Feet</c>.</summary>
    private bool InWater() => _player.WaterLevel > WaterLevelFeet;

    /// <summary><c>GetCarryingRuneType</c> (<c>tf_player_shared.cpp:11498</c>) as its condition, or −1 for <c>RUNE_NONE</c>.</summary>
    private int CarryingRune()
    {
        foreach (int condition in RuneConditions)
        {
            if (_player.Conditions.Has(condition))
            {
                return condition;
            }
        }

        return -1;
    }

    private Vector3 WorldSpaceCenter() => _player.Origin + ((PlayerMins + PlayerMaxs) * 0.5f);

    private bool IsControlStunned() =>
        _player.StunActive && _player.Conditions.Has(CondStunned) && (_player.StunFlags & StunControls) != 0;

    private bool IsLoserStateStunned() =>
        _player.StunActive && _player.Conditions.Has(CondStunned) && (_player.StunFlags & StunLoserState) != 0;

    /// <summary><c>GetAmountStunned( TF_STUN_MOVEMENT )</c> (<c>tf_player_shared.cpp:9938</c>).</summary>
    private float AmountStunned() =>
        _player.StunActive && _player.Conditions.Has(CondStunned) && (_player.StunFlags & StunMovement) != 0 &&
        _player.StunExpireTime > _player.CurTime
            ? Math.Clamp(_player.StunAmount, 0, 255) * (1f / 255f)
            : 0f;

    /// <summary>
    /// <c>StunMove</c> (<c>tf_gamemovement.cpp:537</c>). The final-countdown and ConTracker freeze at its end is not ported:
    /// both read state the demo does not hand this class (B450).
    /// </summary>
    private void StunMove()
    {
        bool controlStunned = IsControlStunned();

        if (controlStunned || IsLoserStateStunned())
        {
            bool attackDown = (_buttons & (InAttack2 | InAttack)) != 0;

            _buttons = attackDown && _player.PlayerClass == ClassHeavy && _player.ActiveWeaponIsMinigun ? InAttack2 : 0;

            if (controlStunned)
            {
                _forwardMove = 0f;
                _sideMove = 0f;
                _upMove = 0f;
            }
        }

        float amount = AmountStunned();

        if (amount != 0f)
        {
            if (_player.StunLerpTarget - amount != 0f)
            {
                _player.LastMovementStunChange = _player.CurTime;
                _player.StunLerpTarget = amount;
                _player.StunNeedsFadeOut = true;
            }

            ScaleStunnedMove(amount);
            return;
        }

        if (_player.LastMovementStunChange != 0f)
        {
            if (_player.StunNeedsFadeOut)
            {
                _player.LastMovementStunChange = _player.CurTime;
                _player.StunNeedsFadeOut = false;
            }

            float current = RemapValClamped(_player.CurTime - _player.LastMovementStunChange, 0.2f, 0f, 0f, 1f);

            if (current != 0f)
            {
                ScaleStunnedMove(_player.StunLerpTarget * current);
            }
            else
            {
                _player.StunLerpTarget = 0f;
                _player.LastMovementStunChange = 0f;
            }
        }
    }

    private void ScaleStunnedMove(float amount)
    {
        _forwardMove *= 1f - amount;
        _sideMove *= 1f - amount;

        if ((_player.StunFlags & StunForwardOnly) != 0)
        {
            _forwardMove = 0f;
        }
    }

    /// <summary>
    /// <c>CanMoveDuringTaunt</c> (<c>tf_player_shared.cpp:13097</c>). Its competitive-mode refusals read the game rules, which
    /// this class is not handed (B450); <c>tf_allow_sliding_taunt</c> is server-only.
    /// </summary>
    private bool CanMoveDuringTaunt() =>
        _player.Conditions.Has(CondKart) ||
        ((_player.Conditions.Has(CondTaunting) || _player.Conditions.Has(CondThriller)) && _player.AllowMoveDuringTaunt);

    /// <summary>
    /// <c>TauntMove</c> (<c>tf_gamemovement.cpp:633</c>). <c>CanPlayerMove</c> is taken as true: its refusals are the game
    /// rules' (B450). <c>SetTauntYaw</c> turns the model and moves nothing.
    /// </summary>
    private void TauntMove()
    {
        if (_player.Conditions.Has(CondKart))
        {
            VehicleMove();
            return;
        }

        if (!_player.Conditions.Has(CondTaunting) || !CanMoveDuringTaunt())
        {
            _player.CurrentTauntMoveSpeed = 0f;
            return;
        }

        // Stryker disable once all : emptying the guard leaves `taunt` unassigned, and Safe Mode drops the method.
        if (_player.TauntMovement is not { } taunt)
        {
            // The taunt item's attributes are not known: the move is declined rather than guessed.
            _declined = true;
            return;
        }

        float maxMoveSpeed = taunt.Speed;
        float moveDirection = 1f;

        if (!taunt.ForceForward)
        {
            moveDirection = 0f;

            if (_forwardMove > 0f && _convars.ForwardSpeed > 0f)
            {
                moveDirection += _forwardMove / _convars.ForwardSpeed;
            }
            else if (_forwardMove < 0f && _convars.BackSpeed > 0f)
            {
                moveDirection += _forwardMove / _convars.BackSpeed;
            }

            moveDirection = Math.Clamp(moveDirection, -1f, 1f);
        }

        float sign = moveDirection != 0f ? 1f : -1f;

        _player.CurrentTauntMoveSpeed = taunt.Acceleration > 0f
            ? Math.Clamp(
                _player.CurrentTauntMoveSpeed + (sign * (_frametime / taunt.Acceleration) * maxMoveSpeed), 0f, maxMoveSpeed)
            : maxMoveSpeed;

        float smoothMoveSpeed = maxMoveSpeed > 0f
            ? SimpleSpline(_player.CurrentTauntMoveSpeed / maxMoveSpeed) * maxMoveSpeed
            : 0f;

        _maxSpeed = maxMoveSpeed;
        _forwardMove = moveDirection * smoothMoveSpeed;
        _clientMaxSpeed = maxMoveSpeed;
    }

    /// <summary>
    /// <c>VehicleMove</c> (<c>tf_gamemovement.cpp:738</c>). <c>m_iKartState</c> and the lean feed the animation alone. The
    /// kart's steering is <c>CreateVehicleMove</c>'s, on the client's usercmd before it was recorded, so the yaw is the cmd's.
    /// </summary>
    private void VehicleMove()
    {
        float maxMoveSpeed = KartNormalSpeed;
        float targetSpeed = KartIdleSpeed;
        float acceleration = KartCoastAccel;
        float current = _player.CurrentTauntMoveSpeed;
        bool input = false;

        if (_forwardMove > 0f)
        {
            float normalized = _convars.ForwardSpeed > 0f ? _forwardMove / _convars.ForwardSpeed : 0f;
            normalized = MathF.Min(normalized, 1f);
            targetSpeed = KartNormalSpeed;

            if (targetSpeed > current)
            {
                acceleration = (current < KartSlowMovingThreshold ? KartSlowMovingAccel : KartNormalAccel) * normalized;
            }

            input = true;
        }
        else if (_forwardMove < 0f)
        {
            float normalized = _convars.BackSpeed > 0f ? _forwardMove / _convars.BackSpeed : 0f;
            normalized = normalized < -1f ? 1f : -normalized;

            if (current > 0f)
            {
                targetSpeed = KartBrakeSpeed;

                if (targetSpeed < current)
                {
                    acceleration = KartBrakeAccel * normalized;
                }
            }
            else if (_player.OldForwardMove >= 0f || current < 0f || _player.VehicleReverseTime < _player.CurTime)
            {
                targetSpeed = KartReverseSpeed;

                if (targetSpeed < current)
                {
                    acceleration = KartBrakeAccel * normalized;
                }
            }
            else if (_player.VehicleReverseTime >= float.MaxValue)
            {
                // Stall, then reverse.
                _player.VehicleReverseTime = _player.CurTime + 0.6f;
            }

            input = true;
        }

        if (current > 0f)
        {
            _player.VehicleReverseTime = float.MaxValue;
        }

        if (input && (current < 0f) != (targetSpeed < 0f))
        {
            acceleration = KartBrakeAccel;
        }

        if (_player.Conditions.Has(CondBombHead))
        {
            maxMoveSpeed *= KartBombHeadScale;
            acceleration *= KartBombHeadScale;
        }

        float targetMoveSpeed = Approach(targetSpeed, current, acceleration * _frametime);
        float smoothMoveSpeed = Bias(MathF.Abs(current) / maxMoveSpeed, 0.7f) * maxMoveSpeed * Sign(targetMoveSpeed);

        if (_player.Conditions.Has(CondKartDash))
        {
            maxMoveSpeed = KartDashSpeed;
            targetMoveSpeed = KartDashSpeed;
            smoothMoveSpeed = KartDashSpeed;
        }

        _player.CurrentTauntMoveSpeed = targetMoveSpeed;

        _maxSpeed = maxMoveSpeed;
        _forwardMove = smoothMoveSpeed;
        _clientMaxSpeed = maxMoveSpeed;
        _sideMove = 0f;
    }

    /// <summary>
    /// <c>CheckKartWallBumping</c> (<c>tf_gamemovement.cpp:1968</c>), the client's half: the kart's speed clamped to what the
    /// move kept. The flinch and spark are effects; the bounce and the stop are <c>GAME_DLL</c>.
    /// </summary>
    private void CheckKartWallBumping()
    {
        if (!_player.Conditions.Has(CondKart))
        {
            return;
        }

        float maxSpeed = _player.Velocity.Length();

        _player.CurrentTauntMoveSpeed = Math.Clamp(_player.CurrentTauntMoveSpeed, -maxSpeed, maxSpeed);
    }

    /// <summary>
    /// <c>GrapplingHookMove</c> (<c>tf_gamemovement.cpp:342</c>) with <c>tf_grapplinghook_use_acceleration</c> at its
    /// default 0. Player-destruction team leaders are not told apart: the game type is not handed this class (B450).
    /// </summary>
    private void GrapplingHookMove()
    {
        // Stryker disable once all : emptying the guard leaves `hook` unassigned, and Safe Mode drops the method.
        if (_player.GrapplingHook is not { } hook)
        {
            return;
        }

        if (IsControlStunned())
        {
            _forwardMove = 0f;
            _sideMove = 0f;
            _upMove = 0f;
            _buttons = 0;
            return;
        }

        SetGroundEntity(null);

        Vector3 desired = hook.Center - WorldSpaceCenter();

        if (hook.IsPlayer)
        {
            desired += (hook.HookDirection ?? Normalize(desired)) * -GrapplingHookFollowDistance;
        }

        float maxSpeed = GrapplingHookMoveSpeed;
        bool hasTheFlag = _player.HasTheFlag;
        int rune = CarryingRune();
        bool lightRune = rune < 0 || rune == CondRuneAgility;

        if (rune == CondRuneAgility && !hasTheFlag)
        {
            maxSpeed = 950f;
        }

        if (_player.PlayerClass == ClassHeavy && !hasTheFlag)
        {
            maxSpeed *= 0.7f;
        }
        else if (hasTheFlag)
        {
            float scoutPenalty = lightRune ? 0.8f : 0.65f;
            float otherPenalty = lightRune ? 0.65f : 0.5f;

            maxSpeed *= _player.PlayerClass == ClassScout ? scoutPenalty : otherPenalty;
        }
        else if (_player.PlayerClass == ClassPyro && _player.Conditions.Has(CondGrappledToPlayer))
        {
            maxSpeed *= 0.7f;
        }

        _maxSpeed = maxSpeed;

        float distance = desired.Length();

        _player.Velocity = distance > maxSpeed * _frametime ? desired * (maxSpeed / distance) : desired / _frametime;

        float distanceSquared = Vector3.DistanceSquared(_player.Origin, hook.Origin);

        if (distanceSquared < 10000f)
        {
            _player.Velocity = Normalize(_player.Velocity) * RemapValClamped(distanceSquared, 6400f, 10000f, 0f, maxSpeed);
        }

        _forwardMove = 0f;
        _sideMove = 0f;
        _upMove = 0f;
    }

    /// <summary>
    /// <c>FullWalkMove</c>'s parachute (<c>tf_gamemovement.cpp:2626</c>). *Interpolated:* Valve's <c>abs</c> is read as the
    /// float overload; an integer one would floor the speed before it is compared.
    /// </summary>
    private void ParachuteClamp()
    {
        Vector3 v = _player.Velocity;

        if (!_player.Conditions.Has(CondParachute) || v.Z >= 0f)
        {
            return;
        }

        float z = MathF.Max(v.Z, _player.Conditions.Has(CondBurning) ? ParachuteMaxSpeedOnFireZ : ParachuteMaxSpeedZ);
        float reductionX = MathF.Abs(v.X) > ParachuteMaxSpeedXy ? ((MathF.Abs(v.X) - ParachuteMaxSpeedXy) / 3f) - 10f : 0f;
        float reductionY = MathF.Abs(v.Y) > ParachuteMaxSpeedXy ? ((MathF.Abs(v.Y) - ParachuteMaxSpeedXy) / 3f) - 10f : 0f;

        _player.Velocity = new Vector3(
            Math.Clamp(v.X, -ParachuteMaxSpeedXy - reductionX, ParachuteMaxSpeedXy + reductionX),
            Math.Clamp(v.Y, -ParachuteMaxSpeedXy - reductionY, ParachuteMaxSpeedXy + reductionY),
            z);
    }

    /// <summary><c>CTFGameMovement::CheckWater</c> (<c>tf_gamemovement.cpp:1452</c>): feet, then eyes, then waist.</summary>
    private void CheckWater()
    {
        // Stryker disable once all : emptying the guard leaves `contents` unassigned, and Safe Mode drops the method.
        if (PointContents is not { } contents)
        {
            return;
        }

        Vector3 mins = PlayerMins;
        Vector3 maxs = PlayerMaxs;
        Vector3 origin = _player.Origin;
        Vector3 point = new(origin.X + ((mins.X + maxs.X) * 0.5f), origin.Y + ((mins.Y + maxs.Y) * 0.5f), origin.Z + mins.Z + 1f);

        int level = 0;
        int type = 0;
        int found = contents(point);

        if ((found & MaskWater) != 0)
        {
            type = found;
            level = WaterLevelFeet;

            float waistZ = origin.Z + ((mins.Z + maxs.Z) * 0.5f) + 12f;

            if ((contents(point with { Z = origin.Z + _player.ViewOffsetZ }) & MaskWater) != 0)
            {
                level = WaterLevelEyes;
            }
            else if ((contents(point with { Z = waistZ }) & MaskWater) != 0)
            {
                level = WaterLevelWaist;
            }
        }

        if (_player.Conditions.Has(CondSwimmingCurse))
        {
            level = WaterLevelEyes;
        }

        _player.WaterLevel = level;
        _player.WaterType = type;
    }

    /// <summary><c>FullWalkMoveUnderwater</c> (<c>tf_gamemovement.cpp:2583</c>).</summary>
    private void FullWalkMoveUnderwater()
    {
        if (_player.WaterLevel == WaterLevelWaist)
        {
            CheckWaterJump();
        }

        if (_player.Velocity.Z < 0f && _player.WaterJumpTime != 0f)
        {
            _player.WaterJumpTime = 0f;
        }

        if ((_buttons & InJump) != 0)
        {
            CheckJumpButton();
        }

        WaterMove();
        CategorizePosition();

        if (_player.OnGround)
        {
            _player.Velocity = _player.Velocity with { Z = 0f };
        }
    }

    /// <summary>
    /// <c>CheckWaterJumpButton</c> (<c>tf_gamemovement.cpp:938</c>). The <c>cannot_swim</c> attribute is taken as 0 with the
    /// other item attributes (B450). It counts <c>m_flWaterJumpTime</c> down by seconds, as Valve's does.
    /// </summary>
    private bool CheckWaterJumpButton()
    {
        if (_player.WaterJumpTime != 0f)
        {
            _player.WaterJumpTime = MathF.Max(0f, _player.WaterJumpTime - _frametime);
            return false;
        }

        bool noEffects = _player.Conditions.Has(CondSwimmingNoEffects);

        if (_player.WaterLevel < WaterLevelWaist && !noEffects)
        {
            return true;
        }

        SetGroundEntity(null);

        if (_player.WaterType == ContentsWater || noEffects)
        {
            _player.Velocity = _player.Velocity with { Z = 100f };
        }
        else if (_player.WaterType == ContentsSlime)
        {
            _player.Velocity = _player.Velocity with { Z = 80f };
        }

        return false;
    }

    /// <summary><c>CTFGameMovement::CheckWaterJump</c> (<c>tf_gamemovement.cpp:2464</c>): a hop out onto a ledge.</summary>
    private void CheckWaterJump()
    {
        bool jump = (_buttons & InJump) != 0;
        (Vector3 forward, Vector3 right) = AngleVectors(_viewAngles);

        if (_player.WaterJumpTime != 0f || _player.Velocity.Z < -180f)
        {
            return;
        }

        Vector3 flatVelocity = _player.Velocity with { Z = 0f };
        float currentSpeed = flatVelocity.Length();
        flatVelocity = Normalize(flatVelocity);

        Vector3 flatForward = Normalize(new Vector3(
            (forward.X * _forwardMove) + (right.X * _sideMove),
            (forward.Y * _forwardMove) + (right.Y * _sideMove),
            0f));

        if (currentSpeed != 0f && Vector3.Dot(flatVelocity, flatForward) < 0f && !jump)
        {
            return;
        }

        Vector3 start = _player.Origin + ((PlayerMins + PlayerMaxs) * 0.5f);
        Vector3 end = start + (TfWaterJumpForward * flatForward);
        BspTrace trace = TracePlayerBBox(start, end);

        if (trace.Fraction >= 1f)
        {
            return;
        }

        start = start with { Z = _player.Origin.Z + _player.ViewOffsetZ + WaterJumpHeight };
        end = start + (TfWaterJumpForward * flatForward);
        _player.WaterJumpVelocity = Normal(trace) * -50f;

        if (TracePlayerBBox(start, end).Fraction < 1f)
        {
            return;
        }

        start = end;
        end = end with { Z = end.Z - 1024f };
        trace = TracePlayerBBox(start, end);

        if (trace.Fraction < 1f && trace.Normal.Z >= 0.7f)
        {
            _player.Velocity = _player.Velocity with { Z = TfWaterJumpUp };
            _player.WaterJumpTime = 2000f;
        }
    }

    /// <summary><c>WaterJump</c> (<c>gamemovement.cpp:1348</c>).</summary>
    private void WaterJump()
    {
        _player.WaterJumpTime = MathF.Min(_player.WaterJumpTime, 10000f);

        if (_player.WaterJumpTime == 0f)
        {
            return;
        }

        _player.WaterJumpTime -= 1000f * _frametime;

        if (_player.WaterJumpTime <= 0f || _player.WaterLevel == 0)
        {
            _player.WaterJumpTime = 0f;
        }

        _player.Velocity = new Vector3(_player.WaterJumpVelocity.X, _player.WaterJumpVelocity.Y, _player.Velocity.Z);
    }

    /// <summary>
    /// <c>CTFGameMovement::WaterMove</c> (<c>tf_gamemovement.cpp:1537</c>). <c>cannot_swim</c> and <c>swimming_mastery</c>
    /// are taken as 0 with the other item attributes (B450).
    /// </summary>
    private void WaterMove()
    {
        (Vector3 forward, Vector3 right) = AngleVectors(_viewAngles);
        Vector3 wishVelocity = (forward * _forwardMove) + (right * _sideMove);

        if ((_buttons & InJump) != 0)
        {
            if (_player.WaterLevel == WaterLevelEyes)
            {
                wishVelocity.Z += _clientMaxSpeed;
            }
        }
        else if (_forwardMove == 0f && _sideMove == 0f && _upMove == 0f)
        {
            wishVelocity.Z -= 60f;
        }
        else
        {
            wishVelocity.Z += _upMove;
        }

        float wishSpeed = wishVelocity.Length();

        if (wishSpeed > _maxSpeed)
        {
            wishVelocity *= _maxSpeed / wishSpeed;
            wishSpeed = _maxSpeed;
        }

        wishSpeed *= 0.8f;

        float speed = _player.Velocity.Length();
        float newSpeed = 0f;

        if (speed != 0f)
        {
            newSpeed = speed - (_frametime * speed * _convars.Friction * _player.SurfaceFriction);

            if (newSpeed < 0.1f)
            {
                newSpeed = 0f;
            }

            _player.Velocity *= newSpeed / speed;
        }

        if (_player.Conditions.Has(CondGhost))
        {
            float accelSpeed = _convars.Accelerate * wishSpeed * _frametime * _player.SurfaceFriction;
            _player.Velocity += accelSpeed * Normalize(wishVelocity);

            float xySpeed = new Vector2(_player.Velocity.X, _player.Velocity.Y).Length();

            if (xySpeed > GhostXySpeed)
            {
                float scale = GhostXySpeed / xySpeed;
                _player.Velocity = new Vector3(_player.Velocity.X * scale, _player.Velocity.Y * scale, _player.Velocity.Z);
            }
        }
        else if (wishSpeed >= 0.1f)
        {
            float addSpeed = wishSpeed - newSpeed;

            if (addSpeed > 0f)
            {
                float accelSpeed = MathF.Min(_convars.Accelerate * wishSpeed * _frametime * _player.SurfaceFriction, addSpeed);
                _player.Velocity += accelSpeed * Normalize(wishVelocity);
            }
        }

        _player.Velocity += _player.BaseVelocity;

        Vector3 destination = _player.Origin + (_frametime * _player.Velocity);
        BspTrace trace = TracePlayerBBox(_player.Origin, destination);

        if (trace.Fraction >= 1f)
        {
            // m_bAllowAutoMovement is true for a TF player: press down from a step above.
            Vector3 start = destination with { Z = destination.Z + _convars.StepSize + 1f };
            trace = TracePlayerBBox(start, destination);

            if (!trace.StartSolid && !trace.AllSolid)
            {
                _player.Origin = EndPos(start, destination, trace);
                _player.Velocity -= _player.BaseVelocity;
                return;
            }

            TryPlayerMove(null, null, 0f);
        }
        else if (!_player.OnGround)
        {
            TryPlayerMove(null, null, 0f);
        }
        else
        {
            StepMove(destination, trace);
        }

        _player.Velocity -= _player.BaseVelocity;
    }

    /// <summary><c>Sign</c> (<c>mathlib.h:736</c>): zero is positive.</summary>
    private static float Sign(float x) => x < 0f ? -1f : 1f;

    /// <summary><c>SimpleSpline</c> (<c>mathlib.h:1146</c>).</summary>
    private static float SimpleSpline(float value) => (3f * value * value) - (2f * value * value * value);

    /// <summary><c>Bias</c> (<c>mathlib_base.cpp:1455</c>), with Valve's −1.4427 for 1 / log 0.5.</summary>
    private static float Bias(float x, float amount) => MathF.Pow(x, MathF.Log(amount) * -1.4427f);

    /// <summary><c>Approach</c> (<c>mathlib_base.cpp:3433</c>).</summary>
    private static float Approach(float target, float value, float speed)
    {
        float delta = target - value;

        if (delta > speed)
        {
            return value + speed;
        }

        return delta < -speed ? value - speed : target;
    }

    /// <summary><c>RemapValClamped</c> (<c>mathlib.h:619</c>); every caller here passes A ≠ B, so its A == B branch is not ported.</summary>
    private static float RemapValClamped(float value, float a, float b, float c, float d)
    {
        float t = Math.Clamp((value - a) / (b - a), 0f, 1f);

        return c + ((d - c) * t);
    }

    /// <summary>
    /// <c>ent_gravity · GetCurrentGravity()</c> (<c>gamemovement.cpp:1250-1257</c>): <c>GetGravity()</c> when nonzero, times
    /// <c>sv_gravity · GetGravityMultiplier()</c> (<c>movevars_shared.cpp:25-35</c>). The multiplier is 1 here: <c>C_TFGameRules</c>
    /// sets it to 1.0 (<c>tf_gamerules.cpp:3450</c>) and no demo measured carries it (probe <c>schema</c>, 2009 to 2026).
    /// </summary>
    private float Gravity => (_player.Gravity != 0f ? _player.Gravity : 1f) * _convars.Gravity;

    private void StartGravity()
    {
        _player.Velocity = _player.Velocity with
        {
            Z = _player.Velocity.Z - (Gravity * 0.5f * _frametime) + (_player.BaseVelocity.Z * _frametime),
        };
        _player.BaseVelocity = _player.BaseVelocity with { Z = 0f };

        CheckVelocity();
    }

    private void FinishGravity()
    {
        _player.Velocity = _player.Velocity with { Z = _player.Velocity.Z - (Gravity * _frametime * 0.5f) };

        CheckVelocity();
    }

    private void CheckVelocity()
    {
        float bound = _convars.MaxVelocity;
        Vector3 v = _player.Velocity;

        _player.Velocity = new Vector3(Bound(v.X, bound), Bound(v.Y, bound), Bound(v.Z, bound));
    }

    private static float Bound(float value, float bound) =>
        float.IsNaN(value) ? 0f : Math.Clamp(value, -bound, bound);
}
