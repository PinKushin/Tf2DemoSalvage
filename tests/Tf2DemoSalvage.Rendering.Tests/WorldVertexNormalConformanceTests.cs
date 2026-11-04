using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A brush face's vertex carries the compiled vertex normal and a tangent frame, because a reflection reads both (B669).
/// </summary>
/// <remarks>
/// *Read from published source*, the shader's half: `lightmappedgeneric_vs20.fxc:113-129` builds
/// `tangentSpaceTranspose` from the vertex's normal and tangents, `lightmappedgeneric_ps2_3_x.h:479` takes
/// `worldSpaceNormal = mul( vNormal, i.tangentSpaceTranspose )` and `:526` reflects about it.
///
/// *Read in disassembly*, the data's half, `engine.dll` x64 `FUN_1800f4d40` (the build of a surface's vertices):
/// the normal is `vertnormals[ vertnormalindices[ firstvertnormal + corner ] ]`, each face's first index being the
/// running total of the corners before it (vrad's `SaveVertexNormals`, every face in order). The tangent frame is
/// `FUN_1800f83c0` over `FUN_1800f8590`:
///
/// <code>
/// tVect  = normalize( texinfo.textureVecs[1].xyz )
/// negate = dot( cross( normalize( textureVecs[0].xyz ), tVect ), plane.normal ) &gt; 0     // the PLANE, unflipped
/// S      = normalize( cross( normal, tVect ) )
/// T      = normalize( cross( S, normal ) )
/// if ( negate ) S = -S
/// </code>
///
/// **The world builder set none of it, so every brush face reflected as though it faced straight up.** The pipeline had
/// been proved with hand-set normals (`ReflectionRenderTests`), and production's `WorldVertex` kept its record default
/// `NormalZ = 1`. On a floor that is the truth. On the far pane it read the cube at (0.40, 0.88, 0.23) instead of
/// (0.40, -0.22, 0.89): a value of 0.02 where TF2 has about 0.25.
/// </remarks>
public sealed class WorldVertexNormalConformanceTests
{
    private const string VertexShader = "src/materialsystem/stdshaders/lightmappedgeneric_vs20.fxc";
    private const string PixelShader = "src/materialsystem/stdshaders/lightmappedgeneric_ps2_3_x.h";

    private static readonly BspMaterial[] Materials = [new BspMaterial("glass/glasswindow001a", (0.5f, 0.5f, 0.5f), 128, 128)];

    [Test]
    public void Sdk_TheShadersFrame_IsBuiltFromTheVertexNormalAndTangents()
    {
        string vertex = Sdk(VertexShader);

        vertex.ShouldContain("float3 worldNormal = mul( vObjNormal, ( float3x3 )cModel[0] );", Case.Sensitive);
        vertex.ShouldContain("o.tangentSpaceTranspose[0] = worldTangentS;", Case.Sensitive);
        vertex.ShouldContain("o.tangentSpaceTranspose[1] = worldTangentT;", Case.Sensitive);
        vertex.ShouldContain("o.tangentSpaceTranspose[2] = worldNormal;", Case.Sensitive);

        string pixel = Sdk(PixelShader);

        pixel.ShouldContain("float3 worldSpaceNormal = mul( vNormal, i.tangentSpaceTranspose );", Case.Sensitive);
        pixel.ShouldContain("float3 reflectVect = CalcReflectionVectorUnnormalized( worldSpaceNormal, worldVertToEyeVector );", Case.Sensitive);
    }

    /// <remarks>Each corner keeps its own: a smoothed face's normals differ across it, so the plane cannot stand in.</remarks>
    [Test]
    public void Build_AFaceWithCompiledNormals_EveryCornerCarriesItsOwn()
    {
        BspSurface face = Face(
            (1f, 0f, 0f),
            [
                new SurfaceVertex(0f, 0f, 0f, 0f, 0f, 0f, 0f) { NormalX = 0.6f, NormalY = 0f, NormalZ = 0.8f },
                new SurfaceVertex(64f, 0f, 0f, 1f, 0f, 1f, 0f) { NormalX = 0f, NormalY = 0.6f, NormalZ = 0.8f },
                new SurfaceVertex(64f, 64f, 0f, 1f, 1f, 1f, 1f) { NormalX = 0f, NormalY = 0f, NormalZ = 1f },
            ]);

        IReadOnlyList<WorldVertex> built = MapWorldBuilder.Build(null, [face], Materials, LightmapAtlas.Pack([]), null).Vertices;

        built.Count.ShouldBe(3);
        (built[0].NormalX, built[0].NormalY, built[0].NormalZ).ShouldBe((0.6f, 0f, 0.8f));
        (built[1].NormalX, built[1].NormalY, built[1].NormalZ).ShouldBe((0f, 0.6f, 0.8f));
        (built[2].NormalX, built[2].NormalY, built[2].NormalZ).ShouldBe((0f, 0f, 1f));
    }

    /// <summary>A map compiled without vrad has no lump to read; vbsp's own value there is the face's plane.</summary>
    [Test]
    public void Build_AFaceWithNoCompiledNormal_TakesItsPlanesNormal()
    {
        BspSurface face = Face(
            (0f, -1f, 0f),
            [
                new SurfaceVertex(0f, 0f, 0f, 0f, 0f, 0f, 0f),
                new SurfaceVertex(64f, 0f, 0f, 1f, 0f, 1f, 0f),
                new SurfaceVertex(64f, 0f, 64f, 1f, 1f, 1f, 1f),
            ]);

        WorldVertex first = MapWorldBuilder.Build(null, [face], Materials, LightmapAtlas.Pack([]), null).Vertices[0];

        (first.NormalX, first.NormalY, first.NormalZ).ShouldBe((0f, -1f, 0f));
    }

    /// <summary>
    /// A base face's normal with none compiled, on a face whose side flips <see cref="BspSurface.Normal"/>, is the plane's. Measured on cp_process_f12:
    /// compiled normals side with the plane on 26,602 of 26,782 corners of flipped faces, and 6 against it (B687).
    /// </summary>
    [Test]
    public void Build_AFlippedFaceWithNoCompiledNormal_TakesThePlanesDirection()
    {
        BspSurface face = Face(
            (1f, 0f, 0f),
            [
                new SurfaceVertex(0f, 0f, 0f, 0f, 0f, 0f, 0f),
                new SurfaceVertex(0f, 64f, 0f, 1f, 0f, 1f, 0f),
                new SurfaceVertex(0f, 64f, 64f, 1f, 1f, 1f, 1f),
            ]) with { PlaneNormal = (-1f, 0f, 0f) };

        WorldVertex first = MapWorldBuilder.Build(null, [face], Materials, LightmapAtlas.Pack([]), null).Vertices[0];

        (first.NormalX, first.NormalY, first.NormalZ).ShouldBe((-1f, 0f, 0f));
    }

    /// <summary>
    /// `FUN_180112d30`: an overlay fragment's corner normal is the surface's plane normal, unflipped. `FUN_180111830` writes no tangent, so the fragment has no
    /// frame, even on a face with a texture projection that gives its own corners one.
    /// </summary>
    [Test]
    public void Build_AnOverlayOnAFlippedProjectedFace_TakesThePlaneAndNoFrame()
    {
        BspSurface face = Face(
            (1f, 0f, 0f),
            [
                new SurfaceVertex(0f, 0f, 0f, 0f, 0f, 0f, 0f),
                new SurfaceVertex(0f, 64f, 0f, 1f, 0f, 1f, 0f),
                new SurfaceVertex(0f, 64f, 64f, 1f, 1f, 1f, 1f),
                new SurfaceVertex(0f, 0f, 64f, 0f, 1f, 0f, 1f),
            ]) with { PlaneNormal = (-1f, 0f, 0f), TextureS = (0f, 1f, 0f, 0f), TextureT = (0f, 0f, 1f, 0f) };

        BspOverlay overlay = new(
            0, 0, 0, 0, [0], (0f, 1f), (0f, 1f), [(-8f, -8f), (8f, -8f), (8f, 8f), (-8f, 8f)], (0f, 32f, 32f), (-1f, 0f, 0f), (0f, 1f, 0f), (0f, 0f, 1f));

        MapWorld world = MapWorldBuilder.Build(null, [face], Materials, LightmapAtlas.Pack([]), null, overlays: [overlay]);

        world.Vertices[0].TangentW.ShouldNotBe(0f, "the control: the face's own corner has a frame");

        WorldVertex corner = world.Vertices[^1];

        corner.U.ShouldBeInRange(0f, 1f);
        (corner.NormalX, corner.NormalY, corner.NormalZ).ShouldBe((-1f, 0f, 0f));
        (corner.TangentX, corner.TangentY, corner.TangentZ, corner.TangentW).ShouldBe((0f, 0f, 0f, 0f));
    }

    /// <summary>
    /// Logged from the running engine, cp_process_f12, build 11191711, at `FUN_1800f83c0`'s `RET` (`0x1800f978b`): normal, tvec, the flip the caller passed, S and T as written
    /// (`measurements/2026-11-03-b669-review-admission.md`). The flip is `FUN_1800f8590`'s, `( S × T ) · plane &gt; 0`; the S handed in here sets that sign.
    /// </summary>
    [TestCase(0f, -1f, 0f, false, 1f, 0f, 0f)]
    [TestCase(-0.50545f, -0.86286f, 0f, false, 0.86286f, -0.50545f, 0f)]
    [TestCase(-0.92388f, 0.38268f, 0f, true, 0.38268f, 0.92388f, 0f)]
    [TestCase(-0.92388f, -0.38268f, 0f, true, -0.38268f, 0.92388f, 0f)]
    [TestCase(0f, 1f, 0f, false, -1f, 0f, 0f)]
    [TestCase(0f, 1f, 0f, true, 1f, 0f, 0f)]
    public void Of_TheEnginesLoggedCorners_ReproduceItsSAndT(float nx, float ny, float nz, bool flip, float sx, float sy, float sz)
    {
        (float X, float Y, float Z) t = (0f, 0f, -1f);
        (float X, float Y, float Z) normal = (nx, ny, nz);

        // A texture S across the normal, one way or the other, so `( S × T ) · normal` has the sign the logged flip says.
        (float X, float Y, float Z) acrossThenT = ((ny * t.Z) - (nz * t.Y), (nz * t.X) - (nx * t.Z), (nx * t.Y) - (ny * t.X));
        float toward = flip ? -1f : 1f;
        (float, float, float, float) textureS = (acrossThenT.X * toward, acrossThenT.Y * toward, acrossThenT.Z * toward, 0f);

        (float X, float Y, float Z, float W) tangent = SurfaceTangent.Of(normal, textureS, (t.X, t.Y, t.Z, 0f), normal);

        tangent.X.ShouldBe(sx, 0.0001f);
        tangent.Y.ShouldBe(sy, 0.0001f);
        tangent.Z.ShouldBe(sz, 0.0001f);
        tangent.W.ShouldBe(flip ? 1f : -1f);

        // Every logged T was (0 0 -1): the shader's `cross( N, S ) * w`.
        (float crossX, float crossY, float crossZ) = ((ny * tangent.Z) - (nz * tangent.Y), (nz * tangent.X) - (nx * tangent.Z), (nx * tangent.Y) - (ny * tangent.X));

        (crossX * tangent.W).ShouldBe(0f, 0.0001f);
        (crossY * tangent.W).ShouldBe(0f, 0.0001f);
        (crossZ * tangent.W).ShouldBe(-1f, 0.0001f);
    }

    /// <summary>The frame's arithmetic reaches the vertex: a face's S lands in the tangent, its handedness in w.</summary>
    [Test]
    public void Build_AFaceWithATextureProjection_EveryCornerCarriesItsTangentFrame()
    {
        BspSurface face = Face(
            (0f, 0f, 1f),
            [
                new SurfaceVertex(0f, 0f, 0f, 0f, 0f, 0f, 0f),
                new SurfaceVertex(64f, 0f, 0f, 1f, 0f, 1f, 0f),
                new SurfaceVertex(64f, 64f, 0f, 1f, 1f, 1f, 1f),
            ]) with
        {
            TextureS = (4f, 0f, 0f, 0f),
            TextureT = (0f, -4f, 0f, 0f),
            PlaneNormal = (0f, 0f, 1f),
        };

        WorldVertex first = MapWorldBuilder.Build(null, [face], Materials, LightmapAtlas.Pack([]), null).Vertices[0];

        (first.TangentX, first.TangentY, first.TangentZ, first.TangentW).ShouldBe((1f, 0f, 0f, -1f));
    }

    /// <remarks>
    /// By hand from `FUN_1800f83c0`: n (0,0,1), t (0,-1,0): n×t is (1,0,0), so S (1,0,0), and T = S×n = (0,-1,0).
    /// s×t is (0,0,-1), below zero against the plane, so S stands. The shader rebuilds T as cross(n, S)·w, and
    /// cross(n, S) is (0,1,0), so w is -1.
    /// </remarks>
    [Test]
    public void Of_AProjectionThatIsNotNegated_KeepsSAndMirrorsTheBinormal() =>
        SurfaceTangent.Of((0f, 0f, 1f), (4f, 0f, 0f, 0f), (0f, -4f, 0f, 0f), (0f, 0f, 1f)).ShouldBe((1f, 0f, 0f, -1f));

    /// <remarks>
    /// n (0,0,1), t (0,1,0): n×t is (-1,0,0), T = S×n = (0,1,0). s×t is (0,0,1), over zero, so S is negated to (1,0,0).
    /// T is computed before that and stays; cross(n, S) is now (0,1,0), so w is +1.
    /// </remarks>
    [Test]
    public void Of_AProjectionThatIsNegated_NegatesSAndKeepsTheBinormal() =>
        SurfaceTangent.Of((0f, 0f, 1f), (4f, 0f, 0f, 0f), (0f, 4f, 0f, 0f), (0f, 0f, 1f)).ShouldBe((1f, 0f, 0f, 1f));

    /// <remarks>
    /// The test reads the PLANE, unflipped, and not the vertex normal: plane (0,0,-1) turns s×t (0,0,1) below zero, so
    /// the same projection stands where the test above negates. S stays (-1,0,0), and cross(n, S) is (0,-1,0) with T
    /// (0,1,0), so w is -1.
    /// </remarks>
    [Test]
    public void Of_APlaneOppositeTheNormal_DecidesTheNegationByThePlane() =>
        SurfaceTangent.Of((0f, 0f, 1f), (4f, 0f, 0f, 0f), (0f, 4f, 0f, 0f), (0f, 0f, -1f)).ShouldBe((-1f, 0f, 0f, -1f));

    /// <remarks>Only a brush face's plane is tilted; the pane's own frame, by the arithmetic above: unit, and square to n.</remarks>
    [Test]
    public void Of_ATiltedNormal_IsUnitAndPerpendicular()
    {
        (float x, float y, float z, float w) = SurfaceTangent.Of(
            (0f, -0.70710677f, 0.70710677f), (2.8284271f, 2.8284271f, 2.8284271f, 0f), (0f, -2.8284271f, -2.8284271f, 0f), (0f, -0.70710677f, 0.70710677f));

        ((x * x) + (y * y) + (z * z)).ShouldBe(1f, 1e-5f);
        ((y * -0.70710677f) + (z * 0.70710677f)).ShouldBe(0f, 1e-5f);
        Math.Abs(w).ShouldBe(1f);
    }

    /// <summary>A material with no projection has no frame: w zero is what the shader reads as none.</summary>
    [Test]
    public void Of_NoTextureProjection_IsNoFrame() =>
        SurfaceTangent.Of((0f, 0f, 1f), default, default, default).ShouldBe((0f, 0f, 0f, 0f));

    /// <summary>
    /// The output-level half: production's read of the parity map gives the far pane the normal its plane has, and
    /// somewhere on that map vrad has smoothed a corner away from its plane, or the lump would be no different.
    /// </summary>
    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void Read_CpProcessF12_ThePaneAndTheSmoothedCornersCarryTheirCompiledNormals()
    {
        if (GameInstall.Root is not { } tf || !File.Exists(Path.Combine(tf, "maps", "cp_process_f12.bsp")))
        {
            Assert.Ignore("cp_process_f12 or the game is not installed");
            return;
        }

        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(File.ReadAllBytes(Path.Combine(tf, "maps", "cp_process_f12.bsp")));

        BspSurface pane = surfaces.Single(surface => surface.FaceIndex == 4087);

        pane.Normal.Y.ShouldBe(-0.7071f, 1e-3f, "the control: this is the far pane, tilted back");

        foreach (SurfaceVertex corner in pane.Vertices)
        {
            corner.NormalX.ShouldBe(0f, 1e-3f);
            corner.NormalY.ShouldBe(-0.7071f, 1e-3f);
            corner.NormalZ.ShouldBe(0.7071f, 1e-3f);
        }

        int smoothed = surfaces.Sum(surface => surface.Vertices.Count(corner =>
            (corner.NormalX * surface.Normal.X) + (corner.NormalY * surface.Normal.Y) + (corner.NormalZ * surface.Normal.Z) < 0.999f));

        TestContext.Out.WriteLine($"F12 VERTEX NORMALS {smoothed} corners off their plane of {surfaces.Sum(surface => surface.Vertices.Count)}");
        smoothed.ShouldBeGreaterThan(0, "vrad smooths somewhere on this map, or the index walk has drifted to the wrong faces");
    }

    private static BspSurface Face((float X, float Y, float Z) normal, IReadOnlyList<SurfaceVertex> corners) =>
        new(0, corners, 0, default, normal, SurfaceProperties.None, -1);

    /// <summary>An SDK file; skips where there is no checkout (the Windows runner), fails where the checkout lacks the file.</summary>
    private static string Sdk(string path)
    {
        SourceSdk.Require();

        return SourceSdk.Text(path) ?? throw new InvalidOperationException($"{path} is missing from the SDK");
    }
}
