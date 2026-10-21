using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>Bone-pose storage that one skeleton owns and reuses on every pose it builds.</summary>
/// <remarks>
/// **The engine allocates nothing to pose a model** (finding 88, B567). <c>CBoneSetupMemoryPool</c>
/// (<c>bone_setup.cpp:49-79</c>) hands out <c>MAXSTUDIOBONES</c>-sized arrays from a free list and takes them back when
/// the blend is done, so a frame's poses reuse the same storage. This project built every intermediate pose as a fresh
/// array or list: 1.7 GB per 20 s of f12 at tick 30000, a third of the render thread's allocation.
///
/// **Owned per skeleton, not per thread or per model.** A model is shared by the render thread and the corpse worker
/// (D181), so storage on the model would be written by both; a skeleton is posed by one thread, and a skeleton that
/// poses another (a bone merge) holds its own. Every list here is overwritten by the next call that uses it, so a
/// pose read out of a workspace is valid until the next pose is asked of the same workspace.
///
/// **The lists are internal and concrete on purpose**: a pose is written in place through them, which a wrapper type
/// would only slow down, and CA1002's advice is for public surfaces.
/// </remarks>
public sealed class StudioPoseWorkspace
{
    /// <summary>A grid corner's frame as read, renumbered into the skeleton; one per corner.</summary>
    internal List<StudioBonePose>[] Frame { get; } = [[], [], []];

    /// <summary>A corner's next frame, for the blend between the two.</summary>
    internal List<StudioBonePose>[] Next { get; } = [[], [], []];

    /// <summary>A corner's frame blended toward its next, dense.</summary>
    internal List<StudioBonePose>[] Between { get; } = [[], [], []];

    /// <summary>The grid's two blends, dense: the first pair's, and the last corner's onto it.</summary>
    internal List<StudioBonePose>[] Grid { get; } = [[], []];

    /// <summary>One animation's frame before it is renumbered into its skeleton.</summary>
    internal List<StudioBonePose> Raw { get; } = [];

    /// <summary>The second pose of a blend, expanded; never held past the blend.</summary>
    internal List<StudioBonePose> Scratch { get; } = [];

    /// <summary>One animation's bone chain while its frame is read.</summary>
    internal List<(int Bone, int Flags, int At)> Chain { get; } = [];
}
