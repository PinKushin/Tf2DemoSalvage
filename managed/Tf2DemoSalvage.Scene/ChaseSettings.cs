namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The director's chase-camera parameters, as the <c>hltv_chase</c> event carries them.
/// </summary>
/// <param name="Back">
/// <c>m_flDistance</c>: how far behind the target the camera sits, in units.
/// </param>
/// <param name="Phi">
/// <c>m_flPhi</c>, which becomes the PITCH of <c>angleOffset</c> — the camera is raised and looks
/// down on the target by this much.
/// </param>
/// <param name="Theta">
/// <c>m_flTheta</c>, the YAW of the same offset: the camera swings round the target.
/// </param>
/// <param name="Rise">
/// <c>m_flOffset</c>, which raises the point LOOKED AT rather than the camera itself — see
/// <see cref="ChaseCamera"/> for where the engine applies it, which is later than it reads.
/// </param>
/// <remarks>
/// **A demo can set every one of these, and this project ignored them all**, taking
/// <c>C_HLTVCamera::Reset</c>'s defaults for granted. The director sets them per shot
/// (<c>hltvcamera.cpp:790</c>):
///
/// <code>
///   m_iTraget2    = event->GetInt( "target2" );
///   m_flDistance  = event->GetFloat( "distance", m_flDistance );
///   m_flOffset    = event->GetFloat( "offset", m_flOffset );
///   m_flTheta     = event->GetFloat( "theta", m_flTheta );
///   m_flPhi       = event->GetFloat( "phi", m_flPhi );
/// </code>
///
/// **Each falls back to its PREVIOUS value, not to a constant**, which is why these are state
/// carried between shots rather than arguments computed per frame. An event that names only
/// `distance` leaves the angles where the last one put them.
///
/// **Named `Back` and `Rise` rather than `Distance` and `Offset`.** Valve's names are unhelpfully
/// generic in a type that holds four numbers — "offset" of what, from what — and `Distance` would
/// shadow <see cref="ChaseCamera.Distance"/>, which is the DEFAULT for this field and a different
/// thing. The engine's names are recorded above so the mapping is never in doubt.
/// </remarks>
public readonly record struct ChaseSettings(
    float Back = ChaseCamera.Distance,
    float Phi = 0f,
    float Theta = 0f,
    float Rise = 0f);
