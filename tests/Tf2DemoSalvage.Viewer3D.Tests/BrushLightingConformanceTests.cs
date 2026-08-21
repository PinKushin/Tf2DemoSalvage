using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// A brush entity is lightmapped like the wall beside it, not lit like a model.
/// </summary>
/// <remarks>
/// **B131.** A door drawn through this project's entity path took its light from the leaf's ambient
/// cube while the wall it sits in took a lightmap, so an open door was a flat panel against a shaded
/// corridor. The entry filed it as a real design choice — carry lightmap coordinates into the entity
/// vertex format, or draw brushwork with the world shader — and Valve's source turns out to answer
/// both halves, so neither was a choice.
///
/// **VRAD lights every model's faces, not just the world's.** <c>utils/vrad/vrad.cpp:703</c>,
/// <c>MakePatches</c>:
///
/// <code>
/// for (i=0 ; i&lt;nummodels ; i++)
/// {
///     mod = dmodels+i;
///     ent = EntityForModel (i);
///     VectorCopy (vec3_origin, origin);
///
///     // bmodels with origin brushes need to be offset into their
///     // in-use position
///     GetVectorForKey (ent, "origin", origin);
///
///     for (j=0 ; j&lt;mod->numfaces ; j++)
///     {
///         fn = mod->firstface + j;
///         face_entity[fn] = ent;
///         VectorCopy (origin, face_offset[fn]);
/// </code>
///
/// Two facts fall out of that loop, and the project needed both:
///
/// - **A brush entity's faces carry real baked lightmap samples**, in the same lighting lump and
///   addressed the same way as the world's. Nothing has to be invented for them.
/// - **They are lit at their IN-USE position** — where the mapper left the door, which is closed —
///   and the samples never move afterwards. So an opening door carries its closed-position lighting
///   with it. That is not an approximation to apologise for; it is what Source looks like, and it is
///   why no relighting step is needed when the entity moves.
///
/// **And the engine draws them through the world's own path when it can.**
/// <c>C_BaseEntity::DrawBrushModel</c> opens with the comment
/// <c>// Identity brushes are drawn in view->DrawWorld as an optimization</c>
/// (<c>game/client/c_baseentity.cpp:1962</c>), so a brush entity that has not moved is literally
/// drawn by the world renderer. One that has moved goes to <c>DrawBrushModelEx</c> with a transform
/// — same surfaces, same materials, same lightmaps, different matrix.
///
/// So the answer is: same lighting as the world, per-instance transform. This project already has
/// one vertex format and one shader for both, which is why the fix was carrying two floats rather
/// than building a second path.
/// </remarks>
public sealed class BrushLightingConformanceTests
{
    [Test]
    public void MakePatches_TheLoopOverModels_CoversEveryModelAndNotJustTheWorld()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/utils/vrad/vrad.cpp")
            ?? throw new InvalidOperationException("vrad.cpp is missing from the SDK");

        Match body = new Regex(
            @"void MakePatches \(void\)(?s).{0,2000}?\n\}",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10)).Match(text);

        body.Success.ShouldBeTrue("MakePatches was not found in vrad");

        // Every model, not model zero. This is the line that says a door has a lightmap at all.
        body.Value.ShouldContain("i<nummodels");

        // And lit where it is used, which is why the samples do not follow it when it moves.
        body.Value.ShouldContain("in-use position");
        body.Value.ShouldContain("face_offset");
    }

    [Test]
    public void DrawBrushModel_AnUnmovedBrushEntity_IsDrawnByTheWorldPath()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        string text = SourceSdk.Text("src/game/client/c_baseentity.cpp")
            ?? throw new InvalidOperationException("c_baseentity.cpp is missing from the SDK");

        // The engine's own statement that brush entities and the world share a drawing path, which
        // is what makes "same shader, same lightmap, different matrix" a transcription rather than
        // an interpretation.
        text.ShouldContain("Identity brushes are drawn in view->DrawWorld as an optimization");
    }
}
