namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Whether a frame's pose decides its econ effects, and whether that decision is a landing (B69 g): its own
/// <see cref="FrameSkip"/> cursor, the skip held until a decision runs, and none on a skip's frames, a seek's replay or a
/// level with no particle systems.
/// </summary>
/// <remarks>
/// The engine re-decides in <c>ProcessOnDataChangedEvents</c> and <c>SimulateEntities</c> (cdll_client_int.cpp:2193-2239);
/// a skip's frames and a replay draw no effects (B708), and the landing decides afresh (D221) — told so whichever reader
/// took the skip first, which is why it keeps its own cursor.
/// </remarks>
internal sealed class EconDecisionGate
{
    private (int Generation, int Skips) _taken;
    private bool _landingPending;

    /// <summary>One pose's answer.</summary>
    /// <param name="skip">The transport's last move, or null.</param>
    /// <param name="skipping">A skip is under way.</param>
    /// <param name="replaying">A seek's decal replay is posing.</param>
    /// <param name="hasSystems">The level has particle systems.</param>
    /// <returns>Null to decide nothing; else whether the decision is a landing.</returns>
    public bool? Next(FrameSkip? skip, bool skipping, bool replaying, bool hasSystems)
    {
        _landingPending |= skip?.Take(ref _taken) == true;

        if (skipping || replaying || !hasSystems)
        {
            return null;
        }

        bool landed = _landingPending;
        _landingPending = false;

        return landed;
    }
}
