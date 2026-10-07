using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Scene.Prediction;

/// <summary>A predicted landing: <c>PlayStepSound( origin, m_pSurfaceData, fvol, true )</c> (<c>gamemovement.cpp:3995</c>, B172).</summary>
/// <param name="Sequence">The command that landed.</param>
/// <param name="Volume"><c>fvol</c>.</param>
/// <param name="Origin">The player's origin after the move, <c>mv->GetAbsOrigin()</c>.</param>
/// <param name="Surface"><c>m_pSurfaceData</c>, the ground landed on; null for none.</param>
public readonly record struct PredictedLanding(int Sequence, float Volume, Vector3 Origin, VphysicsSurface? Surface);
