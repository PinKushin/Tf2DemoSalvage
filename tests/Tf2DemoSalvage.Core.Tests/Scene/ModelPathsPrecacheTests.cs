using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="DemoTimeline.ModelPaths"/>, the model precache list (B335).
/// </summary>
/// <remarks>
/// **Twenty-four branches at a flat zero**, and it is the list that decides what gets loaded at
/// level load rather than mid-playback. D86/D87 are about exactly that: Valve does not merely
/// prefer to precache early, `CBaseEntity::PrecacheSound` refuses to do it later — and a model this
/// list misses is one decoded on the frame it first appears, which the owner reports as a hitch
/// every few seconds rather than as a missing model.
///
/// **So a gap here has no visual symptom at all.** Nothing found it; the coverage floor did.
///
/// **Built through `ForTracks` and the viewmodel seam**, which is how `ForSounds` — the SOUND
/// precache list's test — is already written. Authoring a whole demo would exercise the entity
/// decoder instead, and this is a test about three loops and a set.
/// </remarks>
public sealed class ModelPathsPrecacheTests
{
    private const string Barrel = "models/props_gameplay/barrel01.mdl";
    private const string Crate = "models/props_gameplay/crate01.mdl";
    private const string Scattergun = "models/weapons/v_models/v_scattergun_scout.mdl";

    /// <remarks>
    /// **All three sources, which is the assertion the loops exist for.** A version walking only
    /// `_props` passes every other test here — props are the obvious source and the other two were
    /// added later, the viewmodels because *"a weapon switch changes this model, which is why
    /// leaving them out would leave a hitch every few seconds in exactly the view the owner
    /// reported it from"*.
    /// </remarks>
    [Test]
    public void ModelPaths_PropsPlayersAndViewmodels_AreAllIncluded()
    {
        DemoTimeline timeline = DemoTimeline.ForEverything(
            props: [new ScenePropTrack(1, Barrel)],
            players: [new ScenePropTrack(2, Crate)],
            viewmodels: [(3, Held(Scattergun))]);

        string[] paths = [.. timeline.ModelPaths()];

        paths.ShouldBe([Barrel, Crate, Scattergun], ignoreOrder: true);
    }

    /// <remarks>
    /// **The dedup, which is the whole reason for the `HashSet`.** A map puts hundreds of instances
    /// of one prop on it; returning the path once per instance hands the loader the same file
    /// hundreds of times.
    /// </remarks>
    [Test]
    public void ModelPaths_TwoPropsSharingAModel_ReturnsItOnce()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks(
            [new ScenePropTrack(1, Barrel), new ScenePropTrack(2, Barrel)]);

        string[] paths = [.. timeline.ModelPaths()];

        paths.ShouldBe([Barrel], "one model, however many props stand on it");
    }

    /// <remarks>
    /// **The dedup reaches ACROSS the three sources**, which a per-loop set would not: a player
    /// wearing the same model as a prop, or a viewmodel matching either, must still yield one path.
    /// The set is declared once outside all three loops and this is what says so.
    /// </remarks>
    [Test]
    public void ModelPaths_TheSameModelInEachSource_ReturnsItOnce()
    {
        DemoTimeline timeline = DemoTimeline.ForEverything(
            props: [new ScenePropTrack(1, Barrel)],
            players: [new ScenePropTrack(2, Barrel)],
            viewmodels: [(3, Held(Barrel))]);

        string[] paths = [.. timeline.ModelPaths()];

        paths.ShouldBe([Barrel], "one set across all three loops, not one per loop");
    }

    /// <remarks>
    /// **Case-insensitively**, and that is not fussiness: Valve's own content mixes `Models/` and
    /// `models/` in the same map — `bottle001.vmt` names its base texture
    /// `Models/props_gameplay/bottle001` with a capital M — so an ordinal set would precache the
    /// same file twice under two spellings.
    /// </remarks>
    [Test]
    public void ModelPaths_TheSameModelInTwoCases_ReturnsItOnce()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks(
            [new ScenePropTrack(1, Barrel), new ScenePropTrack(2, Barrel.ToUpperInvariant())]);

        string[] paths = [.. timeline.ModelPaths()];

        paths.Length.ShouldBe(1, "OrdinalIgnoreCase, because Valve's own content mixes the two");
    }

    /// <remarks>
    /// **An empty path is not a model**, and the guard is `is { Length: > 0 }` rather than a null
    /// check — a precache table's entry 0 is conventionally the empty string, so an entity that
    /// never received a model index resolves to it. Handing "" to the loader is a file-not-found on
    /// every map, once per such entity.
    ///
    /// Asserted in all three sources, because the guard is written out three times and a missing
    /// one is invisible from the other two.
    /// </remarks>
    [Test]
    public void ModelPaths_AnEntityWhoseModelResolvesToNothing_IsSkipped()
    {
        DemoTimeline timeline = DemoTimeline.ForEverything(
            props: [new ScenePropTrack(1, string.Empty), new ScenePropTrack(2, Barrel)],
            players: [new ScenePropTrack(3, string.Empty)],
            viewmodels: [(4, Held(string.Empty))]);

        string[] paths = [.. timeline.ModelPaths()];

        paths.ShouldBe([Barrel], "the entry-0 empty string is not a path to load");
    }

    /// <remarks>
    /// **A timeline with nothing in it yields nothing rather than throwing.** The enumerator is
    /// lazy, so a fault in it surfaces when something enumerates — a different frame from the one
    /// that built the timeline, which is what makes a lazy fault hard to place.
    /// </remarks>
    [Test]
    public void ModelPaths_ATimelineWithNoEntitiesAtAll_IsEmpty()
    {
        DemoTimeline.ForTracks([]).ModelPaths().ShouldBeEmpty();
    }

    /// <summary>A weapon in the recorder's hands, carrying nothing but its model.</summary>
    /// <remarks>
    /// The rest of a <see cref="SceneViewmodel"/> — sequence, rate, owner, slot — decides how it is
    /// ANIMATED, and none of it reaches the precache list. Left at its defaults so a reader of this
    /// file is not invited to think it matters here.
    /// </remarks>
    private static SceneViewmodel Held(string model) =>
        new(model, Sequence: 0, PlaybackRate: 1f, OwnerEntityIndex: null, Slot: null);
}
