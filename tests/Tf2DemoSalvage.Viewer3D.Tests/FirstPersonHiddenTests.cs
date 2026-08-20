using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// What is not drawn when the camera is inside somebody's head.
/// </summary>
/// <remarks>
/// **The engine does not draw the player whose eyes you are using**, and the first capture of this
/// viewer's first-person view is what made that obvious — the owner's words were "a bunch of purple
/// and black checkerboard textures or some sort of blobs, maybe parts of a player model", then "a
/// player hat or something that doesnt have textures". Both are the recorder's own gear seen from
/// the inside.
///
/// <c>C_BasePlayer::ShouldDrawThisPlayer</c> (<c>c_baseplayer.cpp:1992</c>):
///
/// <code>
/// if ( !InFirstPersonView() )                                  { return true; }
/// if ( !UseVR() &amp;&amp; cl_first_person_uses_world_model.GetBool() ) { return true; }
/// return false;
/// </code>
///
/// So the rule is: in first person, the viewed player is hidden. The cvar that overrides it is the
/// "see your own body" option and is off by default.
///
/// **Cosmetics are the half that is easy to miss.** A hat is a separate entity bone-merged onto its
/// wearer, so hiding the player alone leaves the hat hanging in the middle of the picture — which
/// is exactly what was seen. Anything attached to the hidden player goes with them.
/// </remarks>
public sealed class FirstPersonHiddenTests
{
    /// <summary>The entity whose eyes the camera is in.</summary>
    private const int Viewed = 3;

    [Test]
    public void Visible_InFirstPerson_DropsThePlayerBeingLookedThrough()
    {
        IReadOnlyList<SceneProp> drawn = FirstPersonVisibility.Visible(Scene(), Viewed);

        drawn.Select(prop => prop.EntityIndex).ShouldNotContain(Viewed);
    }

    [Test]
    public void Visible_InFirstPerson_DropsWhateverIsAttachedToThem()
    {
        // **The hat.** A cosmetic is its own entity with no origin of its own — it takes the
        // wearer's bones by name — so hiding the wearer and keeping the hat leaves it floating
        // exactly where the camera is.
        IReadOnlyList<SceneProp> drawn = FirstPersonVisibility.Visible(Scene(), Viewed);

        drawn.Select(prop => prop.EntityIndex).ShouldNotContain(30);
        drawn.Select(prop => prop.EntityIndex).ShouldNotContain(31);
    }

    [Test]
    public void Visible_InFirstPerson_KeepsEveryoneElseAndTheirGear()
    {
        // **The control, and it is the whole difference between hiding a player and hiding
        // players.** A filter that dropped every attached entity, or every player, would satisfy
        // both tests above and empty the map.
        IReadOnlyList<SceneProp> drawn = FirstPersonVisibility.Visible(Scene(), Viewed);

        drawn.Select(prop => prop.EntityIndex).ShouldContain(4);
        drawn.Select(prop => prop.EntityIndex).ShouldContain(40);
        drawn.Select(prop => prop.EntityIndex).ShouldContain(99);
    }

    [Test]
    public void Visible_WithNobodyBeingLookedThrough_DrawsEverything()
    {
        // Every other camera mode. Passing null is how the map and free views ask, and a filter
        // that hid something there would make the feature cost the views that already worked.
        FirstPersonVisibility.Visible(Scene(), null).Count.ShouldBe(Scene().Count);
    }

    /// <summary>A scene with two players, their hats, and a world prop.</summary>
    private static List<SceneProp> Scene() =>
    [
        Prop(Viewed, "models/player/scout.mdl"),
        Prop(30, "models/player/items/scout/hat.mdl", attachedTo: Viewed),
        Prop(31, "models/weapons/w_scattergun.mdl", attachedTo: Viewed),
        Prop(4, "models/player/soldier.mdl"),
        Prop(40, "models/player/items/soldier/hat.mdl", attachedTo: 4),
        Prop(99, "models/props_gameplay/resupply_locker.mdl"),
    ];

    private static SceneProp Prop(int entityIndex, string model, int? attachedTo = null) =>
        new(entityIndex, model, SceneModelKind.Studio, new ScenePose(), attachedTo);
}
