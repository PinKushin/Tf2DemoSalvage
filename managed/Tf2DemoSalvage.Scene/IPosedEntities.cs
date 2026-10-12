using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What an effect asks of the entities the last pass posed: where an attachment is, whether a ray meets a hitbox, and
/// whether the entity was posed at all (D214).
/// </summary>
/// <remarks>
/// **Narrow on purpose.** <see cref="EntityModelSet"/> answers a hundred questions; the particle effects, the bullet
/// traces and the decals that left the view ask these ten, and a test can answer ten without loading a model.
/// </remarks>
public interface IPosedEntities
{
    /// <summary>Where a named attachment of an entity's model is in the world, or null.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="name">The attachment's name.</param>
    /// <returns>The position, or null when the entity is not posed or its model has no such attachment.</returns>
    public (float X, float Y, float Z)? AttachmentPosition(int entity, string name);

    /// <summary>An attachment by its one-based number, as a control point with the attachment's own axes.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="number">The attachment's number.</param>
    /// <returns>The point, or null.</returns>
    public ParticleControlPoint? AttachmentPoint(int entity, int number);

    /// <summary>An attachment by name, as a control point with the attachment's own axes.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="name">The attachment's name.</param>
    /// <returns>The point, or null.</returns>
    public ParticleControlPoint? AttachmentPoint(int entity, string name);

    /// <summary>How far along a ray an entity's posed hitboxes let it get.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="start">The ray's start.</param>
    /// <param name="delta">The ray's extent.</param>
    /// <param name="mask">The trace's contents mask.</param>
    /// <returns>The fraction, or null when it is not hit, not posed here, or has no hitboxes.</returns>
    public float? TraceHitboxes(int entity, Vector3 start, Vector3 delta, int mask);

    /// <summary>An entity's hitboxes as the last pass posed them, and the origin it had then.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The boxes and that origin, or null when it is not posed here or has none.</returns>
    public (IReadOnlyList<ModelHitbox> Boxes, Vector3 PosedAt)? PosedHitboxes(int entity);

    /// <summary>Whether a pass has posed this entity.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>True when it has.</returns>
    public bool IsPosed(int entity);

    /// <summary>What a decal is skinned against: an entity's model, its bones as posed, and the body its model pass drew.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The pose, or null when it is not posed here.</returns>
    public (string Model, IReadOnlyList<float[]> Bones, int Body, IReadOnlyList<(int Base, int Count)>? Parts)? SkinningOf(int entity);

    /// <summary>How far a point is from one of an entity's hitboxes, for a log that says why a ray missed.</summary>
    /// <param name="entity">The entity.</param>
    /// <param name="box">The hitbox's index.</param>
    /// <param name="point">The point, in the world.</param>
    /// <returns>The distance outside the box and the point in its bone's frame, or null when it is not posed here.</returns>
    public (float Outside, Vector3 FromCentre)? HitboxGap(int entity, int box, Vector3 point);

    /// <summary>The frame of the sequence an entity was posed at, for the same log.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <returns>The frame, or null.</returns>
    public (int Sequence, int Frame, float Fraction)? FrameOf(int entityIndex);

    /// <summary>The duck-jump offset an entity's pose was built with, for the same log.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <returns>The offset, or null.</returns>
    public float? DuckJumpOffsetOf(int entityIndex);
}
