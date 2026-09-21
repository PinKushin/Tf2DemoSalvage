using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One tracer a shot draws: which effect, from where, to where.</summary>
/// <param name="Shot">The shot's index in the timeline's feed.</param>
/// <param name="Bullet">Which of its bullets.</param>
/// <param name="Tick">The tick it fired on.</param>
/// <param name="Shooter">The shooter's entity index.</param>
/// <param name="Weapon">Their active weapon's entity index when the shot arrived, whose `muzzle` the drawn tracer starts from.</param>
/// <param name="Effect">The particle system, <c>GetTracerType()</c> plus <c>_crit</c>.</param>
/// <param name="Start">Where the bullet started, `m_vecOrigin` — the fallback when there is no muzzle.</param>
/// <param name="End">Where the WORLD stopped the bullet; players are clipped against it later, by the renderer.</param>
/// <param name="Reach">`vecEnd`, the origin plus the whole range, which the player pass extends.</param>
public readonly record struct ShotTracer(
    int Shot,
    int Bullet,
    int Tick,
    int Shooter,
    int Weapon,
    string Effect,
    (float X, float Y, float Z) Start,
    (float X, float Y, float Z) End,
    (float X, float Y, float Z) Reach);

/// <summary>One bullet the world stopped — `UTIL_ImpactTrace`'s trace, before the players are counted (B415).</summary>
/// <param name="Shot">The shot's index in the timeline's feed.</param>
/// <param name="Bullet">Which of its bullets.</param>
/// <param name="Tick">The tick it fired on.</param>
/// <param name="Shooter">The shooter's entity index.</param>
/// <param name="Team">The shooter's team: `FireBullet` decals nothing on the shooter's own team.</param>
/// <param name="Start">`trace.startpos`, the bullet's origin.</param>
/// <param name="End">`trace.endpos` against the world alone.</param>
/// <param name="Reach">The origin plus the whole range, for the player pass.</param>
/// <param name="Texinfo">The struck brush side's texinfo — `trace.surface` — or −1 for terrain, whose surface is not traced.</param>
/// <param name="Normal">`trace.plane.normal`; zero for terrain.</param>
/// <param name="SurfaceProp">
/// A server `Impact` dispatch's `m_nSurfaceProp`, which names the game material directly; −1 for a client bullet, whose
/// material is its struck texinfo's.
/// </param>
/// <param name="DamageType">The dispatch's `m_nDamageType`, which `DamageDecal` reads; 0 for a client bullet.</param>
/// <remarks>**A negative <see cref="Shot"/> is a server dispatch**, `−1 − index` into the dispatch feed: no player judgement is asked of it.</remarks>
public readonly record struct ShotImpact(
    int Shot,
    int Bullet,
    int Tick,
    int Shooter,
    int Team,
    (float X, float Y, float Z) Start,
    (float X, float Y, float Z) End,
    (float X, float Y, float Z) Reach,
    int Texinfo,
    (float X, float Y, float Z) Normal = default,
    int SurfaceProp = -1,
    int DamageType = 0)
{
    /// <summary>Whether the server sent this impact rather than the client tracing it.</summary>
    public bool FromServer => Shot < 0;
}

/// <summary>Which bullets of a demo's shots draw a tracer, and where each ends (B415).</summary>
/// <remarks>
/// **The client rebuilds every bullet and traces it itself**, `FX_FireBullets` into `CTFPlayer::FireBullet`
/// (`tf_fx_shared.cpp:158`, `tf_player_shared.cpp:10276`), because `CTEFireBullets` carries a seed and no paths —
/// `docs/findings/57-the-shot-is-a-seed.md`. Per bullet, in the engine's order:
///
/// <code>
/// trace from m_vecOrigin along the rebuilt direction for the mode's m_flRange
/// if ( trace.fraction &lt; 1.0 ) and ( tracerCount++ % m_iTracerFreq ) == 0:
///     UTIL_ParticleTracer( GetTracerType() [+ "_crit"], muzzle, trace.endpos, … )
/// </code>
///
/// **`tracerCount` is one static for the whole client**, so which bullet of a pair draws depends on every bullet
/// before it in the demo, from every player. That is why this answers for ALL shots at once, in fire order, rather
/// than for one. It starts at zero here, as a freshly started client's does; a client that played something first
/// would be at another phase, and nothing in a demo says which.
///
/// **What this does NOT decide:** where the tracer is DRAWN from. `FireBullet` moves the start to the active weapon's
/// `muzzle` attachment, which needs the posed world model and lives with the renderer; <see cref="ShotTracer.Start"/>
/// is the bullet's own origin, which is what the engine keeps when there is no attachment.
///
/// **Players are the renderer's half.** `UTIL_PlayerBulletTrace` also traces `CONTENTS_HITBOX`, which needs every
/// player posed at the moment of the shot, so <see cref="ShotTracer.End"/> is the world's answer and the viewer clips
/// it with <see cref="PlayerBulletTrace"/> when the tracer is first drawn. The counter is decided here, on the world
/// alone. A player changes it only for a bullet the world would have let run its whole range: that bullet hits the
/// player (fraction below 1) and counts, where the world alone says it missed. *Not measured* how often that happens.
/// </remarks>
public sealed class HitscanTracers
{
    /// <summary>`m_iTracerFreq` as `FX_FireBullets` sets it for everything but the minigun.</summary>
    private const int TracerFrequency = 2;

    /// <summary>`FireBulletsInfo_t`'s own default, which the minigun keeps.</summary>
    private const int DefaultTracerFrequency = 4;

    /// <summary>`TF_WEAPON_MINIGUN`.</summary>
    private const int MinigunId = 18;

    /// <summary>`TF_TEAM_RED`.</summary>
    private const int RedTeam = 2;

    /// <summary>`CTFWeaponInfo`'s default range, `GetFloat( "Range", 8192.0f )`.</summary>
    private const float DefaultRange = 8192f;

    /// <summary>`TF_WEAPON_SECONDARY_MODE`.</summary>
    private const int SecondaryMode = 1;

    /// <summary>
    /// Every `TF_WEAPON_*` whose `g_aWeaponDamageTypes` entry carries `DMG_BUCKSHOT` (`tf_shareddefs.cpp:715`): the
    /// four shotguns, the scattergun, the Frontier Justice, the Shortstop, the Soda Popper, the Baby Face's Blaster and
    /// the Rescue Ranger. No `GetDamageType` override adds it.
    /// </summary>
    private static readonly HashSet<int> Buckshot = [12, 13, 14, 15, 16, 69, 71, 76, 85, 90];

    private readonly Func<string, byte[]?> _readFile;

    private readonly Dictionary<string, WeaponScript?> _scripts = new(StringComparer.Ordinal);

    /// <summary>Reads tracers out of the game's own weapon scripts.</summary>
    /// <param name="readFile">Opens a path out of the game's content, or answers null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="readFile"/> is null.</exception>
    public HitscanTracers(Func<string, byte[]?> readFile)
    {
        ArgumentNullException.ThrowIfNull(readFile);

        _readFile = readFile;
    }

    /// <summary>Every tracer a demo's shots draw, in fire order.</summary>
    /// <param name="shots">Every shot, in fire order — the counter needs all of them.</param>
    /// <param name="sweep">The world trace along a segment: how far a bullet gets, and what it struck.</param>
    /// <param name="fixedSpread">`IsFixedWeaponSpreadEnabled`: the server's `tf_use_fixed_weaponspreads`.</param>
    /// <param name="impacts">When given, every bullet the world stopped, in fire order.</param>
    /// <returns>The tracers.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<ShotTracer> Trace(
        IReadOnlyList<SceneShot> shots,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), BspTrace> sweep,
        bool fixedSpread,
        ICollection<ShotImpact>? impacts = null)
    {
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(sweep);

        List<ShotTracer> tracers = [];
        int count = 0;

        for (int index = 0; index < shots.Count; index++)
        {
            SceneShot shot = shots[index];

            // `if ( !pPlayer ) return;`, and a weapon id with no script returns too.
            if (shot.By is not { } by ||
                TfWeaponAliases.Of(shot.WeaponId) is not { } alias ||
                Script(alias) is not { } script)
            {
                continue;
            }

            bool secondary = shot.Mode == SecondaryMode;
            int bullets = Whole(script, secondary, "BulletsPerShot", 0);
            float range = Real(script, secondary, "Range", DefaultRange);

            // `pPlayer->GetActiveTFWeapon()` decides the damage type and whether the crit flag is read at all.
            // *Interpolated:* its weapon id is taken to be the shot's; a player fires the weapon they hold.
            bool holding = by.Weapon is not null;
            int weapon = by.Weapon ?? 0;
            bool buckshot = holding && Buckshot.Contains(shot.WeaponId);
            bool critical = holding && shot.Critical;

            (float X, float Y, float Z)[] directions = FireBulletsSpread.Directions(
                shot.Pitch, shot.Yaw, shot.Spread, shot.Seed, bullets, buckshot && bullets > 1 && fixedSpread);

            int frequency = shot.WeaponId == MinigunId ? DefaultTracerFrequency : TracerFrequency;
            string? effect = holding ? TracerType(script, shot.WeaponId, by.Team, critical) : null;

            for (int bullet = 0; bullet < directions.Length; bullet++)
            {
                (float X, float Y, float Z) direction = directions[bullet];
                (float X, float Y, float Z) end = (
                    shot.Origin.X + (direction.X * range),
                    shot.Origin.Y + (direction.Y * range),
                    shot.Origin.Z + (direction.Z * range));

                BspTrace hit = sweep(shot.Origin, end);
                float fraction = hit.Fraction;

                if (fraction >= 1f)
                {
                    continue;
                }

                (float X, float Y, float Z) stopped = (
                    shot.Origin.X + ((end.X - shot.Origin.X) * fraction),
                    shot.Origin.Y + ((end.Y - shot.Origin.Y) * fraction),
                    shot.Origin.Z + ((end.Z - shot.Origin.Z) * fraction));

                impacts?.Add(new ShotImpact(
                    index, bullet, shot.Tick, shot.Shooter, by.Team, shot.Origin, stopped, end, hit.Texinfo, hit.Normal));

                if ((count++ % frequency) != 0 || effect is null)
                {
                    continue;
                }

                tracers.Add(new ShotTracer(index, bullet, shot.Tick, shot.Shooter, weapon, effect, shot.Origin, stopped, end));
            }
        }

        return tracers;
    }

    /// <summary>`CTFWeaponBase::GetTracerType`, plus `FireBullet`'s `_crit`.</summary>
    /// <remarks>
    /// <code>
    /// if ( m_szTracerEffect[0] ) return "%s_%s", effect, owner team == TF_TEAM_RED ? "red" : "blue";
    /// if ( GetWeaponID() == TF_WEAPON_MINIGUN ) return "BrightTracer";
    /// return BaseClass::GetTracerType();                  // CBaseEntity's: NULL
    /// </code>
    ///
    /// **Not established:** the item's own `tracer_effect` override (`GetStaticData()->GetTracerEffect( team )`),
    /// which replaces the script's effect for items such as the Machina. Filed in B415.
    /// </remarks>
    private static string? TracerType(WeaponScript script, int weaponId, int team, bool critical)
    {
        string? name = null;

        if (script.Value("TracerEffect") is { Length: > 0 } effect)
        {
            name = effect + (team == RedTeam ? "_red" : "_blue");
        }
        else if (weaponId == MinigunId)
        {
            name = "BrightTracer";
        }

        return name is not null && critical ? name + "_crit" : name;
    }

    /// <summary>A mode's integer: <c>Secondary_</c> falls back to the primary's value, as the parser does.</summary>
    private static int Whole(WeaponScript script, bool secondary, string key, int otherwise)
    {
        int primary = int.TryParse(script.Value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int read)
            ? read
            : otherwise;

        return secondary &&
               int.TryParse(script.Value("Secondary_" + key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int second)
            ? second
            : primary;
    }

    /// <summary>A mode's real number, with the same fallback.</summary>
    private static float Real(WeaponScript script, bool secondary, string key, float otherwise)
    {
        float primary = float.TryParse(script.Value(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
            ? read
            : otherwise;

        return secondary &&
               float.TryParse(script.Value("Secondary_" + key), NumberStyles.Float, CultureInfo.InvariantCulture, out float second)
            ? second
            : primary;
    }

    /// <summary>One weapon script, read once whether or not it exists.</summary>
    private WeaponScript? Script(string alias)
    {
        // Stryker disable once : a mutated condition leaves 'already' unassigned in the body, CS0165 — B410.
        if (_scripts.TryGetValue(alias, out WeaponScript? already))
        {
            return already;
        }

        WeaponScript? read = WeaponScript.Read(_readFile, alias);

        _scripts[alias] = read;

        return read;
    }
}
