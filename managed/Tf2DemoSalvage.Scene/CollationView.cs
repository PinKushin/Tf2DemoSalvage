using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What the world pass collated by, for something else the same views must collate — the particle effects (finding 84, D1).
/// </summary>
/// <param name="Frustum">The main view's volume.</param>
/// <param name="Views">The main and sky views' leaf lists, and the sky's volume.</param>
/// <param name="Tree">The map's leaf tree; null for none, when everything is in one leaf.</param>
/// <remarks>
/// **The engine has one leaf system for every renderable, a particle effect among them** (`CNewParticleEffect` is a
/// renderable the leaf system collates, `clientleafsystem.cpp:1574-1716`). Ours keeps the entities' in the world pass, so a
/// second collection collates with the same views rather than with a second idea of what is visible.
/// </remarks>
public sealed record CollationView(ViewFrustum Frustum, RenderableViews Views, BspLeafTree? Tree);
