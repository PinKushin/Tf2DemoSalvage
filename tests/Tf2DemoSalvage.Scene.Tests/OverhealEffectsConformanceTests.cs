using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The pluses on an overhealed player — <c>C_TFPlayer::UpdateOverhealEffect</c> (B538).</summary>
/// <remarks>
/// <code>
/// // c_tf_player.cpp:10056-10115
/// bool bShow = m_Shared.InCond( TF_COND_HEALTH_OVERHEALED );
/// int iTeam = GetTeamNumber();
/// if ( IsLocalPlayer() || ( m_Shared.IsStealthed() &amp;&amp; !InSameTeam( GetLocalTFPlayer() ) ) )
///     bShow = false;
/// else if ( IsPlayerClass( TF_CLASS_SPY ) &amp;&amp; !InSameTeam( GetLocalTFPlayer() ) &amp;&amp; m_Shared.InCond( TF_COND_DISGUISED ) )
///     iTeam = m_Shared.GetDisguiseTeam();
/// if ( bShow ) … CreateOverhealEffect( iTeam );  else … ParticleProp()->StopEmission( m_pOverHealedEffect );
///
/// case TF_TEAM_BLUE: pEffect = "overhealedplayer_blue_pluses";  case TF_TEAM_RED: pEffect = "overhealedplayer_red_pluses";
/// m_pOverHealedEffect = ParticleProp()->Create( pEffect, PATTACH_ABSORIGIN_FOLLOW );
///
/// // tf_player_shared.cpp:7322-7335, :6905-6916 — called from OnAddOverhealed and OnRemoveOverhealed
/// </code>
/// `TF_COND_HEALTH_OVERHEALED` is 23, `TF_CLASS_SPY` 8, `TF_TEAM_RED` 2 and `TF_TEAM_BLUE` 3. Shipped data: the pluses
/// live two seconds, which is how long a stopped effect is still offered.
/// </remarks>
public sealed class OverhealEffectsConformanceTests
{
    private const int Red = 2;
    private const int Blue = 3;
    private const int Spy = 8;

    private static readonly PlayerConditions Overhealed = new(1 << OverhealEffects.Overhealed, 0, 0, 0, 0);

    private static ScenePlayer Player(int entity, PlayerConditions conditions, int team = Blue, int playerClass = 6) =>
        new(entity, 0f, 0f, 0f, team, 300, playerClass, Conditions: conditions);

    [TestCase(Blue, "overhealedplayer_blue_pluses")]
    [TestCase(Red, "overhealedplayer_red_pluses")]
    [TestCase(1, null)]
    [TestCase(null, null)]
    public void EffectName_ATeam_IsItsPlusesAndNoOtherTeamHasAny(int? team, string? expected) =>
        OverhealEffects.EffectName(team).ShouldBe(expected);

    [Test]
    public void ShownTeam_AnOverhealedPlayer_IsHisTeamAndOneWhoIsNotHasNone()
    {
        OverhealEffects.ShownTeam(Player(3, Overhealed), localEntity: 9).ShouldBe(Blue);
        OverhealEffects.ShownTeam(Player(3, default), localEntity: 9).ShouldBeNull();
    }

    [Test]
    public void ShownTeam_TheLocalPlayer_HasNone() =>
        OverhealEffects.ShownTeam(Player(3, Overhealed), localEntity: 3).ShouldBeNull();

    [Test]
    public void ShownTeam_AStealthedPlayer_HasNoneForAnEnemyAndHisOwnForAFriend()
    {
        PlayerConditions cloaked = Overhealed with { Cond = Overhealed.Cond | (1 << PlayerConditions.Stealthed) };

        OverhealEffects.ShownTeam(Player(3, cloaked) with { IsEnemy = true }, localEntity: 9).ShouldBeNull();
        OverhealEffects.ShownTeam(Player(3, cloaked), localEntity: 9).ShouldBe(Blue);
    }

    [Test]
    public void ShownTeam_ADisguisedSpy_IsHisDisguisesTeamToAnEnemyAlone()
    {
        PlayerConditions disguised = Overhealed with { Cond = Overhealed.Cond | (1 << PlayerConditions.Disguised) };
        ScenePlayer spy = Player(3, disguised, Blue, Spy) with { DisguiseTeam = Red };

        OverhealEffects.ShownTeam(spy with { IsEnemy = true }, localEntity: 9).ShouldBe(Red);
        OverhealEffects.ShownTeam(spy, localEntity: 9).ShouldBe(Blue);
        OverhealEffects.ShownTeam(spy with { IsEnemy = true, PlayerClass = 6 }, localEntity: 9).ShouldBe(Blue, "only a spy's disguise is asked");
    }

    [Test]
    public void At_APlayerWhoBecomesOverhealed_RunsFromThatTickAndKeepsItsStart()
    {
        OverhealEffects effects = new();
        Func<int, IReadOnlyList<ScenePlayer>> players = tick => [Player(3, tick >= 100 ? Overhealed : default)];

        effects.At(99, players, null).ShouldBeEmpty();

        OverhealRun started = effects.At(100, players, null).ShouldHaveSingleItem();

        started.ShouldBe(new OverhealRun(3, Blue, 100, null));
        effects.At(150, players, null).ShouldHaveSingleItem().ShouldBe(started, "the start is the condition's, not the frame's");
    }

    [Test]
    public void At_ThePlayerStopsBeingOverhealed_StopsThenAndIsOfferedTwoSecondsMore()
    {
        OverhealEffects effects = new();
        Func<int, IReadOnlyList<ScenePlayer>> players = tick => [Player(3, tick is >= 100 and < 120 ? Overhealed : default)];

        effects.At(100, players, null);

        effects.At(120, players, null).ShouldHaveSingleItem().ShouldBe(new OverhealRun(3, Blue, 100, 120));
        effects.At(120 + OverhealEffects.FadeTicks - 1, players, null).ShouldHaveSingleItem();
        effects.At(120 + OverhealEffects.FadeTicks, players, null).ShouldBeEmpty();
    }

    [Test]
    public void At_ASeekIntoARun_StartsItAtMostTwoSecondsBackAndABackwardSeekStartsAgain()
    {
        OverhealEffects effects = new();
        Func<int, IReadOnlyList<ScenePlayer>> players = tick => [Player(3, tick >= 100 ? Overhealed : default)];

        // Far inside: the particles alive now were all born in the last two seconds, so the replay starts there.
        OverhealRun met = effects.At(1000, players, null).ShouldHaveSingleItem();

        met.From.ShouldBe(1000 - OverhealEffects.FadeTicks);
        effects.At(1001, players, null).ShouldHaveSingleItem().ShouldBe(met);

        // Back to just after it began: the start is the condition's own.
        effects.At(110, players, null).ShouldHaveSingleItem().From.ShouldBe(100);
    }

    [Test]
    public void At_ASeekPastAStop_StillOffersTheStoppedRun()
    {
        OverhealEffects effects = new();
        Func<int, IReadOnlyList<ScenePlayer>> players = tick => [Player(3, tick is >= 100 and < 120 ? Overhealed : default)];

        effects.At(140, players, null).ShouldHaveSingleItem().ShouldBe(new OverhealRun(3, Blue, 100, 120));
    }

    [Test]
    public void Bursts_ABurstWithAModel_BearsItsParticlesOnTheBoxesWhereThePointWasThen()
    {
        // The boxes are posed for the tick being shown; a catch-up step to an earlier tick takes them where the
        // point stood then, as the engine met the entity there.
        ParticleSystem system = new(
            "on_model",
            [
                new ParticleFunction(
                    "emit_instantaneously",
                    "emit_instantaneously",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, Number: 4d),
                    }),
            ],
            [new ParticleFunction(PositionOnModel.Named, PositionOnModel.Named, new Dictionary<string, DmxValue>(StringComparer.Ordinal))],
            [],
            [],
            [],
            new Dictionary<string, DmxValue>(StringComparer.Ordinal));

        Vector3 now = new(1000f, 0f, 0f);
        Vector3 then = new(400f, 0f, 0f);
        ModelHitbox box = ModelHitbox.At(Vector3.Zero, new Vector3(10f, 10f, 10f), now);

        ParticleBurst burst = new(
            Key: 5,
            system,
            ParticleControlPoint.Unoriented(now),
            Tick: 100,
            History: _ => (ParticleControlPoint.Unoriented(then), null))
        {
            Model = () => ([box], now),
        };

        ParticleEffects effects = new();

        effects.Bursts([burst], 1f / 66f, null, tick: 110);

        ParticleStore particles = effects.BurstEffect(5).ShouldNotBeNull().Particles;

        particles.Count.ShouldBe(4);
        Enumerable.Range(0, 4).Select(particles.PositionOf).ShouldAllBe(at => at.X >= 400f && at.X <= 410f);
    }
}
