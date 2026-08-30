namespace Tf2DemoSalvage.Scene;

// These three types describe GEOMETRY, not drawing: a vertex, a run of triangles sharing a
// material, and the sun reaching a model. They carry no Direct3D type and no rendering decision —
// the renderer consumes them, and this layer produces them.
//
// They lived at the top of WorldRenderer.cs until 2026-08-22 for no better reason than that the
// renderer was written first. Moving them here is what let the scene layer compile without
// Silk.NET at all (D59), and they are public rather than internal because they are now the
// contract between two assemblies rather than three structs in one file.

/// <summary>One corner of a world triangle, ready for the GPU.</summary>
/// <param name="X">Clip-space horizontal position, -1 to 1.</param>
/// <param name="Y">Clip-space vertical position, -1 to 1.</param>
/// <param name="Depth">Clip-space depth, 0 nearest; derived from the surface's height.</param>
/// <param name="U">Texture coordinate across.</param>
/// <param name="V">Texture coordinate down.</param>
/// <param name="LightU">Lightmap atlas coordinate across.</param>
/// <param name="LightV">Lightmap atlas coordinate down.</param>
/// <param name="Alpha">Blend between the material's two textures; 0 draws only the first.</param>
/// <param name="Red">Per-vertex light, one for anything that takes its light from the lightmap.</param>
/// <param name="Green">Per-vertex light.</param>
/// <param name="Blue">Per-vertex light.</param>
/// <param name="NormalX">World surface normal, east-west; lights models from the ambient cube.</param>
/// <param name="NormalY">World surface normal, north-south.</param>
/// <param name="NormalZ">World surface normal, vertically.</param>
/// <param name="LightStep">How far along the atlas each directional lightmap sits, or zero.</param>
/// <remarks>
/// **The per-vertex colour exists for static props and nothing else.** A brush face takes its light
/// from the lightmap atlas; a model cannot, because the same model stands in many places under
/// different light, so the compiler bakes a colour per vertex per placement. Brush faces carry
/// white here, which multiplies to no change, so one shader serves both.
/// </remarks>
/// <param name="NextX">Where this vertex sits one animation frame later.</param>
/// <param name="NextY">Where this vertex sits one animation frame later.</param>
/// <param name="NextZ">Where this vertex sits one animation frame later.</param>
/// <param name="NextNormalX">Its normal one animation frame later.</param>
/// <param name="NextNormalY">Its normal one animation frame later.</param>
/// <param name="NextNormalZ">Its normal one animation frame later.</param>
/// <param name="BoneA">Which bone moves this vertex most, for a model skinned on the GPU.</param>
/// <param name="BoneB">The second bone moving it.</param>
/// <param name="BoneC">The third bone moving it.</param>
/// <param name="WeightA">How much the first bone moves it.</param>
/// <param name="WeightB">How much the second moves it.</param>
/// <param name="WeightC">How much the third moves it.</param>
public readonly record struct WorldVertex(
    float X, float Y, float Depth, float U, float V, float LightU, float LightV, float Alpha,
    float Red = 1f, float Green = 1f, float Blue = 1f, float LightStep = 0f,
    float NormalX = 0f, float NormalY = 0f, float NormalZ = 1f,
    float NextX = 0f, float NextY = 0f, float NextZ = 0f,
    float NextNormalX = 0f, float NextNormalY = 0f, float NextNormalZ = 1f,
    float BoneA = 0f, float BoneB = 0f, float BoneC = 0f,
    float WeightA = 0f, float WeightB = 0f, float WeightC = 0f);

/// <summary>A run of triangles sharing one texture.</summary>
/// <param name="MaterialIndex">Which material, indexed into the map's table.</param>
/// <param name="FirstVertex">Where the run starts.</param>
/// <param name="VertexCount">How many vertices it covers.</param>
/// <param name="BodyPart">Which body part this run belongs to, for a model batch.</param>
/// <param name="BodyModel">Which of that part's alternatives, so one can be chosen per entity.</param>
/// <param name="Category">What this run of triangles is, for the category view (B219).</param>
/// <param name="MaterialSlot">The mesh skinref this run came from, for the skin lookup (B229).</param>
/// <remarks>
/// **A batch never spans two body parts**, which is what makes the choice possible at draw time. The
/// grouping key is the material AND the part and alternative it came from, so a run can be skipped
/// whole when the entity's <c>m_nBody</c> did not select it. Merging on material alone would put a
/// capture point's three signs in one run, and then no per-entity decision could separate them.
/// </remarks>
public readonly record struct WorldBatch(
    int MaterialIndex,
    int FirstVertex,
    int VertexCount,
    int BodyPart = 0,
    int BodyModel = 0,

    // **What this run of triangles IS, for the category view** (B219). It rode in the vertex
    // COLOUR until 2026-08-27, which meant switching the view rebuilt every vertex in the map —
    // and `ClearWorld` discarding the models with them was the bug that forced this out. A batch
    // belongs to exactly one category, so this is where the answer belongs: the colour is then
    // chosen at draw time and the toggle is a constant write rather than a rebuild.
    SurfaceCategory Category = SurfaceCategory.Brush,

    // **The skinref every triangle in this run shares, for the skin lookup at draw time** (B229).
    // `MaterialIndex` above is family zero's answer, and asking what that answer becomes in
    // another family is a question with two answers as soon as two meshes share a material —
    // which is why the run is keyed on this as well as on the material.
    //
    // −1 for world brushwork, which has no skin table and is never handed a family.
    int MaterialSlot = -1);

/// <summary>What a drawn surface is, for the diagnostic view.</summary>
/// <remarks>
/// **Public, and beside <see cref="WorldBatch"/> rather than inside the builder**, because the
/// renderer now needs it: the category decides a colour at DRAW time instead of at build time, so
/// the type has to cross the same boundary the batch does.
/// </remarks>
public enum SurfaceCategory
{
    /// <summary>Ordinary world brushwork.</summary>
    Brush,

    /// <summary>A displacement's subdivided terrain.</summary>
    Terrain,

    /// <summary>A placed model.</summary>
    Prop,

    /// <summary>An overlay fragment — a marking clipped to the surface it lies on.</summary>
    /// <remarks>
    /// **Added because its absence was read as an answer.** Overlay fragments carried no vertex
    /// colour, so they took the default of white — which is not a category colour but the lack of
    /// one, and there was no legend entry saying so. During the B154 hunt that white was read first
    /// as "an uncoloured surface" and then as the sign being investigated, and it was neither. A
    /// diagnostic view that omits a category cannot answer "is anything here" for that category,
    /// which is the one question it exists to answer.
    /// </remarks>
    Overlay,

    /// <summary>Anything whose material could not be resolved.</summary>
    Missing,
}

/// <summary>The sun as it reaches one model.</summary>
/// <param name="Red">Intensity, linear, from the map's own emit_skylight.</param>
/// <param name="Green">Intensity, linear.</param>
/// <param name="Blue">Intensity, linear.</param>
/// <param name="DirectionX">The direction the light travels, as the map stores it.</param>
/// <param name="DirectionY">The direction the light travels.</param>
/// <param name="DirectionZ">The direction the light travels.</param>
/// <remarks>
/// Present only for a model that traced to sky. A sky light is defined with that condition in
/// Valve's own description — "surface must trace to SKY texture" — so a model in shade carries no
/// sun rather than a dimmed one.
/// </remarks>
public readonly record struct SunLight(
    float Red, float Green, float Blue,
    float DirectionX, float DirectionY, float DirectionZ);
