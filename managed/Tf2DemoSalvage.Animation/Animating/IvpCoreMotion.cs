namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>What the rest test <c>FUN_180077220</c> answers — the value a unit's PSI keeps in a core's byte <c>+0x1</c> (B369).</summary>
public enum IvpCoreMotion
{
    /// <summary>Not tested yet; the rest test never answers it.</summary>
    None = 0,

    /// <summary>Moved past its anchor, or only still for a moment.</summary>
    Moving = 1,

    /// <summary>Near its anchor, but not for longer than the environment's rest delay.</summary>
    Still = 2,

    /// <summary>Near its anchor for long enough and not turning — a unit whose every core answers this is frozen.</summary>
    Resting = 3,
}
