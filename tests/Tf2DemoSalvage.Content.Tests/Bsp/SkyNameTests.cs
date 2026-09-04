using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The map's 2D skybox — <c>worldspawn</c>'s <c>skyname</c> and the six faces it names.
/// </summary>
/// <remarks>
/// **`skyname` sets a CONVAR rather than a field**, which is why a map without the key still has a
/// sky: `CWorld::KeyValue` (<c>server/world.cpp:417</c>) writes it into `sv_skyname`, declared with
/// a default of <c>sky_urb01</c> (<c>movevars_shared.cpp:105</c>).
/// </remarks>
public sealed class SkyNameTests
{
    [Test]
    public void SkyName_ForAMapThatStatesOne_IsTheMapsOwn()
    {
        BspEntities.SkyName(Parse("""
            {
            "classname" "worldspawn"
            "skyname" "sky_harvest_01"
            }
            """)).ShouldBe("sky_harvest_01");
    }

    [Test]
    public void SkyName_ForAMapThatStatesNone_IsValvesConvarDefault()
    {
        // Not "no sky": sv_skyname is FCVAR_ARCHIVE with a real default, so a map silent on the
        // key inherits one rather than drawing nothing.
        BspEntities.SkyName(Parse("""
            {
            "classname" "worldspawn"
            }
            """)).ShouldBe("sky_urb01");
    }

    /// <remarks>
    /// **The control.** Without it, a reader that answered the FIRST entity's `skyname` regardless
    /// of class would pass both cases above — and a `sky_camera` or a `light_environment` carrying
    /// an unrelated key would then decide the sky.
    /// </remarks>
    [Test]
    public void SkyName_WhenAnotherEntityCarriesTheKey_IgnoresIt()
    {
        BspEntities.SkyName(Parse("""
            {
            "classname" "sky_camera"
            "skyname" "sky_wrong"
            }
            {
            "classname" "worldspawn"
            "skyname" "sky_right"
            }
            """)).ShouldBe("sky_right");
    }

    /// <remarks>
    /// **Two arrays of these six strings exist in the SDK in different orders.**
    /// `skyboxswapper.cpp:60` uses <c>{ rt, bk, lf, ft, up, dn }</c> — for precaching, where order
    /// does not matter. vbsp's assigns DIRECTIONS, because its output is a cubemap and the index is
    /// the cube face (<c>cubemap.cpp:195</c>): <c>{ rt, lf, bk, ft, up, dn }</c>. Taking the wrong
    /// one swaps `bk` and `lf` and puts two walls of the sky on each other's side.
    /// </remarks>
    [Test]
    public void SkyFaces_AreInCubeFaceOrder_NotThePrecacheOrder()
    {
        BspEntities.SkyFaces("sky_harvest_01").ShouldBe([
            "skybox/sky_harvest_01rt",
            "skybox/sky_harvest_01lf",
            "skybox/sky_harvest_01bk",
            "skybox/sky_harvest_01ft",
            "skybox/sky_harvest_01up",
            "skybox/sky_harvest_01dn",
        ]);
    }

    private static IReadOnlyList<BspEntity> Parse(string text) =>
        BspEntities.Parse(Encoding.UTF8.GetBytes(text));
}
