using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The seek scan's sampler is wired from the moment source to the model set (D233) — the hop that, left unset, keys
/// every roll on the landing tick with every unit test green (the B193 shape).
/// </summary>
public sealed class MomentPresenterEarlierPlayerTests
{
    [Test]
    public void Source_Set_HandsTheModelsThePlayerAsPlayerPropsBuildsHimAtThatTick()
    {
        EntityModelSet models = new();
        MomentScene scene = new(models, new ViewmodelScene(), NullLogger.Instance) { Appearance = new Soldiers() };
        MomentPresenter presenter = new(scene, new FrameLedger(), NullLogger.Instance);

        models.PlayerPropAt.ShouldNotBeNull()(12d, 3).ShouldBeNull("no source yet");

        presenter.Source = new Source();
        SceneProp prop = models.PlayerPropAt.ShouldNotBeNull()(12d, 3).ShouldNotBeNull();

        prop.ModelPath.ShouldBe("models/player/soldier.mdl");
        prop.Pose.Speed.ShouldBe(12f, "the source's answer for that tick, carried through");
        models.PlayerPropAt(12d, 4).ShouldBeNull("a player the source does not list");

        presenter.Source = null;
        models.PlayerPropAt(12d, 3).ShouldBeNull("cleared with the source");
    }

    /// <summary>Answers a soldier whose speed is the tick asked — only through the side-effect-free query.</summary>
    private sealed class Source : IMomentSource
    {
        public float IntervalPerTick => 0.015f;

        public int? Recorder => null;

        public void PlayersAt(double tick, ICollection<ScenePlayer> into, bool interpolating = true) => into.Clear();

        public ScenePlayer? ActivityPlayerAt(double tick, int entityIndex) =>
            entityIndex == 3
                ? new ScenePlayer(EntityIndex: 3, X: 0f, Y: 0f, Z: 0f, Team: SceneTeams.Red, Health: 200, PlayerClass: 3)
                {
                    Speed = (float)tick,
                }
                : null;

        public void PropsAt(double tick, ICollection<SceneProp> into, int? viewEntity = null, bool interpolating = true) =>
            into.Clear();

        public int? RoundStateAt(double tick) => null;

        public void OnNewModel(int entityIndex, IReadOnlyList<bool> looping, bool staticProp)
        {
        }
    }

    private sealed class Soldiers : IPlayerAppearance
    {
        public string? ModelOf(int playerClass) => "models/player/soldier.mdl";

        public string? WeaponSuffix(string? weaponClass, int? playerClass, int? weaponItem) => null;

        public string? Hands(int playerClass) => null;

        public ItemBodygroups BodygroupsOf(int itemDefinitionIndex, int team) => ItemBodygroups.None;

        public SceneTaunt? TauntForScene(string scene) => null;
    }
}
