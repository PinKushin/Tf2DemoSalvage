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
        // **Weapon, holder AND item**, because the role is not a property of the weapon alone: a
        // shotgun is a primary for an engineer and a secondary for a soldier, a heavy and a pyro — and
        // an item's `anim_slot` outranks the script, so a demoman's stock launchers are each the
        // other's table (B105). The scripts are read per weapon and holder; the item is for the report.
        HashSet<(string Weapon, int? Class, int? Item)> held = [];

        foreach (TimelineFrame frame in timeline.Frames)
        {
            foreach (ScenePlayer player in frame.Players)
            {
                if (player.WeaponClass is { } weapon)
                {
                    held.Add((weapon, player.PlayerClass, player.WeaponItem));
                }
            }
        }

        WeaponRoles roles = WeaponRoles.Read(game.Archives.Read, held.Select(each => (each.Weapon, each.Class)));

        // **Only the scenes this recording plays, resolved once each** (B351). The archive is 3.6 MB
        // carrying 9,939 scenes, and turning one filename into a plan costs a CRC search, an LZMA
        // decompression and an event walk — so this is the same arrangement the weapon roles use one
        // paragraph above, and for the same reason: a match touches a handful.
        //
        // Empty when the install has no `scenes.image`, which leaves every taunt unresolved rather
        // than failing: the same degradation the class models and the item schema take.
        Dictionary<string, SceneTaunt> taunts = new(StringComparer.Ordinal);

        // **Each expression file once** (B513): a match's taunts and voice lines name a handful, over and over.
        Dictionary<string, FlexSettings?> expressionFiles = new(StringComparer.OrdinalIgnoreCase);

        FlexSettings? Expression(string name)
        {
            if (!expressionFiles.TryGetValue(name, out FlexSettings? file))
            {
                file = ReadExpression(game, name, log);
                expressionFiles[name] = file;
            }

            return file;
        }

        if (game.Archives.Read(ScenePath) is { Length: > 0 } image &&
            SceneImage.Read(image) is { } scenes)
        {
            foreach (SceneChoreography playing in timeline.Scenes)
            {
                if (playing.Scene.Length > 0 && !taunts.ContainsKey(playing.Scene) &&
                    scenes.TauntFor(playing.Scene, Expression) is { } taunt)
                {
                    taunts[playing.Scene] = taunt;
                }
            }

            // Stryker disable all : the String mutator wraps the interpolated literal in a
            // ternary that cannot bind to string.Create's interpolated-string handler (CS1620),
            // and Safe Mode then drops every mutation in this method — B410.
            log.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"scenes: {scenes.Count:N0} in the archive, {taunts.Count:N0} played by this " +
                    $"recording, {taunts.Values.Count(one => one.Gestures.Count > 0):N0} staging a " +
                    $"gesture, {taunts.Values.Count(one => one.Loops):N0} looping"));

            // Stryker restore all
        }
        else
        {
            log.LogInformation("{Message}", "scenes: no scenes.image, so taunts will not resolve");
        }

        // **The item schema comes along because a player's body number needs it** (B352): a hat
        // hides the head it replaces, and only `items_game.txt` says which part that is. Reached
        // for here rather than by the scene for the same reason the class models are — this is the
        // one place that already holds the install. Its `anim_slot` decides the weapon's table too (B105).
        (Dictionary<string, Sentence> sentences, Dictionary<string, float> lengths) = Sentences(game, log);

        GameAppearance appearance = new(game.Classes, roles, game.Weapons.Items, taunts)
        {
            Faces = new FaceSources(
                timeline.Scenes,
                scene => taunts.TryGetValue(scene, out SceneTaunt? taunt) ? taunt : null,
                timeline.Sounds,
                timeline.IntervalPerTick,
                sentences,
                Expression,
                lengths,

                // A sound the cache lists no count for — every MP3, an ADPCM wave, a map's own — is read from its file;
                // the path is the cache's spelling, and a .wav name may be served by its .mp3.
                path =>
                {
                    string file = path.Replace('\\', '/');
                    return (game.Archives.Read(file) ?? game.Archives.Read(System.IO.Path.ChangeExtension(file, ".mp3"))) is { } bytes
                        ? SoundLength.Seconds(bytes)
                        : null;
                })
            {
                InterpolationSeconds =
                    ScenePropTrack.DelayTicksFor(timeline.IntervalPerTick, timeline.ClientInterpAmount) * (double)timeline.IntervalPerTick,
            },
        };

        // **The role each held weapon is DRAWN with, asked of the appearance the scene will use**, so
        // the report cannot say one table while the pose gets another — the script's answer alone
        // says PRIMARY for every demoman's grenade launcher, which the engine animates as a secondary.
        log.LogInformation(
            "{Message}",
            "weapon roles: " + string.Join(
                ", ",
                held.OrderBy(each => each.Weapon, StringComparer.Ordinal)
                    .ThenBy(each => each.Class)
                    .ThenBy(each => each.Item)
                    .Select(each =>
                        $"{each.Weapon}/{each.Class?.ToString(CultureInfo.InvariantCulture) ?? "?"}" +
                        $"/{each.Item?.ToString(CultureInfo.InvariantCulture) ?? "-"}=" +
                        appearance.WeaponSuffix(each.Weapon, each.Class, each.Item))));

        return appearance;
    }

    /// <summary>The VPKs' sound caches that carry sentences (B513) — the voice archive and the misc one.</summary>
    private static readonly string[] SoundCaches =
        ["tf2_sound_vo_english.vpk.sound.cache", "tf2_sound_misc.vpk.sound.cache", "tf2_misc.vpk.sound.cache"];

    /// <summary>
    /// Every cached sentence, by sound path (B513). **TF2's voice lines are MP3s with no phoneme chunk**, so the
    /// engine's lip sync reads the sentence the sound cache stores beside the VPK (<c>CAudioSourceCachedInfo::Restore</c>,
    /// x64 <c>engine.dll</c> <c>0x180053740</c>); a missing or unreadable cache moves no mouth.
    /// </summary>
    private static (Dictionary<string, Sentence> Sentences, Dictionary<string, float> Lengths) Sentences(
        GameContent game, ILogger log)
    {
        Dictionary<string, Sentence> all = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, float> lengths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in SoundCaches)
        {
            if (game.Archives.Read(name) is not { Length: > 0 } bytes)
            {
                continue;
            }

            try
            {
                (Dictionary<string, Sentence> sentences, Dictionary<string, float> cached) = SoundCacheFile.ReadAll(bytes);

                foreach ((string path, Sentence sentence) in sentences)
                {
                    all.TryAdd(path, sentence);
                }

                foreach ((string path, float length) in cached)
                {
                    lengths.TryAdd(path, length);
                }
            }
            catch (System.IO.InvalidDataException failure)
            {
                log.LogWarning(failure, "sentences: {Cache} could not be read", name);
            }
        }

        log.LogInformation(
            "{Message}",
            $"sentences: {all.Count.ToString(CultureInfo.InvariantCulture)} cached for lip sync, " +
            $"{lengths.Count.ToString(CultureInfo.InvariantCulture)} sound lengths");
        return (all, lengths);
    }

    /// <summary>Where the compiled choreography archive sits inside the game's VPKs (B351).</summary>
    private const string ScenePath = "scenes/scenes.image";

    /// <summary>
    /// <c>expressions/%s.vfe</c> (<c>c_baseflex.cpp:465</c>), or null when absent or unreadable — the engine then
    /// skips the event, and so does the face (B513).
    /// </summary>
    private static FlexSettings? ReadExpression(GameContent game, string name, ILogger log)
    {
        string path = "expressions/" + name.Replace('\\', '/') + ".vfe";

        if (game.Archives.Read(path) is not { Length: > 0 } bytes)
        {
            log.LogInformation("{Message}", $"expressions: {path} is not in the archives, so its events move no face");
            return null;
        }

        try
        {
            return FlexSettings.Read(bytes);
        }
        catch (System.IO.InvalidDataException failure)
        {
            log.LogWarning(failure, "expressions: {Path} could not be read", path);
            return null;
        }
    }
}
