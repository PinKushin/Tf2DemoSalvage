namespace Tf2DemoSalvage.Core.Scene;

/// <summary>What a <c>CRopeKeyframe</c> says about itself — <c>DT_RopeKeyframe</c> as <c>C_RopeKeyframe</c> holds it.</summary>
/// <param name="StartPoint"><c>m_hStartPoint</c>, a raw handle; usually the rope entity itself.</param>
/// <param name="EndPoint"><c>m_hEndPoint</c>, a raw handle; the next keyframe in a chain.</param>
/// <param name="StartAttachment"><c>m_iStartAttachment</c>; 0 means the entity's <c>WorldSpaceCenter</c>.</param>
/// <param name="EndAttachment"><c>m_iEndAttachment</c>.</param>
/// <param name="Slack"><c>m_Slack</c>, added to the length before <c>ROPESLACK_FUDGEFACTOR</c>'s −100.</param>
/// <param name="Length"><c>m_RopeLength</c>.</param>
/// <param name="LockedPoints"><c>m_fLockedPoints</c>, the <c>ROPE_LOCK_*</c> bits of `rope_shared.h:55-61`.</param>
/// <param name="Flags"><c>m_RopeFlags</c>, the <c>ROPE_*</c> bits of `rope_shared.h:25-39`.</param>
/// <param name="Segments"><c>m_nSegments</c>, the node count before the client clamps it to 2..10.</param>
/// <param name="ConstrainBetweenEndpoints"><c>m_bConstrainBetweenEndpoints</c>.</param>
/// <param name="Subdiv"><c>m_Subdiv</c>; 255 is the client constructor's "use <c>rope_subdiv</c>".</param>
/// <param name="TextureScale"><c>m_TextureScale</c>, inches per texture pixel; the constructor's 4.</param>
/// <param name="Width"><c>m_Width</c>.</param>
/// <param name="ScrollSpeed"><c>m_flScrollSpeed</c>, received and never read by the published client.</param>
/// <remarks>
/// **A NOBASE table** (`c_rope.cpp:42-64`), like <c>DT_Beam</c>: it declares its own origin and move parent and no model
/// index at all. A rope's material arrives as <c>m_iRopeMaterialModelIndex</c>, a <c>modelprecache</c> index, which is
/// what the timeline carries as the rope's model path. Everything else about a rope — its nodes, its sag, its sway — is
/// simulated by the client and appears nowhere on the wire.
/// </remarks>
public sealed record SceneRope(
    int StartPoint,
    int EndPoint,
    int StartAttachment,
    int EndAttachment,
    int Slack,
    int Length,
    int LockedPoints,
    int Flags,
    int Segments,
    bool ConstrainBetweenEndpoints,
    int Subdiv,
    float TextureScale,
    float Width,
    float ScrollSpeed);
