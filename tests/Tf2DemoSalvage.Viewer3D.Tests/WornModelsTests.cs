using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Which models may never be baked, because something else places them.
/// </summary>
/// <remarks>
/// **This suite exists because its absence shipped B119.** A bone-merged model that gets baked keeps
/// its own matrices while the caller applies the WEARER's transform, so it draws at the wearer's
/// origin — the player's feet for a cosmetic, and the CAMERA for a viewmodel, where a fourteen-unit
/// knife against a near plane of one vanishes completely.
///
/// Every step of that is silent. The model packs, uploads, instances and draws; `Merge` returns at
/// its first guard without logging; the poser reports `asked for 3, produced 3`. It was found by
/// looking at the screen and by nothing else.
///
/// **The rule had no seam.** It lived inside a private method on a <c>Form</c>, so asserting it meant
/// opening a window, so it was never asserted. Extracting <see cref="WornModels"/> was the actual
/// work of fixing it; these tests are the point of doing that.
/// </remarks>
public sealed class WornModelsTests
{
    /// <summary>A model the viewer builds itself and hangs off the viewmodel.</summary>
    private const string HeldWeapon = "models/weapons/c_models/c_knife/c_knife.mdl";

    [Test]
    public void From_AWeaponHeldInFirstPerson_IsWorn()
    {
        // **The regression, and the whole reason this file exists.** The first-person weapon is
        // created by the CLIENT — `econ_entity.cpp:1153`, `InitializeAsClientEntity` — so no demo
        // carries it and it appears in no prop track. A rule built by walking the timeline alone is
        // therefore right about every model the demo describes and blind to this one, which is
        // exactly the shape B119 took: correct-looking code, silent failure, empty hand.
        HashSet<string> worn = WornModels.From([], [HeldWeapon]);

        worn.ShouldContain(HeldWeapon);
    }

    [Test]
    public void From_AnAttachedStudioTrack_IsWorn()
    {
        // A cosmetic: in the demo, and bone-merged onto its wearer.
        ScenePropTrack hat = Track("models/player/items/spy/spy_hat.mdl", attachedTo: 4);

        WornModels.From([hat], []).ShouldContain(hat.ModelPath);
    }

    [Test]
    public void From_AnUnattachedTrack_IsNotWorn()
    {
        // **The control, and without it every assertion here passes against "return everything".**
        // A model standing on its own origin is placed by its own transform, so baking it is correct
        // and cheap — a health pack that never moves should not be forced onto the skinned path.
        ScenePropTrack crate = Track("models/props_gameplay/resupply_locker.mdl", attachedTo: null);

        WornModels.From([crate], []).ShouldBeEmpty();
    }

    [Test]
    public void From_AnAttachedBrushEntity_IsNotWorn()
    {
        // A brush entity has no skeleton to merge onto, so asking for one would force a pointless
        // skinned load of map geometry. `Kind` is derived from the path: a `*NN` name is a brush.
        ScenePropTrack door = Track("*27", attachedTo: 4);

        door.Kind.ShouldNotBe(SceneModelKind.Studio, "the fixture must really be a brush entity");

        WornModels.From([door], []).ShouldBeEmpty();
    }

    [Test]
    public void From_TheSameModelWornAndHeld_IsListedOnce()
    {
        // Both sources can name one model — a spy's knife is a held weapon and, on another player,
        // a bone-merged prop. It is a set, so `PropModels` is asked for it once.
        ScenePropTrack alsoAProp = Track(HeldWeapon, attachedTo: 4);

        WornModels.From([alsoAProp], [HeldWeapon]).Count.ShouldBe(1);
    }

    [Test]
    public void From_APathDifferingOnlyInCase_IsListedOnce()
    {
        // Model paths are compared without case everywhere else here, and the precache is not
        // consistent about it. Two spellings would load the same file twice and — worse — a lookup
        // elsewhere could find the set does not contain the spelling it holds.
        WornModels.From([], [HeldWeapon, HeldWeapon.ToUpperInvariant()]).Count.ShouldBe(1);
    }

    [Test]
    public void From_AnEmptyWeaponPath_IsNotWorn()
    {
        // A weapon whose model never resolved arrives as an empty string, and asking the loader for
        // "" reports a missing model on every demo in the corpus.
        WornModels.From([], [string.Empty]).ShouldBeEmpty();
    }

    /// <summary>A prop track, with the attachment the timeline would have set.</summary>
    private static ScenePropTrack Track(string modelPath, int? attachedTo)
    {
        ScenePropTrack track = new(entityIndex: 9, modelPath);

        track.AttachedTo = attachedTo;

        return track;
    }
}
