using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A brush ENTITY's tool surfaces are dropped, exactly as the world's are (B381).
/// </summary>
/// <remarks>
/// **The owner saw triggers drawing on a process map**, and this is the path that does it:
/// <c>BrushModels.Build</c> gates only on <c>Vertices.Count &lt; 3</c>, where <c>MapWorld</c> drops a
/// surface twice over — once for the not-drawn flags and once for the one tool material that carries
/// none. So every brush entity's faces become drawable geometry, `TOOLSTRIGGER` among them.
///
/// **Measured on `cp_process_final`, and already recorded in `MapWorld`'s own comment:**
///
/// <code>
///   TOOLSINVISIBLEDISPLACEMENT  518 faces, 518 visible, flags Translucent
///   TOOLSSKYBOX                 361 faces,   0 visible, flags Sky, NoLight
///   TOOLSTRIGGER                318 faces,   0 visible, flags Trigger, NoLight
///   TOOLSBLACK                   80 faces,  80 visible, flags None
/// </code>
///
/// Trigger and sky carry flags, so a visibility check excludes them — and the world path has one while
/// the brush-entity path does not. That the bug is not visible on every map is because a networked
/// trigger usually carries `EF_NODRAW` and the runtime hides it, which is a second gate doing the first
/// one's job: per entity, per demo, and absent whenever a trigger is networked without the flag.
///
/// **`TOOLSBLACK` must survive, and it is why the name filter stays narrow.** It carries no flags
/// because the engine really does draw it — the void behind a window, under a grate, inside a vent.
/// `MapWorld`'s own note records that skipping it left 4.8 million square units unpainted and survived
/// four wrong explanations, and the areaportal windows B358 fixed are made of it.
/// </remarks>
public sealed class BrushEntityToolSurfaceConformanceTests
{
    [Test]
    public void Build_ATriggerBrushEntity_ProducesNoGeometry()
    {
        // **The manipulation: one brush entity whose faces carry `SurfaceProperties.Trigger`.** That is
        // what `TOOLSTRIGGER` compiles to, measured on cp_process_final, and the engine draws none of
        // its 318 faces.
        IReadOnlyDictionary<string, PropModels.ModelFrames> built = Build(SurfaceProperties.Trigger);

        built.ShouldNotContainKey(
            "*1", "a trigger's faces carry SurfaceProperties.Trigger and the engine draws none of them");
    }

    [Test]
    public void Build_ANodrawBrushEntity_ProducesNoGeometry()
    {
        // The same rule for the other tool flags a brush entity can carry — a door's unseen back face
        // is `nodraw`, and drawing it puts a solid panel where the engine has nothing.
        Build(SurfaceProperties.NoDraw).ShouldNotContainKey("*1");
        Build(SurfaceProperties.Sky).ShouldNotContainKey("*1");
        Build(SurfaceProperties.Hint).ShouldNotContainKey("*1");
    }

    [Test]
    public void Build_AnOrdinaryBrushEntity_StillProducesGeometry()
    {
        // **The control, and it is the half that matters most.** Eighty-three `func_illusionary` and
        // eighteen `func_door` on cp_process_final are brush entities with real textures; a filter that
        // dropped them would delete most of the map's moving parts, which is a far worse defect than
        // the one being fixed.
        IReadOnlyDictionary<string, PropModels.ModelFrames> built = Build(SurfaceProperties.None);

        built.ShouldContainKey("*1", "a brush entity with an ordinary material is drawn");
    }

    /// <summary>One brush entity of two triangles, whose faces carry the given flags.</summary>
    /// <remarks>
    /// **Two models, because index 0 is the world and `Build` starts at one.** A fixture with a single
    /// model would test nothing: the loop would never reach it.
    /// </remarks>
    private static IReadOnlyDictionary<string, PropModels.ModelFrames> Build(SurfaceProperties flags)
    {
        SurfaceVertex[] corners =
        [
            new(0f, 0f, 0f, 0f, 0f, 0f, 0f),
            new(64f, 0f, 0f, 1f, 0f, 1f, 0f),
            new(64f, 64f, 0f, 1f, 1f, 1f, 1f),
        ];

        BspSurface world = new(
            FaceIndex: 0,
            Vertices: corners,
            MaterialIndex: 0,
            Lightmap: new BspLightmap(0, 0, ReadOnlyMemory<byte>.Empty),
            Normal: (0f, 0f, 1f),
            Flags: SurfaceProperties.None,
            DisplacementIndex: -1);

        BspSurface entity = world with { FaceIndex = 1, Flags = flags };

        List<BspModel> models =
        [
            new BspModel(
                (0f, 0f, 0f), (0f, 0f, 0f), (0f, 0f, 0f), FirstFace: 0, FaceCount: 1, HeadNode: 0),
            new BspModel(
                (0f, 0f, 0f), (0f, 0f, 0f), (0f, 0f, 0f), FirstFace: 1, FaceCount: 1, HeadNode: 0),
        ];

        return BrushModels.Build(
            models,
            [world, entity],
            LightmapAtlas.Pack([new BspLightmap(0, 0, ReadOnlyMemory<byte>.Empty)]));
    }
}
