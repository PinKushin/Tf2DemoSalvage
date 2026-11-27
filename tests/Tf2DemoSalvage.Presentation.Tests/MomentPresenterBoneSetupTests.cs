using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Tests;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>What a view reports it set bones up for is what its poses built (B878), read through a real scene.</summary>
public sealed class MomentPresenterBoneSetupTests
{
    private static readonly SceneProp Locker =
        new(7, "models/props_gameplay/resupply_locker.mdl", SceneModelKind.Studio, new ScenePose { X = 100f }, null);

    /// <summary>
    /// **A players-only pose between a real one and its report does not take the real one's builds away** (B878 review 3,
    /// Sonnet, the cavecrew reviewer): every pose starts a world pass whose builds alone the scene names. Shown and posed at
    /// 10 with the locker, then the decal replay's players-only show and pose at 9: the report at the next frame names the
    /// locker, for 10. The control: the first frame reports nothing set up.
    /// </summary>
    [Test]
    public void Show_AfterAPlayersOnlyPose_StillReportsTheRealPosesBuilds()
    {
        EntityModelSet models = new();
        models.Add([Locker], path => ModelFramesFixture.OneSecondLoop(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) });
        MomentPresenter presenter = new(new MomentScene(models, new ViewmodelScene(), NullLogger.Instance), new FrameLedger(), NullLogger.Instance);
        Source source = new();
        presenter.Source = source;

        presenter.NextFrame(null);
        presenter.Show(10d, View());
        presenter.PoseNow();

        presenter.PlayersOnly = true;
        presenter.Show(9d, View());
        presenter.PoseNow();
        presenter.PlayersOnly = false;

        presenter.NextFrame(null);
        presenter.Show(11d, View());

        source.Reports.ConvertAll(report => (report.Tick, string.Join(',', report.Entities)))
            .ShouldBe([(10d, string.Empty), (10d, "7")]);
    }

    private static MomentView View() =>
        new(CurrentTick: 0, FirstPerson: false, Followed: null, Eye: null, ViewmodelFieldOfView: 54f);

    /// <summary>Answers the locker at every tick and records each bone-setup report's tick and entities.</summary>
    private sealed class Source : IMomentSource
    {
        public List<(double Tick, HashSet<int> Entities)> Reports { get; } = [];

        public float IntervalPerTick => 0.015f;

        public int? Recorder => null;

        public void PlayersAt(double tick, ICollection<ScenePlayer> into, bool interpolating = true) => into.Clear();

        public void PropsAt(double tick, ICollection<SceneProp> into, int? viewEntity = null, bool interpolating = true)
        {
            into.Clear();
            into.Add(Locker);
        }

        public void BonesSetUp(IReadOnlySet<int> entities, double tick) => Reports.Add((tick, [.. entities]));

        public int? RoundStateAt(double tick) => null;

        public void OnNewModel(
            int entityIndex, IReadOnlyList<bool> looping, bool staticProp, System.Func<int, bool>? sequenceLoops = null)
        {
        }
    }
}
