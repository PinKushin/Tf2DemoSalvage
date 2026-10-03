using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The fog the view draws through, chosen as the client chooses it (B139).</summary>
/// <param name="World">
/// The main view's: the controller the local player's <c>m_PlayerFog.m_hCtrl</c> names
/// (<c>C_BasePlayer::UpdateFogController</c>, c_baseplayer.cpp:2802), or null — the engine sets
/// <c>m_CurrentFog.enable = false</c> when there is no controller, however many the map has.
/// </param>
/// <param name="Sky">The 3D skybox's: the same player's <c>m_skybox3d.fog</c> (viewrender.cpp:4799).</param>
public readonly record struct ViewFog(SceneFog? World, SceneFog? Sky)
{
    /// <summary>Reads both from the entities as they stand.</summary>
    /// <param name="entities">The entity table after a packet.</param>
    /// <returns>The view's fog; both null when no entity carries the local player's fog handle.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="entities"/> is null.</exception>
    /// <remarks>
    /// **The local player is whichever entity carries the handle**, because <c>DT_Local</c> is sent to
    /// its owner alone — the same rule the soundscape sample uses. A SourceTV recording carries it on
    /// the SourceTV client's own player (docs/memory/sourcetv-has-a-local-player.md).
    /// </remarks>
    public static ViewFog From(EntityStateTable entities)
    {
        System.ArgumentNullException.ThrowIfNull(entities);

        foreach (EntityState entity in entities.All)
        {
            if (entity.FogControllerHandle() is not { } handle)
            {
                continue;
            }

            SceneFog? world = entities.Resolve(handle) is { } slot &&
                              entities.TryGet(slot, out EntityState? controller)
                ? controller.Fog()
                : null;

            return new ViewFog(world, entity.SkyboxFog());
        }

        // **No entity carries the handle at all**, which is every point-of-view recording in the
        // corpus (B452): the player's DT_Local fog and skybox fields never reach the decoded state.
        // The server gives every player `GetMasterFogController()` — with none flagged master,
        // "the first fog controller found" (fogcontroller.cpp:363-383) — so that rule stands in.
        // ponytail: lowest index for FindEntityByClassname's order; a master-flagged later
        // controller (the flag is not networked) would differ. The sky fog stays unknown: none.
        EntityState? first = entities.OfClass(ControllerClass).MinBy(controller => controller.EntityIndex);

        return new ViewFog(first?.Fog(), null);
    }

    /// <summary>The fog controller's networked class.</summary>
    private const string ControllerClass = "CFogController";
}
