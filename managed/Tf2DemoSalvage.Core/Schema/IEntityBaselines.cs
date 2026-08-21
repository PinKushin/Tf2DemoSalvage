using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Answers what an entity's state is, as opposed to what its snapshot carried.
/// </summary>
/// <remarks>
/// **These are two different questions and the difference is invisible.** A snapshot's property
/// list is wire-faithful: exactly the bits the server sent, which is what a re-encoder must
/// reproduce. An entity's state is that list overlaid on its class's instance baseline, because
/// an entity entering the visible set is a delta against that baseline and omits everything equal
/// to it — the engine merges the two in <c>CL_CopyNewEntity</c> before the entity exists at all.
///
/// **Asking the wrong one returns a plausible answer rather than an error**, which is how B132
/// survived: <see cref="Scene.EntityStateTable"/> read the wire list, so every entity whose whole
/// state came from its baseline reached the accumulated world holding nothing. It kept its class
/// name, because the class id travels on the update itself, so the table reported a
/// <c>CFogController</c> that existed on 3,762 consecutive packets and knew nothing about fog.
/// Nineteen of one demo's 195 entities were empty that way and no test in the repository could
/// see it.
///
/// **It exists as an interface so the accumulator has to be given one.** The state table cannot
/// resolve a baseline itself — only the decoder holds them — and an optional dependency would let
/// a caller reconstruct the defect by leaving it out. <see cref="EntityBaselines.None"/> is for
/// callers that genuinely have no schema, and says so at the call site.
/// </remarks>
public interface IEntityBaselines
{
    /// <summary>An entity's full state: its class baseline overlaid with what the snapshot sent.</summary>
    /// <param name="entity">The entity as a snapshot described it.</param>
    /// <returns>
    /// The merged properties for an entering entity; the entity's own properties unchanged for
    /// every other update type, and whenever its class has no baseline.
    /// </returns>
    public IReadOnlyList<DecodedProperty> EffectiveProperties(DecodedEntity entity);
}

/// <summary>Baseline sources for callers that have none.</summary>
public static class EntityBaselines
{
    /// <summary>
    /// A source that knows no baselines, so an entity's state is exactly what its snapshot said.
    /// </summary>
    /// <remarks>
    /// **Named rather than defaulted.** A caller with no schema — a hand-built fixture, a test
    /// that supplies whole entities directly — is in a genuinely different situation from one that
    /// forgot to pass a decoder, and the two produce identical results. Writing it out is what
    /// tells them apart when the next empty entity turns up.
    /// </remarks>
    public static IEntityBaselines None { get; } = new NoBaselines();

    private sealed class NoBaselines : IEntityBaselines
    {
        public IReadOnlyList<DecodedProperty> EffectiveProperties(DecodedEntity entity)
        {
            System.ArgumentNullException.ThrowIfNull(entity);

            return entity.Properties;
        }
    }
}
