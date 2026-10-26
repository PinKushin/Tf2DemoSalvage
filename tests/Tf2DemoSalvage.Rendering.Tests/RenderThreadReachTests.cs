using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// **The queued render thread never asks the scene for a pose** (batch-33 review 2): its jobs are `Device3D` draws, and
/// `EntityModelSet`'s per-frame scenes (`BeginScenes`, then each pass) are written on the game thread by `MomentScene.Pose`.
/// A pose query from a draw job would read them while the next frame rewrites them.
/// </summary>
/// <remarks>
/// Held structurally: the render assembly references no `SkinningOf`. Its only callers are `DecalEffects.Step` and
/// `DecalEffects.Replay` in Presentation, which the render assembly cannot reference, run from
/// `ViewerPresenter.UploadCameraOver` after `PoseOver` and from the seek's replay after `QueuedRenderThread.Lock`.
/// </remarks>
public sealed class RenderThreadReachTests
{
    [Test]
    public void RenderAssembly_MemberReferences_NameNoPoseQuery()
    {
        using PEReader file = new(File.OpenRead(typeof(Device3D).Assembly.Location));
        MetadataReader reader = file.GetMetadataReader();

        string[] names =
        [
            .. reader.MemberReferences
                .Select(handle => reader.GetMemberReference(handle))
                .Where(member => member.Parent.Kind == HandleKind.TypeReference &&
                    reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name) is "EntityModelSet" or "IPosedEntities")
                .Select(member => reader.GetString(member.Name)),
        ];

        names.ShouldNotBeEmpty("the control: the scan sees the render assembly's calls into EntityModelSet (UploadModels reads it)");
        names.ShouldNotContain("SkinningOf");
    }
}
