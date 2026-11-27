namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What one view last knew of an entity's bone setup, which decides its cycle history's resets (B878).</summary>
/// <param name="SetUpAt">The tick its bones were last set up; updates received after it have not been seen by a setup.</param>
/// <param name="ParityThen">
/// <c>m_nPrevNewSequenceParity</c>: the parity its newest update stated at that setup, which
/// <c>MaintainSequenceTransitions</c> stores (<c>c_baseanimating.cpp:1835</c>); −1 before any, the constructor's (<c>:716</c>).
/// </param>
/// <param name="AnchoredAt">
/// The arrival of the newest update that reset the cycle history before that setup — an anchor still in force until a
/// real reset supersedes it or the bracket leaves it behind — or null.
/// </param>
/// <remarks>
/// **A view-time fact.** The engine sets bones up on demand — drawing, an attachment, a hitbox, a merge
/// (<c>docs/RISKS.md</c> B878) — so which updates reset the cycle depends on what the camera saw. A timeline is shared, so
/// the state lives with the view that drew it (<see cref="BoneSetups"/>), never on the track.
/// </remarks>
public readonly record struct BoneSetup(int SetUpAt, int ParityThen, int? AnchoredAt)
{
    /// <summary>An entity no setup has seen: every update's parity differs from the constructor's −1.</summary>
    public static BoneSetup Never { get; } = new(int.MinValue, -1, null);
}
