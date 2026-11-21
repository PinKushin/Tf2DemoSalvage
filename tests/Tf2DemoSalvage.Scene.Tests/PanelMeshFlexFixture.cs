using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A panel model's mesh flex (B779), hand-built: descriptor 0 moves <c>.vvd</c> vertex 1 by (2, 0, 0) with a normal delta of
/// (0, 0, 1), on a ramp rising from <c>target0</c> to full at <c>target0 + 1</c>. Two corners, corner <i>c</i> on vertex
/// <i>c</i>, packed in the order [1, 0], so the moved vertex is buffer slot 0 and slot 1 never moves.
/// </summary>
internal static class PanelMeshFlexFixture
{
    /// <summary>The packed order a cache answers for a flexed model.</summary>
    public static readonly IReadOnlyList<int> Order = [1, 0];

    /// <summary>The frames with that mesh flex added to their rules.</summary>
    /// <param name="frames">The model, whose <see cref="PropModels.ModelFrames.Flex"/> holds the descriptors, controllers and rules.</param>
    /// <param name="target0">Where the ramp leaves zero; below zero, a zero weight already moves the vertex fully.</param>
    /// <returns>The flexed model.</returns>
    public static PropModels.ModelFrames Flexed(PropModels.ModelFrames frames, float target0 = 0f) => frames with
    {
        Flex = frames.Flex with
        {
            Flexes = [new StudioMeshFlex(0, 0, target0, target0 + 1f, 10f, 11f, [new StudioVertAnim(1, 255, 0, (2f, 0f, 0f), (0f, 0f, 1f))])],
        },
        CornerVertex = [0, 1],
        VertexCount = 2,
    };

    /// <summary>Asserts the face is the vertex moved by <paramref name="weight"/>, in slot 0 alone.</summary>
    /// <param name="instance">The drawn model.</param>
    /// <param name="weight">The ramped descriptor weight expected.</param>
    public static void ShouldBeFlexedBy(ModelInstance instance, float weight)
    {
        float[] face = instance.Flex.ShouldNotBeNull("CMDL::Draw flexes the mesh of a model with flex descriptors");
        face.Length.ShouldBe(12);
        face[0].ShouldBe(2f * weight, 1e-5f, "slot 0's position delta");
        face[5].ShouldBe(weight, 1e-5f, "slot 0's normal delta");
        face[6..].ShouldAllBe(value => value == 0f, "slot 1 carries the unmoved vertex 0");
        instance.FlexRange.ShouldBe(new FlexSlots(0, 1));
    }
}
