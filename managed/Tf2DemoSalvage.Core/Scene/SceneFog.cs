namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The atmosphere at one moment, as the demo recorded it.</summary>
/// <param name="Start">Distance at which fog begins, in world units.</param>
/// <param name="End">Distance at which it reaches full strength.</param>
/// <param name="Red">Fog colour, 0 to 1.</param>
/// <param name="Green">Fog colour, 0 to 1.</param>
/// <param name="Blue">Fog colour, 0 to 1.</param>
/// <param name="MaxDensity">
/// The most fog any distance may reach, 0 to 1. **One means no cap**, which is also what an absent
/// value means — a controller that does not send it is not asking for clear air.
/// </param>
/// <remarks>
/// **Fog is the first thing this project draws whose inputs come from the DEMO rather than from the
/// map.** <c>CFogController</c> networks these per tick (<c>fogcontroller.cpp:78</c>), so fog that
/// changes as a round's triggers fire is recorded and replayable — and a viewer that read the map's
/// entity lump instead would show the starting atmosphere for the whole match.
///
/// Measured on the committed corpus: 3 of 10 demos carry the class at all, the 2009 badlands POV and
/// both 2011 koth_viaduct recordings. A demo without one draws no fog rather than a default, which
/// is the honest reading — the alternative invents weather.
///
/// **Not carried here: the lerp targets.** <c>m_fog.startLerpTo</c>, <c>endLerpTo</c>,
/// <c>colorPrimaryLerpTo</c>, <c>lerptime</c> and <c>duration</c> let the server animate a fog
/// change over time, and the client interpolates between the two sets. This takes the current values
/// only, so a fog transition arrives as a step at the tick the server finished it rather than as a
/// fade. Recorded as a known limit rather than discovered from a screenshot later.
/// </remarks>
public readonly record struct SceneFog(
    float Start,
    float End,
    float Red,
    float Green,
    float Blue,
    float MaxDensity);
