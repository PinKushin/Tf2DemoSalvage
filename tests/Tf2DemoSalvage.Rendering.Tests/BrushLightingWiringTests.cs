using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A brush entity reaches the renderer lightmapped; a studio model reaches it with a cube.
/// </summary>
/// <remarks>
/// **The wiring, which the component tests cannot see (B131).** <c>BrushModels</c> can put the right
/// lightmap coordinates on a vertex and the shader can sample them, and a door still draws flat if
/// the instance carries an ambient cube — because the cube branch OVERWRITES the lightmap sample
/// rather than adding to it. Two correct halves, one wrong picture, and every component test green.
///
/// So the claim measured here is about what <c>Instances</c> hands over, not about what either half
/// can do when called with the right arguments.
///
/// **Both kinds in one test, because either alone is satisfiable by a constant.** "Brush gets no
/// cube" passes against a renderer that lights nothing; "studio gets a cube" passes against the
/// original defect. The pair is the claim.
/// </remarks>
public sealed class BrushLightingWiringTests
{
    [Test]
    public void Instances_ABrushEntityAndAStudioModel_AreLitByDifferentMeans()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props =
        [
            new(1, "models/props/crate.mdl", SceneModelKind.Studio, Pose, null),
            new(2, "*12", SceneModelKind.Brush, Pose, null),
        ];

        models.Add(props, OneTriangle);
        models.Instances(props, instances, Cube, Sun, 0d);

        instances.Count.ShouldBe(2);

        ModelInstance studio = Find(instances, "models/props/crate.mdl");
        ModelInstance brush = Find(instances, "*12");

        // A studio model is lit by the leaf it stands in, because the same model stands in many
        // places under different light and there is nowhere to bake it.
        studio.Light.ShouldNotBeNull();
        studio.Sun.ShouldNotBeNull();

        // **A brush entity is not, and null is the whole fix.** vrad lit its faces where the mapper
        // left them (vrad.cpp:703) and those samples travel on the vertices; the renderer reads a
        // null cube as "none supplied" and leaves the atlas sample standing, which is what
        // LightmappedGeneric does.
        brush.Light.ShouldBeNull();
        brush.Sun.ShouldBeNull();
    }

    private static ScenePose Pose => new() { X = 100f, Scale = 1f };

    private static ModelInstance Find(IReadOnlyList<ModelInstance> instances, string path)
    {
        foreach (ModelInstance instance in instances)
        {
            if (instance.ModelPath == path)
            {
                return instance;
            }
        }

        throw new KeyNotFoundException(path);
    }

    private static PointLighting Cube(float x, float y, float z) =>
        PointLighting.Bounce(
            new((0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f),
                (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f)));

    private static SunLight? Sun(float x, float y, float z) =>
        new(1f, 1f, 1f, 0f, 0f, -1f);

    private static PropModels.ModelFrames OneTriangle(string path) =>
        new(
            [[
                new PropVertex(0f, 0f, 0f, 0f, 0f, 0),
                new PropVertex(1f, 0f, 0f, 1f, 0f, 0),
                new PropVertex(1f, 1f, 0f, 1f, 1f, 0),
            ]],
            new Dictionary<int, (int, int, float)>(),
            [],
            []);
}
