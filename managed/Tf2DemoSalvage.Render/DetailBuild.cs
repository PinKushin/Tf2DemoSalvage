using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>One view's detail build: its sprites and its models, around that view's origin (B363, B434, B711).</summary>
/// <remarks>
/// **One per view that builds its own**, because the engine builds each view's detail lists around
/// <c>CurrentViewOrigin()</c> (<c>BuildDetailObjectRenderLists( origin )</c>, `viewrender.cpp:3488-3491`), and
/// <c>CollateRenderablesInLeaf</c> adds a leaf's detail props only when that view's build reached it
/// (`ShouldDrawDetailObjectsInLeaf`, `clientleafsystem.cpp:1718-1751`): the main view's around the eye, the monitor's
/// around the point camera, the 3D sky's around the sky camera.
/// </remarks>
internal sealed class DetailBuild
{
    /// <summary>What it was last built for: the eye, the fade and the view direction.</summary>
    public (float X, float Y, float Z, float Distance, float Fade, float Factor, (float, float, float) Forward)? BuiltFor { get; set; }

    /// <summary>The sprites' quads.</summary>
    public List<DetailSpriteVertex> Corners { get; } = [];

    /// <summary>The quads by BSP leaf, with the translucent pass's per-leaf cursor (B434).</summary>
    public DetailSpriteLeaves Leaves { get; } = new();

    /// <summary>The models as placed.</summary>
    public List<DetailModelInstance> Placed { get; } = [];

    /// <summary>The same, as instances the model pass draws.</summary>
    public List<ModelInstance> Instances { get; } = [];

    /// <summary>
    /// <see cref="Instances"/> by the lump leaf each is filed in, as indices: what a view collates at each listed leaf
    /// (`CollateRenderablesInLeaf`, B711). A key's list is emptied, not removed, between builds.
    /// </summary>
    public Dictionary<int, List<int>> ByLeaf { get; } = [];

    /// <summary>Beside <see cref="Instances"/>: each one's detail prop, which another view fades and turns from its own eye.</summary>
    public List<int> Props { get; } = [];

    /// <summary>Forgets the build, for a new map's props.</summary>
    public void Clear()
    {
        Corners.Clear();
        Leaves.Clear();
        Placed.Clear();
        Instances.Clear();
        ByLeaf.Clear();
        Props.Clear();
        BuiltFor = null;
    }
}
