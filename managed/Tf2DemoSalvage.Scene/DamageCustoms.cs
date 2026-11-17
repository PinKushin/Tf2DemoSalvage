namespace Tf2DemoSalvage.Scene;

/// <summary>The <c>ETFDmgCustom</c> values a corpse's <c>m_iDamageCustom</c> is read against.</summary>
/// <remarks>
/// Counted off `ETFDmgCustom`'s enumerators (`tf_shareddefs.h:1181`), comments excluded — taking line offsets instead is
/// wrong for every value after the first comment in the block. One home, so the death animation and the decapitation
/// cannot disagree about which kill is which.
/// </remarks>
internal static class DamageCustoms
{
    /// <summary><c>TF_DMG_CUSTOM_HEADSHOT</c>.</summary>
    public const int Headshot = 1;

    /// <summary><c>TF_DMG_CUSTOM_BACKSTAB</c>.</summary>
    public const int Backstab = 2;

    /// <summary><c>TF_DMG_CUSTOM_DECAPITATION</c>.</summary>
    public const int Decapitation = 20;

    /// <summary><c>TF_DMG_CUSTOM_TAUNTATK_BARBARIAN_SWING</c>.</summary>
    public const int BarbarianSwing = 24;

    /// <summary><c>TF_DMG_CUSTOM_TAUNTATK_ENGINEER_GUITAR_SMASH</c>.</summary>
    public const int GuitarSmash = 33;

    /// <summary><c>TF_DMG_CUSTOM_DECAPITATION_BOSS</c>.</summary>
    public const int DecapitationBoss = 41;

    /// <summary><c>TF_DMG_CUSTOM_HEADSHOT_DECAPITATION</c>.</summary>
    public const int HeadshotDecapitation = 51;

    /// <summary><c>TF_DMG_CUSTOM_MERASMUS_DECAPITATION</c>.</summary>
    public const int MerasmusDecapitation = 60;

    /// <summary><c>TF_DMG_CUSTOM_TAUNTATK_ALLCLASS_GUITAR_RIFF</c>.</summary>
    public const int GuitarRiff = 62;
}
