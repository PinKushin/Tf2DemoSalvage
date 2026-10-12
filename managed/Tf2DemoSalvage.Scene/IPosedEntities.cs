using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What an effect asks of the entities the last pass posed: where an attachment is, whether a ray meets a hitbox, and
/// whether the entity was posed at all (D214).
/// </summary>
/// <remarks>
/// **Narrow on purpose.** <see cref="EntityModelSet"/> answers a hundred questions; the particle effects and the bullet
/// traces that left the view ask these six, and a test can answer six without loading a model.
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
}
