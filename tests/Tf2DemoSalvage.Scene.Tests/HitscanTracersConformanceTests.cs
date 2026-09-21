using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which bullets of which shots draw a tracer, and where it ends — `CTFPlayer::FireBullet` (B415).</summary>
/// <remarks>
/// The client half of `FireBullet` (`tf_player_shared.cpp:10560-10620`) draws a tracer only when the bullet hit
/// something, `trace.fraction &lt; 1.0`, and then only when one client-wide counter says so:
///
/// <code>
/// static int tracerCount;
/// if ( ( info.m_iTracerFreq != 0 ) &amp;&amp; ( tracerCount++ % info.m_iTracerFreq ) == 0 ) … UTIL_ParticleTracer( … )
/// </code>
///
/// `m_iTracerFreq` is 2, and 4 for the minigun (`FireBulletsInfo_t`'s default, which `FX_FireBullets` leaves alone).
/// </remarks>
public sealed class HitscanTracersConformanceTests
{
    /// <summary>`TF_WEAPON_NONE`, standing in for any single-bullet weapon that is not buckshot.</summary>
    private const int Rifle = 0;

    /// <summary>`TF_WEAPON_SCATTERGUN`, buckshot.</summary>
    private const int Scattergun = 16;

    /// <summary>`TF_WEAPON_MINIGUN`.</summary>
    private const int Minigun = 18;

    private const int Red = 2;
    private const int Blue = 3;

    /// <summary>A world that stops every bullet halfway, on texinfo 3.</summary>
    private static readonly Func<(float X, float Y, float Z), (float X, float Y, float Z), (float Fraction, int Texinfo)> Halfway =
        static (_, _) => (0.5f, 3);

    /// <summary>A world with nothing in it.</summary>
    private static readonly Func<(float X, float Y, float Z), (float X, float Y, float Z), (float Fraction, int Texinfo)> Empty =
        static (_, _) => (1f, -1);

    /// <remarks>
    /// **Every bullet the world stops is an impact, tracer or not**: `UTIL_ImpactTrace` sits under
    /// `if ( trace.fraction &lt; 1.0 )` beside the counter, not inside it, and never asks for a tracer effect.
    /// </remarks>
    [Test]
    public void Trace_TwoBulletsThatHit_AreTwoImpactsOnTheStruckSurface()
    {
        List<ShotImpact> impacts = [];

        Tracers(Rifle, tracer: null, range: 1000f)
            .Trace([Shot(Rifle, tick: 1), Shot(Rifle, tick: 2, weapon: null)], Halfway, fixedSpread: false, impacts);

        impacts.Count.ShouldBe(2);
        (impacts[1] with { End = default, Reach = default })
            .ShouldBe(new ShotImpact(1, 0, 2, 5, Red, (0f, 0f, 0f), default, default, 3));
        impacts[1].End.X.ShouldBe(500f, 0.001f, "yaw 0 fires down +X; half of 1000");
        impacts[1].Reach.X.ShouldBe(1000f, 0.001f);
    }

    [Test]
    public void Trace_ABulletIntoNothing_IsNoImpact()
    {
        List<ShotImpact> impacts = [];

        Tracers(Rifle, "bullet_tracer01").Trace([Shot(Rifle, tick: 1)], Empty, fixedSpread: false, impacts);

        impacts.ShouldBeEmpty();
    }

    [Test]
    public void Trace_TwoBulletsThatHit_DrawATracerForTheFirstOnly()
    {
        IReadOnlyList<ShotTracer> tracers = Tracers(Rifle, "bullet_tracer01")
            .Trace([Shot(Rifle, tick: 1), Shot(Rifle, tick: 2)], Halfway, fixedSpread: false);

        tracers.Count.ShouldBe(1);
        tracers[0].Shot.ShouldBe(0);
    }

    /// <remarks>
    /// **A bullet that hits nothing does not advance the counter**: the `tracerCount++` is inside
    /// `if ( trace.fraction &lt; 1.0 )`. So a miss followed by a hit draws the hit's tracer.
    /// </remarks>
    [Test]
    public void Trace_AMissThenAHit_DrawTheHitsTracer()
    {
        int calls = 0;

        IReadOnlyList<ShotTracer> tracers = Tracers(Rifle, "bullet_tracer01").Trace(
            [Shot(Rifle, tick: 1), Shot(Rifle, tick: 2)],
            (_, _) => calls++ == 0 ? (1f, -1) : (0.5f, 3),
            fixedSpread: false);

        tracers.Count.ShouldBe(1);
        tracers[0].Shot.ShouldBe(1);
    }

    [Test]
    public void Trace_ABulletIntoNothing_DrawsNoTracer()
    {
        Tracers(Rifle, "bullet_tracer01").Trace([Shot(Rifle, tick: 1)], Empty, fixedSpread: false).Count.ShouldBe(0);
    }

    /// <remarks>The tracer ends at `trace.endpos`: the start plus the bullet's whole range, times the fraction.</remarks>
    [Test]
    public void Trace_ABulletThatHits_EndsWhereTheTraceStopped()
    {
        ShotTracer tracer = Tracers(Rifle, "bullet_tracer01", range: 1000f)
            .Trace([Shot(Rifle, tick: 1, origin: (10f, 20f, 30f))], Halfway, fixedSpread: false)[0];

        tracer.Start.ShouldBe((10f, 20f, 30f));
        tracer.End.X.ShouldBe(510f, 0.001f, "yaw 0 fires down +X; half of 1000");
        tracer.End.Y.ShouldBe(20f, 0.001f);
        tracer.End.Z.ShouldBe(30f, 0.001f);
    }

    /// <remarks>`CTFWeaponBase::GetTracerType`: `"%s_%s"` with `"red"` for `TF_TEAM_RED` and `"blue"` otherwise.</remarks>
    [TestCase(Red, false, "bullet_tracer01_red")]
    [TestCase(Blue, false, "bullet_tracer01_blue")]
    [TestCase(Red, true, "bullet_tracer01_red_crit")]
    public void Trace_ByTeamAndCrit_NamesTheTeamsEffect(int team, bool critical, string expected)
    {
        Tracers(Rifle, "bullet_tracer01")
            .Trace([Shot(Rifle, tick: 1, team: team, critical: critical)], Halfway, fixedSpread: false)[0]
            .Effect.ShouldBe(expected);
    }

    /// <remarks>
    /// **With no active weapon `CBasePlayer::GetTracerType` falls to `CBaseEntity`'s, which is NULL**, and the crit
    /// flag is only read inside `if ( pWeapon )` — so no tracer at all.
    /// </remarks>
    [Test]
    public void Trace_AShooterHoldingNothing_DrawsNoTracer()
    {
        Tracers(Rifle, "bullet_tracer01")
            .Trace([Shot(Rifle, tick: 1, weapon: null)], Halfway, fixedSpread: false)
            .Count.ShouldBe(0);
    }

    /// <remarks>`FX_FireBullets` returns at once when the player is not in the client's list.</remarks>
    [Test]
    public void Trace_AShotFromNobody_DrawsNothingAndCountsNothing()
    {
        SceneShot nobody = Shot(Rifle, tick: 1) with { By = null };

        IReadOnlyList<ShotTracer> tracers = Tracers(Rifle, "bullet_tracer01")
            .Trace([nobody, Shot(Rifle, tick: 2)], Halfway, fixedSpread: false);

        tracers.Count.ShouldBe(1);
        tracers[0].Shot.ShouldBe(1);
    }

    /// <remarks>A script with no `TracerEffect` returns `CBaseEntity::GetTracerType`'s NULL, except the minigun's.</remarks>
    [Test]
    public void Trace_AWeaponWithNoTracerEffect_DrawsNoTracer()
    {
        Tracers(Rifle, tracer: null).Trace([Shot(Rifle, tick: 1)], Halfway, fixedSpread: false).Count.ShouldBe(0);
    }

    [Test]
    public void Trace_AMinigunWithNoTracerEffect_IsBrightTracer()
    {
        Tracers(Minigun, tracer: null).Trace([Shot(Minigun, tick: 1)], Halfway, fixedSpread: false)[0]
            .Effect.ShouldBe("BrightTracer");
    }

    /// <remarks>The minigun keeps `FireBulletsInfo_t`'s default frequency of 4, sharing the one counter.</remarks>
    [Test]
    public void Trace_EightMinigunBullets_DrawTwoTracers()
    {
        // Eight rather than five, so every third (a frequency of 3) would draw three and be told apart.
        List<SceneShot> shots = [];

        for (int tick = 0; tick < 8; tick++)
        {
            shots.Add(Shot(Minigun, tick));
        }

        Tracers(Minigun, "bullet_tracer01").Trace(shots, Halfway, fixedSpread: false).Count.ShouldBe(2);
    }

    /// <remarks>
    /// **Every pellet is a bullet to the counter**, so a ten-pellet scattergun blast draws five tracers, and with the
    /// fixed pattern pellet 0 goes straight down the middle.
    /// </remarks>
    [Test]
    public void Trace_ATenPelletBlastWithFixedSpread_DrawsFiveTracersTheFirstDeadAhead()
    {
        IReadOnlyList<ShotTracer> tracers = Tracers(Scattergun, "bullet_tracer01", pellets: 10, range: 1000f)
            .Trace([Shot(Scattergun, tick: 1, spread: 0.1f)], Halfway, fixedSpread: true);

        tracers.Count.ShouldBe(5);
        tracers[0].End.Y.ShouldBe(0f, 0.001f);
        tracers[0].End.Z.ShouldBe(0f, 0.001f);
    }

    /// <summary>A tracer builder over one synthetic weapon script.</summary>
    private static HitscanTracers Tracers(int weaponId, string? tracer, int pellets = 1, float range = 8192f)
    {
        StringBuilder script = new("WeaponData\n{\n");

        script.Append("\"BulletsPerShot\"\t\"").Append(pellets).Append("\"\n");
        script.Append("\"Range\"\t\"").Append(range.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\"\n");

        if (tracer is not null)
        {
            script.Append("\"TracerEffect\"\t\"").Append(tracer).Append("\"\n");
        }

        script.Append("}\n");

        byte[] bytes = Encoding.UTF8.GetBytes(script.ToString());
        string alias = TfWeaponAliases.Of(weaponId)!;

        return new HitscanTracers(path =>
            string.Equals(path, "scripts/" + alias + ".txt", StringComparison.Ordinal) ? bytes : null);
    }

    private static SceneShot Shot(
        int weaponId,
        int tick,
        (float X, float Y, float Z) origin = default,
        int team = Red,
        bool critical = false,
        int? weapon = 40,
        float spread = 0f) =>
        new(tick, 5, origin, 0f, 0f, weaponId, 0, tick, spread, critical, new ShotShooter(team, weapon, null));
}
