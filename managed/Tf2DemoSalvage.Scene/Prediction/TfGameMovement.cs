using System;
using System.Numerics;

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
/// <c>StartGravity</c>, <c>FinishGravity</c>, <c>CheckVelocity</c>, <c>SetGroundEntity</c>.
///
/// **Declined, so the networked velocity stands** (<see cref="ProcessMovement"/> returns false): water above the
/// feet, a taunt, a kart, ghost mode, a grappling hook, a deployed parachute, a stun, and the swimming conditions —
/// <c>WaterMove</c>, <c>TauntMove</c>, <c>VehicleMove</c>, <c>GrapplingHookMove</c> and <c>StunMove</c> are not ported.
///
/// **Taken as their defaults, each filed under B450:** item attributes (<c>mod_jump_height</c>, <c>mod_air_control</c>,
/// <c>CanAirDash</c>'s extra dashes, <c>GetMovementForwardPull</c>), the ground's surfaceprop (friction, jump factor,
/// max-speed factor all 1), a moving ground's velocity, <c>m_flGravity</c> and the game rules' gravity multiplier (1),
/// <c>CanJump</c> and <c>CanDuck</c> (true), and <c>CheckStuck</c>, which the client runs once a second.
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
    private const int CondGhost = 77;
    private const int CondParachute = 80;
    private const int CondKart = 82;
    private const int CondSwimmingCurse = 86;
    private const int CondGrapplingHook = 98;
    private const int CondSwimmingNoEffects = 107;
    private const int CondRocketPack = 125;
    private const int CondLostFooting = 126;
    private const int CondAirCurrent = 127;

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

    // CMoveData.
    private float _forwardMove;
    private float _sideMove;
    private float _upMove;
    private uint _buttons;
    private Vector3 _viewAngles;
    private float _maxSpeed;
    private float _clientMaxSpeed;
    private bool _speedCropped;

    /// <summary>A movement simulation over a world.</summary>
    /// <param name="trace">The world, with whatever boxes block the player.</param>
    /// <param name="convars">The server's movement ConVars.</param>
    public TfGameMovement(PlayerTraceRay trace, MovementConVars convars)
    {
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _convars = convars ?? throw new ArgumentNullException(nameof(convars));
    }

    /// <summary><c>CPrediction::RunCommand</c>'s movement half: <c>SetupMove</c>, <c>ProcessMovement</c>, <c>FinishMove</c>.</summary>
    /// <param name="player">The player, advanced in place.</param>
    /// <param name="command">The usercmd.</param>
    /// <param name="frametime">The tick interval.</param>
    /// <param name="first">
    /// Whether this is the first command after the networked state was restored — <c>m_bGameCodeMovedPlayer</c>, which
    /// is true when the network origin differs from the last predicted one, and asks for a full <c>CategorizePosition</c>.
    /// </param>
    /// <returns>False when the move is one this port declines, or there is no world; the player is then untouched.</returns>
    public bool ProcessMovement(ref PredictedPlayer player, UserCommand command, float frametime, bool first)
    {
        ArgumentNullException.ThrowIfNull(command);

        PlayerConditions conditions = player.Conditions;

        if (conditions.Has(CondTaunting) || conditions.Has(CondKart) || conditions.Has(CondGhost) ||
            conditions.Has(CondGrapplingHook) || conditions.Has(CondParachute) || conditions.Has(CondStunned) ||
            conditions.Has(CondSwimmingCurse) || conditions.Has(CondSwimmingNoEffects) || player.WaterLevel > 1)
        {
            return false;
        }

        _player = player;
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
        HighMaxSpeedMove();
        PlayerMove(first);

        // FinishMove (gamemovement.cpp:1197) and CPrediction::FinishMove's m_nOldButtons.
        _player.OldButtons = _buttons;
        _player.OldForwardMove = _forwardMove;
        _player.CurTime += frametime;

        if (_noWorld)
        {
            // Cleared for the next command, which starts from a world that may have arrived since.
            _noWorld = false;
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
        if (_trace(start, end, mins, maxs, BspLeafTree.MaskPlayerSolid) is { } trace)
        {
            return trace;
        }

        _noWorld = true;
        return new BspTrace(1f, -1, default, false);
    }

    private static Vector3 EndPos(Vector3 start, Vector3 end, BspTrace trace) => start + ((end - start) * trace.Fraction);

    private static bool Hit(BspTrace trace) => trace.Fraction < 1f || trace.StartSolid;

    private static Vector3 Normal(BspTrace trace) => new(trace.Normal.X, trace.Normal.Y, trace.Normal.Z);

    private void CategorizePosition()
    {
        _player.SurfaceFriction = 1f;

        if (_player.Velocity.Z > 250f)
        {
            SetGroundEntity(null);
            return;
        }

        Vector3 start = _player.Origin;
        Vector3 end = start with { Z = start.Z - 2f };
        bool moveToEndPos = false;

        if (_player.OnGround)
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
                _player.Conditions = Without(_player.Conditions, CondLostFooting);
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

    private static PlayerConditions Without(PlayerConditions conditions, int condition) => (condition / 32) switch
    {
        0 => conditions with { Cond = conditions.Cond & ~(1 << condition) },
        1 => conditions with { Ex = conditions.Ex & ~(1 << (condition - 32)) },
        2 => conditions with { Ex2 = conditions.Ex2 & ~(1 << (condition - 64)) },
        3 => conditions with { Ex3 = conditions.Ex3 & ~(1 << (condition - 96)) },
        _ => conditions with { Ex4 = conditions.Ex4 & ~(1 << (condition - 128)) },
    };

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
        // The ground's own velocity is taken as zero: a moving brush entity's is not known here.
        _player.OnGround = trace is not null;

        if (trace is null)
        {
            return;
        }

        // CategorizeGroundSurface: the default surfaceprop's friction, 0.8 · 1.25 clamped to 1.
        _player.SurfaceFriction = 1f;
        _player.Velocity = _player.Velocity with { Z = 0f };

        // CTFGameMovement::SetGroundEntity.
        _player.AirDash = 0;
        _player.AirDucked = 0;
    }

    private void Duck()
    {
        DuckOverrides();

        uint changed = _player.OldButtons ^ _buttons;
        uint pressed = changed & _buttons;
        uint released = changed & _player.OldButtons;

        _player.OldButtons = (_buttons & InDuck) != 0 ? _player.OldButtons | InDuck : _player.OldButtons & ~InDuck;

        if (_player.IsDead)
        {
            return;
        }

        HandleDuckingSpeedCrop();

        if ((_buttons & InDuck) != 0 || _player.Ducking || _player.FlDucking)
        {
            if ((_buttons & InDuck) != 0)
            {
                OnDuck(pressed);
            }
            else
            {
                OnUnDuck(released);
            }
        }
    }

    private void DuckOverrides()
    {
        bool onGround = _player.OnGround;

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

        // m_bAllowAutoMovement is true for a TF player, so this always runs.
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
        StartGravity();

        if ((_buttons & InJump) != 0)
        {
            CheckJumpButton();
        }
        else
        {
            _player.OldButtons &= ~InJump;
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
        FinishGravity();

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
        bool scout = _player.PlayerClass == ClassScout;
        bool onGround = _player.OnGround;

        if (_player.IsDead ||
            (_player.FlDucking && !(scout && !onGround)) ||
            (_player.Ducking && _player.FlDucking) || _player.DuckJumpTime > 0f ||
            (_player.OldButtons & InJump) != 0)
        {
            return;
        }

        if (!onGround)
        {
            // CTFPlayer::CanAirDash: a scout's one dash; attributes that add more are not read.
            if (scout && _player.AirDash < 1)
            {
                AirDash();
                _player.AirDucked = 0;
                return;
            }

            _player.OldButtons |= InJump;
            return;
        }

        PreventBunnyJumping();
        SetGroundEntity(null);

        if (_player.Ducking || _player.FlDucking)
        {
            _player.Velocity = _player.Velocity with { Z = JumpImpulse };
        }
        else
        {
            _player.Velocity = _player.Velocity with { Z = _player.Velocity.Z + JumpImpulse };
        }

        FinishGravity();

        _player.OldButtons |= InJump;
    }

    private void AirDash()
    {
        (Vector3 forward, Vector3 right) = FlatAxes();

        Vector3 wish = new(
            (forward.X * _forwardMove) + (right.X * _sideMove),
            (forward.Y * _forwardMove) + (right.Y * _sideMove),
            0f);

        _player.Velocity = wish with { Z = AirDashZ };
        _player.AirDash++;
    }

    private void PreventBunnyJumping()
    {
        float maxScaledSpeed = BunnyJumpMaxSpeedFactor * _player.MaxSpeed;

        if (maxScaledSpeed <= 0f)
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
    private bool CanAccelerate() => _player.PlayerState is null or 0;

    private float GetAirSpeedCap()
    {
        if (_player.Conditions.Has(CondShieldCharge))
        {
            return MaxChargeSpeed;
        }

        float cap = 30f;

        if (_player.Conditions.Has(CondRocketPack))
        {
            cap *= 0.5f;
        }

        return cap;
    }

    private void AirMove()
    {
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

        _player.Velocity += _player.BaseVelocity;
        TryPlayerMove(null, null, wallSlideCoeff);
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

    private void TryPlayerMove(Vector3? firstDest, BspTrace? firstTrace, float slideMultiplier)
    {
        const int NumBumps = 4;

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
                return;
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

            // The floor and wall bits it returns feed only CheckKartWallBumping, and karts are declined.
            Vector3 normal = Normal(pm);

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

    private float Gravity => _convars.Gravity;

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
