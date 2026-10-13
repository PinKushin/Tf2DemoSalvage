using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Posed entities a test fills by hand: what is posed, where its attachments are, what a ray meets.</summary>
internal sealed class FakePosedEntities : IPosedEntities
{
    /// <summary>The entities a pass posed.</summary>
    public HashSet<int> Posed { get; } = [];

    /// <summary>Attachments by entity and name.</summary>
    public Dictionary<(int Entity, string Name), ParticleControlPoint> Named { get; } = [];

    /// <summary>Attachments by entity and one-based number.</summary>
    public Dictionary<(int Entity, int Number), ParticleControlPoint> Numbered { get; } = [];

    /// <summary>Hitbox trace fractions by entity.</summary>
    public Dictionary<int, float> Fractions { get; } = [];

    /// <summary>Posed hitboxes by entity.</summary>
    public Dictionary<int, (IReadOnlyList<ModelHitbox> Boxes, Vector3 PosedAt)> Hitboxes { get; } = [];

    /// <summary>What a decal is skinned against, by entity.</summary>
    public Dictionary<int, (string Model, IReadOnlyList<float[]> Bones, int Body, IReadOnlyList<(int Base, int Count)>? Parts)> Skinning { get; } = [];

    /// <summary>The mask the last hitbox trace was asked with.</summary>
    public int? LastMask { get; private set; }

    public (float X, float Y, float Z)? AttachmentPosition(int entity, string name) =>
        Named.TryGetValue((entity, name), out ParticleControlPoint point) ? (point.At.X, point.At.Y, point.At.Z) : null;

    public (float X, float Y, float Z)? AttachmentPosition(int entity, int number) =>
        Numbered.TryGetValue((entity, number), out ParticleControlPoint point) ? (point.At.X, point.At.Y, point.At.Z) : null;

    public ParticleControlPoint? AttachmentPoint(int entity, int number) =>
        Numbered.TryGetValue((entity, number), out ParticleControlPoint point) ? point : null;

    public ParticleControlPoint? AttachmentPoint(int entity, string name) =>
        Named.TryGetValue((entity, name), out ParticleControlPoint point) ? point : null;

    public float? TraceHitboxes(int entity, Vector3 start, Vector3 delta, int mask)
    {
        LastMask = mask;

        return Fractions.TryGetValue(entity, out float fraction) ? fraction : null;
    }

    public (IReadOnlyList<ModelHitbox> Boxes, Vector3 PosedAt)? PosedHitboxes(int entity) =>
        Hitboxes.TryGetValue(entity, out (IReadOnlyList<ModelHitbox> Boxes, Vector3 PosedAt) posed) ? posed : null;

    public bool IsPosed(int entity) => Posed.Contains(entity);

    public (string Model, IReadOnlyList<float[]> Bones, int Body, IReadOnlyList<(int Base, int Count)>? Parts)? SkinningOf(int entity) =>
        Skinning.TryGetValue(entity, out (string, IReadOnlyList<float[]>, int, IReadOnlyList<(int Base, int Count)>?) pose) ? pose : null;

    public (float Outside, Vector3 FromCentre)? HitboxGap(int entity, int box, Vector3 point) =>
        Posed.Contains(entity) ? (7.5f, new Vector3(1f, 2f, 3f)) : null;

    public (int Sequence, int Frame, float Fraction)? FrameOf(int entityIndex) => null;

    public float? DuckJumpOffsetOf(int entityIndex) => null;

    /// <summary>The hitbox point nearest a viewer, by entity and attachment number.</summary>
    public Dictionary<(int Entity, int Number), Vector3> Nearest { get; } = [];

    /// <summary>The viewer the last nearest-point question was asked from.</summary>
    public Vector3? LastViewer { get; private set; }

    public Vector3? HitboxPointNearest(int entity, int number, Vector3 viewer)
    {
        LastViewer = viewer;

        return Nearest.TryGetValue((entity, number), out Vector3 point) ? point : null;
    }
}
