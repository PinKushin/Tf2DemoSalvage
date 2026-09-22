namespace Tf2DemoSalvage.Scene;

/// <summary>A sentry gun's muzzle flash — `TF_3rdPersonMuzzleFlashCallback_SentryGun` (B415).</summary>
/// <remarks>
/// <code>
/// // tf_fx_muzzleflash.cpp:157
/// pEnt = data.GetEntity();
/// if ( pEnt &amp;&amp; !pEnt->IsDormant() )
///     switch( data.m_fFlags ) { case 1: default: "muzzle_sentry"; case 2: case 3: "muzzle_sentry2"; }
///     DispatchParticleEffect( name, PATTACH_POINT_FOLLOW, pEnt, data.m_nAttachmentIndex );
/// </code>
/// The server fills the flags with the sentry's upgrade level and the attachment with the barrel that fired
/// (`tf_obj_sentrygun.cpp:1562`), so the flash follows that barrel for its whole life.
/// </remarks>
public static class SentryMuzzleFlash
{
    /// <summary>The dispatch's name in the <c>EffectDispatch</c> table.</summary>
    public const string EffectName = "TF_3rdPersonMuzzleFlash_SentryGun";

    /// <summary>A level 1 sentry's flash, and the default.</summary>
    public const string Level1 = "muzzle_sentry";

    /// <summary>A level 2 or 3 sentry's flash.</summary>
    public const string Level2 = "muzzle_sentry2";

    /// <summary>Every system the flash can start, for loading with the map.</summary>
    public static readonly string[] Systems = [Level1, Level2];

    /// <summary>The system for a sentry's upgrade level, as the dispatch's flags carry it.</summary>
    /// <param name="upgradeLevel">`m_fFlags`.</param>
    /// <returns>The particle system's name.</returns>
    public static string System(int upgradeLevel) => upgradeLevel is 2 or 3 ? Level2 : Level1;
}
