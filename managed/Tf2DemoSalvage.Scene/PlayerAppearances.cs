using System;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where the players' appearance comes from, asked once per moment.</summary>
/// <remarks>
/// **The fourth source of this shape**, after <see cref="IEyeSource"/>, <see cref="IViewmodelSource"/>
/// and <see cref="IMomentSource"/> — and the only one that cannot be handed a finished answer when a
/// demo opens, because the thing it needs does not exist yet.
/// </remarks>
public interface IAppearanceSource
{
    /// <summary>The appearance to use now, built the first time it can be.</summary>
    /// <param name="current">What the caller is using; returned unchanged once it is real.</param>
    /// <returns>The appearance to use, which the caller must assign.</returns>
    public IPlayerAppearance Ensure(IPlayerAppearance current);
}

/// <summary>Builds the player appearance once both halves of it exist.</summary>
/// <param name="log">Where the resolved weapon-role table is reported.</param>
/// <remarks>
/// **This was `MainForm.EnsureWeaponRoles` and the two fields it read** (B188, D90): one line in the
/// window, called from `ShowMoment` on every frame, reaching for `_timeline` and `_game`.
///
/// **Two settable properties, because there are genuinely two lifecycles.** The timeline arrives
/// when a demo opens and changes with each one; the install is located on the first map read and
/// never again. Nothing sees both moments, which is exactly why this ended up in the window — the
/// form was the only object that happened to hold both. A holder with two owners is the honest shape
/// for that, rather than a constructor argument that would have to be one or the other.
///
/// **Lazy on purpose, and a constraint rather than an optimisation.** The archives open AFTER a demo
/// is applied, so building at load time reads nothing — the first attempt did exactly that and
/// produced an empty table in silence. Asking per moment costs nothing once there is an answer.
///
/// **A missed wiring is caught at the third level and not here.** Both properties left unset gives
/// <see cref="DemoAppearance.None"/> for ever, which `MomentScene` reports as "no player appearance"
/// and `WiringUiTests` asserts against a running viewer — the arrangement that exists because the
/// side-effect version of this shipped a regression with 620 tests green (B193).
/// </remarks>
public sealed class PlayerAppearances(ILogger log) : IAppearanceSource
{
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>The demo being shown, set when one is opened. Null when none is.</summary>
    public DemoTimeline? Timeline { get; set; }

    /// <summary>What the install provides, set when it is located. Null before that.</summary>
    public GameContent? Game { get; set; }

    /// <inheritdoc />
    public IPlayerAppearance Ensure(IPlayerAppearance current) =>
        DemoAppearance.Ensure(current, Timeline, Game, _log);
}
