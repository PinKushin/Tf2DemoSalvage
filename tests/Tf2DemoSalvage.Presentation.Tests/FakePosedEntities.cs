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

    /// <summary>The mask the last hitbox trace was asked with.</summary>
    public int? LastMask { get; private set; }

    public (float X, float Y, float Z)? AttachmentPosition(int entity, string name) =>
        Named.TryGetValue((entity, name), out ParticleControlPoint point) ? (point.At.X, point.At.Y, point.At.Z) : null;

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
}
