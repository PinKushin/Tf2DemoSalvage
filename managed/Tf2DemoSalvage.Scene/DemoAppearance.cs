using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the players in a demo look like, resolved once the install is open.</summary>
/// <remarks>
/// **This was <c>MainForm.EnsureWeaponRoles</c>** (B188, D90): walking a timeline, reading an
/// archive and building an appearance. None of it is window work and none of it had a test.
///
/// **It is the member that already caused a shipped regression, which is why its shape changed.**
/// When `AddViewmodel` moved out of the form, the call to `EnsureWeaponRoles` went with it by
/// accident: every weapon suffix answered null and every player animated with the generic primary
/// pose — the right weapon, the wrong hold, on everybody. An analyzer caught it, but only because
/// the method became unreachable; had one other caller remained, nothing would have said a word.
///
/// **So the wiring is the RETURN VALUE now.** The old method wrote into `MomentScene.Appearance` as
/// a side effect, and a side effect is exactly the thing that goes missing when code moves —
/// B193's whole subject. <see cref="Ensure"/> hands back the appearance to use, which a caller
/// cannot benefit from without assigning it.
///
/// **Lazy on purpose, and this is a constraint rather than an optimisation.** The archives open
/// AFTER a demo is applied, so building at load time reads nothing — the first attempt did exactly
/// that and produced an empty table in silence. The caller therefore calls this per moment, and it
/// answers instantly once there is something to answer with.
/// </remarks>
public static class DemoAppearance
{
    /// <summary>An appearance that knows nothing, used until the install can be read.</summary>
    /// <remarks>
    /// **A sentinel compared by identity**, both here and by `MomentScene`'s "no player appearance"
    /// report, so it has to be one instance. It is also what makes "nothing built yet"
    /// distinguishable from "built, and this demo genuinely resolved no models" — a distinction the
    /// null-object pattern loses unless something keeps it (D83).
    /// </remarks>
    public static IPlayerAppearance None => NoAppearance.Instance;

    /// <summary>The appearance to use, building it the first time the install can be read.</summary>
    /// <param name="current">What the caller is using now; returned unchanged once it is real.</param>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides, or null before it is opened.</param>
    /// <param name="log">Where the resolved weapon-role table is reported.</param>
    /// <returns>The appearance to use, which the caller must assign.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> is null.</exception>
    /// <remarks>
    /// **The held set is gathered from every FRAME, not from the roster at one tick.** A player
    /// switches weapon constantly, and a table built from what is carried right now is missing a
    /// suffix the moment anybody draws anything else — which shows as one weapon held in the pose
    /// of another, not as an error.
    ///
    /// **Keyed by (weapon, class) rather than by weapon.** The same script name resolves to
    /// different roles per class, which is why the pair is what
    /// <see cref="WeaponRoles.Suffix(string, int?)"/> takes.
    /// </remarks>
    public static IPlayerAppearance Ensure(
        IPlayerAppearance current, DemoTimeline? timeline, GameContent? game, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (current is not NoAppearance || timeline is null || game is null)
        {
            return current;
        }

        // Only the classes this recording mentions: the archive holds 78 weapon scripts, a match
        // touches a handful, and each one costs an ICE decryption.
        //
        // **Weapon AND holder**, because the role is not a property of the weapon alone: a shotgun
        // is a primary for an engineer and a secondary for a soldier, a heavy and a pyro.
        HashSet<(string Weapon, int? Class)> held = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (player.WeaponClass is { } weapon)
                {
                    held.Add((weapon, player.PlayerClass));
                }
            }
        }

        WeaponRoles roles = WeaponRoles.Read(game.Archives.Read, held);

        // **Built the moment the roles exist, because `GameAppearance` CAPTURES them.** It is a
        // record over the two values, so an appearance made before the roles were read keeps
        // answering null for every weapon suffix — which does not fail, it silently falls back to
        // the primary forms and draws the wrong animation on everybody.

        log.LogInformation(
            "{Message}",
            "weapon roles: " + string.Join(
                ", ",
                held.OrderBy(pair => pair.Weapon, StringComparer.Ordinal)
                    .ThenBy(pair => pair.Class)
                    .Select(pair =>
                        $"{pair.Weapon}/{pair.Class?.ToString(CultureInfo.InvariantCulture) ?? "?"}=" +
                        roles.Suffix(pair.Weapon, pair.Class))));

        return new GameAppearance(game.Classes, roles);
    }
}
