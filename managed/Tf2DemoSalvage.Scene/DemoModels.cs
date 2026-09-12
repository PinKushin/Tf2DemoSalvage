using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Every model a demo will ever show, worked out before anything is drawn.</summary>
/// <remarks>
/// **This was <c>MainForm.DemoModelPaths</c> and <c>WornModelPaths</c>** (B188, D90) — a question
/// about a demo and an install, asked from a window that had neither to do with it.
///
/// **The whole set is resolved up front because the loader is a dictionary, not an on-demand read.**
/// <see cref="MapAssets"/> is handed this set at map load and packs exactly what is in it, so a
/// model missing from it packs to nothing for ever. Loading during playback would grow the material
/// table and force a re-upload mid-match, which is the cost the up-front pass exists to avoid.
/// </remarks>
public static class DemoModels
{
    /// <summary>Pack every model a demo will show, before anything is drawn.</summary>
    /// <param name="models">Where packed models live.</param>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides, or null when it is not available.</param>
    /// <param name="render">The render log.</param>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> or <paramref name="render"/> is null.</exception>
    /// <remarks>
    /// **This was `MainForm.PrecacheModels`** (B208), and it is the exact twin of
    /// `Presentation.DemoSounds.Precache` — same guards, same narrow catch, same timing line. Its
    /// twin moved out of the window on 2026-08-26 and this one was left behind, which is the worse
    /// kind of miss: an asymmetry that afterwards looks deliberate.
    ///
    /// **It lives in `Scene` while its twin lives in `Presentation`, and that is not an
    /// inconsistency.** Each sits with its collaborators — this needs `EntityModelSet` and
    /// `GameContent`, both here; the sound one needs `SoundCache` from `Audio`, which only
    /// `Presentation` can see. Composing downward is D92.
    ///
    /// **The guards are here rather than at the call site**, so a future caller does not have to
    /// rediscover that precaching needs both a demo and an open install.
    ///
    /// **The exceptions are caught rather than thrown**, and narrowly: a model that will not read is
    /// a defect in that file or in our reading of it, and must not take the whole demo down with it.
    /// A failure costs the precache and nothing else — anything missed is packed on sight exactly as
    /// before, which is slower rather than broken.
    ///
    /// **Up front is the engine's own timing** (D86). `CBaseEntity::PrecacheModel` is guarded by
    /// `IsPrecacheAllowed()` and warns on an out-of-order precache, because Source loads models at
    /// level load and not on sight. Packing when a prop first became visible is what cost 385 ms in
    /// a single frame here, and an asynchronous load would only move the hitch rather than remove
    /// it — the first appearance would still wait.
    ///
    /// **The timeline is a better list than `modelprecache`, which is what the engine uses.** The
    /// table names what the SERVER precached, including models this recording never shows; the
    /// tracks name what actually appears. Both are known before the first frame, which is the part
    /// that matters, so the narrower list wins at no cost.
    /// </remarks>
    public static void Precache(
        EntityModelSet models, DemoTimeline? timeline, GameContent? game, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(render);

        if (timeline is null || game is null)
        {
            return;
        }

        try
        {
            long packedAt = Stopwatch.GetTimestamp();

            HashSet<string> packing = ToPack(timeline, game);

            // **Said out loud because an attachment that is never packed looks exactly like an
            // attachment the schema does not declare** — both draw nothing. The renderer's own
            // "posed before its geometry was uploaded" names the model but not why, and the two
            // causes are a missing pack and a missing upload.
            int attachments = timeline is { } withItems
                ? game.Weapons.AllAttachmentsIn(withItems)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count()
                : 0;

            models.Precache(packing);

            double packedSeconds =
                (Stopwatch.GetTimestamp() - packedAt) / (double)Stopwatch.Frequency;

            render.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"precached models in {packedSeconds * 1000d:0} ms " +
                    $"({models.Count} packed of {packing.Count} offered, " +
                    $"{attachments} of them item attachments, " +
                    $"{models.Vertices.Count} vertices)"));
        }
        catch (Exception failure) when (
            failure is InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            render.LogWarning(failure, "precaching models");
        }
    }

    /// <summary>Every studio model the demo shows, at any tick, from any source.</summary>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides.</param>
    /// <returns>Distinct model paths, compared without regard to case.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    /// <remarks>
    /// **Four sources, and three of them are easy to forget** — each was a real bug:
    ///
    /// <list type="bullet">
    /// <item><b>Every class, not only the ones standing at tick zero.</b> A player can switch class
    /// at any moment, so a set built from who is playing now is missing whatever they change to, and
    /// that player simply vanishes mid-round. Nine models is the whole roster and it loads once.</item>
    /// <item><b>The viewmodels</b>, which are in neither of the other sets: a viewmodel has no origin
    /// so the timeline deliberately keeps it out of <c>Props</c>. It cost a whole feature — the
    /// viewer resolved <c>c_demo_arms.mdl</c>, packed it, reported "0 instances" and drew nothing,
    /// with the model in the archive the entire time.</item>
    /// <item><b>The held weapons</b>, which are not entities at all and are resolved through the item
    /// schema.</item>
    /// </list>
    /// </remarks>
    public static HashSet<string> Needed(DemoTimeline? timeline, GameContent game)
    {
        ArgumentNullException.ThrowIfNull(game);

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string model in game.ModelPaths())
        {
            paths.Add(model);

            foreach (string gib in GibsOf(model, game))
            {
                paths.Add(gib);
            }
        }

        if (timeline is not { } demo)
        {
            return paths;
        }

        foreach (ScenePropTrack track in demo.Props)
        {
            // **A studio track can have no path yet, and that is not a model to load.** A weapon
            // whose model the wire never carried reaches Core with an empty path and an item
            // number — `CEconEntity::UpdateModelToClass` resolves it from `items_game.txt`, which
            // Core cannot read — and its kind is Studio because every `model_player` is a `.mdl`.
            //
            // Passing the empty string on threw out of `PakFile.ReadFile` and killed the viewer at
            // load. The models themselves are not lost: `game.Weapons.AllIn` below walks what every
            // player holds and resolves each through the same item schema, so `c_medigun.mdl` is
            // loaded by the route that knows its name.
            if (track.Kind == SceneModelKind.Studio && track.ModelPath.Length > 0)
            {
                paths.Add(track.ModelPath);
            }
        }

        foreach (string arms in demo.ViewmodelModels)
        {
            paths.Add(arms);
        }

        foreach (string weapon in game.Weapons.AllIn(demo))
        {
            paths.Add(weapon);
        }

        // **The extra models an item hangs on itself, which appear in no track and no string
        // table.** `attached_models` names them in the item schema and the entity networks only its
        // item index, so nothing above can find them — measured on the shipped schema, 325
        // definitions declare at least one.
        //
        // **This list and the packing set are DIFFERENT lists, and that is B195.** Adding
        // attachments to the packer alone put the Degreaser's pilot light in `EntityModelSet` with
        // no geometry behind it: `Add` writes the model's key, sets `added`, and then drops out
        // silently when the geometry lookup answers nothing — so the count rose, the draw emitted
        // an instance, and the renderer reported "posed before its geometry was uploaded". The
        // asset loader is what actually reads the file, so the path has to be here too.
        foreach (string attachment in game.Weapons.AllAttachmentsIn(demo))
        {
            paths.Add(attachment);
        }

        // **What an ITEM names for ITSELF, which no track carries** (B379). The walk over `demo.Props`
        // above adds a track's own path, and a worn item does not have one: `WeaponPropModels.Resolve`
        // replaces the prop's model from `GetPlayerDisplayModel` at draw time for every prop with an
        // item index, so the model that reaches the renderer was never on the track this list is built
        // from. Measured on `20130518_0313_cp_granary_blu_blu`: 34 of the 66 models that packed no
        // geometry at all were absent from this set and named by no track — every one a
        // `models/player/items/…` cosmetic, thirteen of them undrawn on each frame, while the asset
        // load reported `ASKED FOR 166; HAVE 166; MISSING 0` and was telling the truth.
        foreach (string worn in game.Weapons.AllWornIn(demo))
        {
            paths.Add(worn);
        }

        return paths;
    }

    /// <summary>Every model to pack before playback, so nothing is read mid-match.</summary>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides.</param>
    /// <returns>Distinct model paths, compared without regard to case.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    /// <remarks>
    /// **This is NOT <see cref="Needed"/>, and the two disagreeing is B195.** This set is what gets
    /// PACKED into the vertex buffer; `Needed` is what gets DECODED by the asset loader. They are
    /// built from different sources — this one from the timeline's own accessor plus the class
    /// roster, that one from the prop tracks, the viewmodels and the item schema — so a path in one
    /// and not the other either packs to nothing or hitches on first sight. Neither fails loudly.
    ///
    /// Kept as it was rather than unified here, because merging them changes which models are packed
    /// and that wants its own measurement rather than riding along with a move.
    ///
    /// **The class models are added because a player's model is chosen at runtime.** Nothing on the
    /// wire carries it — `CTFPlayerClassShared::GetModelName` resolves it from the class script — so
    /// a player track's own path may not name every model that player will wear.
    /// </remarks>
    public static HashSet<string> ToPack(DemoTimeline? timeline, GameContent game)
    {
        ArgumentNullException.ThrowIfNull(game);

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        if (timeline is { } demo)
        {
            // Props, players and — the one a caller would miss — the viewmodels, which change on
            // every weapon switch and are private to the timeline.
            foreach (string path in demo.ModelPaths())
            {
                paths.Add(path);
            }
        }

        foreach (string model in game.ModelPaths())
        {
            paths.Add(model);

            foreach (string gib in GibsOf(model, game))
            {
                paths.Add(gib);
            }
        }

        // **The extra models items hang on themselves**, which appear in no track and in no string
        // table: `attached_models` names them in the item schema, and the entity that carries them
        // networks only its item index. Measured on the shipped schema, 325 definitions declare at
        // least one — the Degreaser's pilot light, the Quick-Fix's `c_overhealer.mdl`.
        //
        // Packed up front like everything else here, because the engine treats loading geometry
        // during play as a programming error (D86) and an attachment is discovered exactly when the
        // item is drawn.
        if (timeline is { } withItems)
        {
            foreach (string attachment in game.Weapons.AllAttachmentsIn(withItems))
            {
                paths.Add(attachment);
            }

            // **And what the item names for ITSELF** (B379). B195's whole point is that this set and
            // `Needed` disagreeing is a defect in either direction: a path packed and not loaded
            // draws nothing, and one loaded and not packed hitches on first sight. The worn models
            // were in neither.
            foreach (string worn in game.Weapons.AllWornIn(withItems))
            {
                paths.Add(worn);
            }
        }

        return paths;
    }

    /// <summary>Every entity sprite's material, which is in no other load list (B378).</summary>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <returns>Distinct model paths of kind <see cref="SceneModelKind.Sprite"/>.</returns>
    /// <remarks>
    /// **A sprite is not in <see cref="Needed"/> and should not be.** That walk adds a track's path
    /// only when its kind is <c>Studio</c>, because everything it feeds reads a `.mdl`; a sprite's
    /// "model" is a `.vmt` or a `.spr` and handing one to a model loader draws nothing and reports
    /// nothing — which the loader's own remarks already say. So the paths need their own list rather
    /// than a relaxed filter on that one.
    ///
    /// **Unlike the worn models of B379, these ARE named by a track**, so this is a filter and not a
    /// resolve: `env_sprite` networks its model index like any other entity and the string table
    /// turns it into `materials/Sprites/light_glow03.vmt`. The two lists are separate because they
    /// load different things, not because the paths come from different places.
    /// </remarks>
    public static HashSet<string> Sprites(DemoTimeline? timeline)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        if (timeline is not { } demo)
        {
            return paths;
        }

        foreach (ScenePropTrack track in demo.Props)
        {
            if (track.Kind == SceneModelKind.Sprite && track.ModelPath.Length > 0)
            {
                paths.Add(track.ModelPath);
            }
        }

        return paths;
    }

    /// <summary>The models the demo ever hangs off another entity's skeleton.</summary>
    /// <param name="timeline">The decoded demo, or null when none is open.</param>
    /// <param name="game">What the install provides.</param>
    /// <returns>The worn set, which is empty when no demo is open.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="game"/> is null.</exception>
    /// <remarks>
    /// The rule itself is <see cref="WornModels.From"/>, which is where its reasoning and its tests
    /// live. This supplies the two sources: the demo's own prop tracks, and the first-person
    /// weapons, which are built by the viewer and appear in no timeline.
    /// </remarks>
    public static HashSet<string> Worn(DemoTimeline? timeline, GameContent game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (timeline is not { } demo)
        {
            return [];
        }

        HashSet<string> worn = WornModels.From(demo.Props, game.Weapons.AllIn(demo));

        // **An attachment is skinned for the same reason a worn item is: it has no transform of its
        // own.** `DrawEconEntityAttachedModels` poses it with the ITEM's bone-to-world array
        // (`econ_entity.cpp:103`), so baking its bones away leaves nothing to hang it from — the
        // rule this set exists to enforce, applied to the one case that reaches the draw by a
        // different route.
        foreach (string attachment in game.Weapons.AllAttachmentsIn(demo))
        {
            worn.Add(attachment);
        }

        return worn;
    }

    /// <summary>The gib models a class model's <c>.phy</c> declares.</summary>
    /// <param name="model">The class model, as the roster names it.</param>
    /// <param name="game">The install the <c>.phy</c> is read from.</param>
    /// <returns>Every piece's model path, or nothing when the model ships no break list.</returns>
    /// <remarks>
    /// **The engine precaches these with the model itself and by the same call.**
    /// <c>CTFPlayer::PrecachePlayerModels</c> (<c>tf_player.cpp:2848</c>) runs
    /// <c>PrecacheModel( pszModel )</c> and then <c>PrecacheGibsForModel( iModel )</c> for every
    /// class — which is <c>PrecachePropsForModel( iModel, "break" )</c>
    /// (<c>props_shared.cpp:1239</c>), walking the collide data's key values and precaching every
    /// <c>breakModel.modelName</c> it finds under <c>break</c>.
    ///
    /// **Nothing else in this file can bring a gib in**, which is why the omission drew nothing
    /// rather than drawing late: a gib is in no <c>ScenePropTrack</c>, no string table and no item
    /// schema, because it is not an entity until a player gibs. By then the loader is a dictionary
    /// rather than an on-demand read, so a gib missing from these sets packs to nothing for ever —
    /// B195 one layer over, and the reason it belongs in BOTH <see cref="Needed"/> and
    /// <see cref="ToPack"/>.
    ///
    /// **A missing or malformed <c>.phy</c> yields nothing rather than throwing.** Most models ship
    /// none, and a file that will not parse is reported where it is read for its geometry —
    /// <c>PropModels.ReadBreakPieces</c> logs it against the model — so failing the whole demo load
    /// here would lose every other model to one bad file while saying no more than that read does.
    /// </remarks>
    private static IReadOnlyList<string> GibsOf(string model, GameContent game) =>
        [.. BreakPiecesOf(model, game).Select(piece => PhysicsModel.GibPath(piece.Model))];

    /// <summary>The break list a model's <c>.phy</c> declares, read from the install.</summary>
    /// <param name="model">The model, as the roster or a track names it.</param>
    /// <param name="game">The install the <c>.phy</c> is read from.</param>
    /// <returns>The pieces, or nothing when the model ships no break list.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Public because a corpse needs the same list at draw time and must not read it a second
    /// way.** <see cref="RagdollProps.Fill"/> takes a supplier of exactly this, and the viewer's
    /// answers it out of the frame cache — which only ever holds a model something has already
    /// DRAWN. A gibbed corpse draws no body, so its class model can be absent from that cache and
    /// the supplier then answers "no pieces" for a model that declares nine
    /// (<c>docs/memory/precache-what-the-engine-precaches.md#a-lookup-is-not-a-loader</c>). Reading
    /// the <c>.phy</c> is what the engine does — <c>PrecachePropsForModel</c> parses the collide
    /// data's key values directly — and it is cheap enough to do per model, since a <c>.phy</c>'s
    /// text is a few hundred bytes beside
    /// the megabytes of geometry the same load already paid for.
    /// </remarks>
    public static IReadOnlyList<PhysicsBreakPiece> BreakPiecesOf(string model, GameContent game)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(game);

        if (game.Archives.Read(Path.ChangeExtension(model, ".phy")) is not { Length: > 0 } file)
        {
            return [];
        }

        try
        {
            return PhysicsModel.Read(file).BreakPieces;
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }
}
