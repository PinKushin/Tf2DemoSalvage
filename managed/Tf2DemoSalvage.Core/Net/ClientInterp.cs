using System;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// The viewer's own interpolation settings — `cl_interp`, `cl_interp_ratio`, `cl_updaterate` — which decide how far
/// behind the present entities are drawn and how late temp entities fire. They belong to whoever WATCHES the demo, not
/// to the recording: the owner's config runs 0 / 1 / 66, which is one update where TF2's defaults are a tenth of a
/// second, and a hardcoded default drew every effect six ticks late for him.
/// </summary>
/// <param name="Interp">`cl_interp`.</param>
/// <param name="Ratio">`cl_interp_ratio`.</param>
/// <param name="UpdateRate">`cl_updaterate`.</param>
public sealed record ClientInterp(float Interp = 0.1f, float Ratio = 2f, float UpdateRate = 20f)
{
    /// <summary>`GetClientInterpAmount()`, in seconds, under the recording server's bounds.</summary>
    /// <param name="server">The recording's replicated settings: `sv_client_min_interp_ratio` and `_max_`.</param>
    /// <returns>The interpolation amount.</returns>
    /// <remarks>
    /// <code>
    /// cl_interp       → max( cl_interp, sv_client_min_interp_ratio / cl_updaterate )   // unless the minimum is -1
    /// cl_interp_ratio → clamp( cl_interp_ratio, min, max )                            // unless the minimum is -1
    /// amount          = max( cl_interp, cl_interp_ratio / cl_updaterate )
    /// </code>
    /// `cl_updaterate` itself is first clamped to `sv_minupdaterate` / `sv_maxupdaterate` (`CBoundedCvar_UpdateRate`).
    /// </remarks>
    public double Amount(ServerConVars server)
    {
        ArgumentNullException.ThrowIfNull(server);

        float minimum = server.Number("sv_client_min_interp_ratio");
        float maximum = server.Number("sv_client_max_interp_ratio");
        float rate = Math.Clamp(UpdateRate, server.Number("sv_minupdaterate"), server.Number("sv_maxupdaterate"));
        // `pMin->GetFloat() != -1`: -1 is the server's "leave it to the client" sentinel.
        bool bounded = MathF.Abs(minimum + 1f) > float.Epsilon;

        float interp = bounded ? MathF.Max(Interp, minimum / rate) : Interp;
        float ratio = bounded ? Math.Clamp(Ratio, minimum, maximum) : Ratio;

        return MathF.Max(interp, ratio / rate);
    }
}
