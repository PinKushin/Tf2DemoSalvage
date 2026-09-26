using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>What the HUD reads of the local player: <see cref="HudStates.From"/>.</summary>
/// <remarks>`GetMaxBuffedHealth` (tf_player_shared.cpp:2235): the buffing base × 1.5, floored to a multiple of 5.</remarks>
public sealed class HudStatesTests
{
    [TestCase(175, 260)] // 262.5 → 260
    [TestCase(125, 185)] // 187.5 → 185
    [TestCase(300, 450)]
    public void From_TheBuffingBase_IsBoostedAndFlooredToAFive(int buffing, int expected) =>
        HudStates.From(Player() with { MaxHealthForBuffing = buffing }, 0).MaxBuffedHealth.ShouldBe(expected);

    [Test]
    public void From_TheHealth_IsTheEntitysNotTheResources()
    {
        HudState state = HudStates.From(Player() with { EntityHealth = 88, MaxHealth = 175, HideHud = 8 }, 66);

        (state.Health, state.MaxHealth, state.HideHud, state.Alive).ShouldBe((88, 175, 8, true));
        state.CurTime.ShouldBe((float)(66 * ScenePropTrack.Tf2TickInterval));
    }

    [Test]
    public void From_ADeadLifeState_IsNotAlive() =>
        HudStates.From(Player() with { LifeState = 2 }, 0).Alive.ShouldBeFalse();

    private static ScenePlayer Player() => new(1, 0f, 0f, 0f, 2, Health: 99, PlayerClass: 3);
}
