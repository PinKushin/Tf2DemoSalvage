using System.Collections.Generic;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The vision filter reaches the drawn list, not just its own unit tests (B354).
/// </summary>
/// <remarks>
/// **A rule nobody applies is a rule that does nothing**, and this project has shipped three of
/// those with a green suite — a kill annotation matching the wrong type, a numeric lookup fed
/// strings, and a decoded `m_flPlaybackRate` no production code ever read. Each was found by looking
/// at the output rather than by the tests that covered the code.
///
/// So `VisionVisibilityConformanceTests` says the rule is right and this says `MomentScene` runs it:
/// a real scene, a real schema, and an assertion on `Drawn`.
///
/// **The schema is synthetic and supplied through the read function `WeaponModels` already takes**,
/// which is what makes this a unit test rather than a corpus one — no install, no 8 MB parse, and
/// the item numbers are the shipped ones so the fixture reads as what it stands for.
/// </remarks>
public sealed class VisionVisibilityWiringTests
{
    [Test]
    public void Build_APyrolandItemWithNoRecorder_IsNotDrawn()
    {
        // A SourceTV recording: the viewer is a spectator, carries nothing, and sees no Pyroland —
        // which is what `tf_spectate_pyrovision` at its default of 0 gives a live spectator.
        MomentScene scene = Scene();

        scene.Build([], [Worn(Balloonicorn)], Info() with { Recorder = null });

        scene.Drawn.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control, and it is the branch that keeps the rule from being "drop econ items".**
    /// The Rainblower grants Pyrovision, so a recorder holding one sees the Balloonicorn — which is
    /// also how the wearer of a Pyroland item sees their own.
    /// </remarks>
    [Test]
    public void Build_APyrolandItemWhenTheRecorderHasPyrovision_IsDrawn()
    {
        MomentScene scene = Scene();

        scene.Build(
            [],
            [Worn(Balloonicorn, entity: 20, wearer: 4), Worn(Rainblower, entity: 21, wearer: 3)],
            Info() with { Recorder = 3 });

        scene.Drawn.Count.ShouldBe(2);
    }

    /// <remarks>
    /// **The bystander, and the one that would catch a filter applied to everything.** An ordinary
    /// hat declares no vision at all — as all but 23 shipped items do — so it survives a viewer with
    /// no vision whatsoever.
    /// </remarks>
    [Test]
    public void Build_AnOrdinaryItemWithNoRecorder_IsStillDrawn()
    {
        MomentScene scene = Scene();

        scene.Build([], [Worn(PlainHat)], Info() with { Recorder = null });

        scene.Drawn.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **The grant is the RECORDER's, not anyone's.** Without this, "somebody in the scene has
    /// Pyrovision" passes the control above while showing Pyroland items to a recorder who cannot
    /// see them — which is the whole behaviour being reproduced.
    /// </remarks>
    [Test]
    public void Build_APyrolandItemWhenSOMEONEELSEHasPyrovision_IsNotDrawn()
    {
        MomentScene scene = Scene();

        scene.Build(
            [],
            [Worn(Balloonicorn, entity: 20, wearer: 4), Worn(Rainblower, entity: 21, wearer: 4)],
            Info() with { Recorder = 3 });

        // The Rainblower has no filter of its own, so it stays; the Balloonicorn goes.
        scene.Drawn.Count.ShouldBe(1);
        scene.Drawn[0].EntityIndex.ShouldBe(21);
    }

    private const int Balloonicorn = 738;
    private const int Rainblower = 741;
    private const int PlainHat = 100;

    private static MomentScene Scene()
    {
        EntityModelSet models = new();

        return new MomentScene(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = new WeaponModels(Read, NullLogger.Instance),
        };
    }

    private static byte[]? Read(string path) =>
        path == "scripts/items/items_game.txt" ? Encoding.UTF8.GetBytes(Schema) : null;

    private static MomentInfo Info() =>
        new(
            Tick: 1d,
            CurrentTick: 1,
            FirstPerson: false,
            Followed: null,
            EyeCamera: null,
            IntervalPerTick: 0.015f,
            ViewmodelFieldOfView: 54f);

    private static SceneProp Worn(int item, int entity = 20, int wearer = 3) =>
        new(
            EntityIndex: entity,
            ModelPath: "models/player/items/pyro/balloonicorn.mdl",
            Kind: SceneModelKind.Studio,
            Pose: new ScenePose(),
            AttachedTo: wearer,
            BoneMerged: true,
            ItemDefinitionIndex: item);

    /// <summary>The two shipped items this is about, with their real numbers and keys.</summary>
    private const string Schema = """
        "items_game"
        {
            "attributes"
            {
                "406"
                {
                    "name" "vision opt in flags"
                    "attribute_class" "vision_opt_in_flags"
                    "description_format" "value_is_or"
                    "stored_as_integer" "0"
                }
            }
            "items"
            {
                "738"
                {
                    "name" "Pet Balloonicorn"
                    "vision_filter_flags" "1"
                    "attributes"
                    {
                        "vision opt in flags"
                        {
                            "attribute_class" "vision_opt_in_flags"
                            "value" "1"
                        }
                    }
                }
                "741"
                {
                    "name" "The Rainblower"
                    "attributes"
                    {
                        "vision opt in flags"
                        {
                            "attribute_class" "vision_opt_in_flags"
                            "value" "1"
                        }
                    }
                }
                "100"
                {
                    "name" "an ordinary hat"
                }
            }
        }
        """;
}
