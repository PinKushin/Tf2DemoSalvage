using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Audio;

/// <summary>One <c>env_soundscape</c> as the map places it.</summary>
/// <param name="Name">The soundscape it names, such as <c>Gorge.Inside</c>.</param>
/// <param name="Index">Its position in the client's list, or -1 when the name is unknown.</param>
/// <param name="X">Where the entity is, in world units.</param>
/// <param name="Y">Where the entity is.</param>
/// <param name="Z">Where the entity is.</param>
/// <param name="Radius">
/// How far it reaches. **-1 means unlimited**, which is what every entity on cp_process uses — so
/// on that map visibility alone decides, not range.
/// </param>
/// <param name="Positions">
/// Its <c>localSound</c> slots, for a script's <c>"position" "3"</c>: <b>slot N at index N</b>, null where
/// <c>localBits</c> leaves bit N clear (<c>soundscape.cpp:217-229</c>) — eight from <see cref="SoundscapePlacements.From"/>.
/// </param>
/// <param name="Id">
/// Which placement this is, by position in the map's own entity order. **The engine's analogue is
/// `entIndex`** — `UpdateAudioParams` restarts a soundscape when either the index or the entity
/// changes, because the positions its loops play at come from that entity and differ between two
/// entities naming the same soundscape. cp_process has 21 entities all naming `Gorge.Inside`.
/// </param>
/// <param name="Cluster">
/// The visibility cluster its origin sits in, or −1 when the map has no vis data or the entity is
/// in solid space. **Precomputed because the engine precomputes it** —
/// `CSoundscapeSystem::LevelInitPostEntity` builds a per-cluster list once at map load rather than
/// asking per frame.
/// </param>
/// <param name="Enabled">
/// <c>!m_bDisabled</c>, from the <c>StartDisabled</c> keyfield (<c>soundscape.cpp:91</c>). A disabled soundscape never
/// contends (<c>:247-256</c>). Only the INITIAL state is knowable: the Enable/Disable inputs that flip it at run time
/// are entity I/O, which no demo records.
/// </param>
public readonly record struct SoundscapePlacement(
    int Id,
    string Name,
    int Index,
    float X,
    float Y,
    float Z,
    float Radius,
    IReadOnlyList<(float X, float Y, float Z)?> Positions,
    int Cluster = -1,
    bool Enabled = true);

/// <summary>
/// Which soundscape a listener is standing in, decided the way the engine decides it.
/// </summary>
/// <remarks>
/// **The map is the source, not the demo.** A SourceTV recording carries the SourceTV camera's
/// soundscape rather than the spectated player's, because `m_audio` is sent only to the client that
/// owns the entity — so a viewer following a player has to work it out from the map, as the server
/// does. Measured: the STV recording of cp_process carries two samples and one index while the POV
/// recording of the same session carries 64 across three (B173).
///
/// It also removes a scaling problem the owner named: *"i really dont want to have to make manual
/// dumps like that for every map, so we need to figure out how to do this right... and probably
/// looking at bsps instead of making me manually do it"*. Every map carries its own answer.
///
/// **Two classes are placed, and only one names a soundscape.** `env_soundscape` carries a `soundscape` key;
/// `env_soundscape_proxy` carries `MainSoundscapeName`, the targetname of any `CEnvSoundscape` — an
/// `env_soundscape_triggerable` or another proxy included — whose index AND position names it copies
/// (`CEnvSoundscapeProxy::Activate`, <c>soundscape.cpp:40-62</c>, B481). cp_process has 4 of the first and 40 of the
/// second. The triggerable is read only as a master (B483).
/// </remarks>
public sealed class SoundscapePlacements
{
    /// <summary><c>NUM_AUDIO_LOCAL_SOUNDS</c>: the <c>position0</c>..<c>position7</c> slots.</summary>
    private const int LocalSounds = 8;

    private readonly List<SoundscapePlacement> _placements;

    private SoundscapePlacements(List<SoundscapePlacement> placements) => _placements = placements;

    /// <summary>Every placed soundscape, in the map's own entity order.</summary>
    /// <remarks>
    /// **Map order, because the engine's selection depends on it.** `UpdateForPlayer` walks the
    /// entity list carrying state forward, so a different order can settle on a different entity
    /// where two are equally close and both visible.
    /// </remarks>
    public IReadOnlyList<SoundscapePlacement> Placements => _placements;

    /// <summary>Reads a map's soundscape entities and resolves them against the catalog.</summary>
    /// <param name="entities">The map's entity lump, already parsed.</param>
    /// <param name="catalog">The client's soundscape list, for name to index.</param>
    /// <returns>The placements.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <param name="leaves">
    /// The map's BSP tree, used to resolve each entity's visibility cluster. Optional: without it
    /// every placement carries cluster −1 and <see cref="Choose"/> does no visibility filtering,
    /// which is the behaviour this had before B177.
    /// </param>
    public static SoundscapePlacements From(
        IReadOnlyList<BspEntity> entities, SoundscapeCatalog catalog, BspLeafTree? leaves = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(catalog);

        // Index by name once, since a proxy resolves through it and there are forty of them.
        Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < catalog.Count; index++)
        {
            // **Last wins**, matching the client's own backward search
            // (`c_soundscape.cpp:352` walks from the end), so a later file redefining a name
            // overrides an earlier one exactly as it does in the engine.
            byName[catalog.Soundscapes[index].Name] = index;
        }

        // A proxy names its master by targetname: `gEntList.FindEntityByName( NULL, m_MainSoundscapeName )`
        // (`soundscape.cpp:42`), which is the FIRST entity of that name in the list, whatever its class.
        Dictionary<string, BspEntity> byTargetName = new(StringComparer.OrdinalIgnoreCase);

        foreach (BspEntity entity in entities)
        {
            if (entity.TryGetValue("targetname", out string target))
            {
                byTargetName.TryAdd(target, entity);
            }
        }

        // **Spawn, then activate — the server's two passes over the entity list** (`mapentities.cpp:257-308`). At spawn
        // each `CEnvSoundscape` takes its index from its own `soundscape` key (`Precache`, `soundscape.cpp:360-377`);
        // a proxy's `Precache` is empty (`soundscape.h:86`), so it holds the constructor's -1 (`:105`) and its own
        // position keys. Then each proxy, in list order, copies what its master holds AT THAT MOMENT (`:49-55`) — so
        // a master that is itself a proxy hands on its master's only if it activated first.
        Dictionary<BspEntity, Held> held = new(ReferenceEqualityComparer.Instance);

        foreach (BspEntity entity in entities)
        {
            if (IsSoundscape(entity))
            {
                string own = !IsProxy(entity) && entity.TryGetValue("soundscape", out string named) ? named : string.Empty;

                // **-1 for a name the catalog does not hold, rather than dropping the entity.** A map naming a
                // soundscape this install lacks is a fact worth being able to report; silently omitting it would look
                // identical to the map having no ambience there.
                held[entity] = new Held(own, own.Length > 0 && byName.TryGetValue(own, out int index) ? index : -1, entity);
            }
        }

        foreach (BspEntity entity in entities)
        {
            // `if ( m_MainSoundscapeName != NULL_STRING )` then `dynamic_cast< CEnvSoundscape* >` (`:40-45`): a master of
            // any of the three classes. With none, only a warning (`:58`) — the proxy keeps -1 and stays in the list.
            if (IsProxy(entity) &&
                entity.TryGetValue("MainSoundscapeName", out string master) &&
                master.Length > 0 &&
                byTargetName.TryGetValue(master, out BspEntity? main) &&
                held.TryGetValue(main, out Held copied))
            {
                held[entity] = copied;
            }
        }

        List<SoundscapePlacement> placements = [];

        foreach (BspEntity entity in entities)
        {
            // **Whose position keys the placement plays at.** A proxy's are its master's: `Activate` copies every
            // `m_positionNames[i]` from it (`soundscape.cpp:52-54`), so the proxy's own never reach the player (B464).
            //
            // The triggerable is held, as a master, and not placed: its own place in the radius contest is B483.
            if (!held.TryGetValue(entity, out Held soundscape) || IsTriggerable(entity))
            {
                continue;
            }

            (float X, float Y, float Z) origin = Origin(entity);

            placements.Add(new SoundscapePlacement(
                placements.Count,
                soundscape.Name,
                soundscape.Index,
                origin.X,
                origin.Y,
                origin.Z,
                Radius(entity),
                Targets(soundscape.PositionsFrom, byTargetName),

                // **The entity's own cluster, resolved once here.** The engine does the same at map
                // load rather than per frame (`LevelInitPostEntity`), and there is no reason to
                // walk the BSP tree forty-four times a second for a value that cannot change.
                leaves?.ClusterAt(origin.X, origin.Y, origin.Z) ?? -1,

                // `FIELD_BOOLEAN` from a keyvalue is `atoi( szValue ) != 0` (`saverestore_gamedll.cpp:62`); a proxy
                // inherits the keyfield and keeps its own.
                !(entity.TryGetValue("StartDisabled", out string disabled) && CStdlib.Atoi(disabled) != 0)));
        }

        return new SoundscapePlacements(placements);
    }

    /// <summary>The soundscape a listener at a point is in, or <c>null</c> when none reaches.</summary>
    /// <param name="x">The listener, in world units.</param>
    /// <param name="y">The listener.</param>
    /// <param name="z">The listener.</param>
    /// <param name="clear">Whether a segment between two points is unobstructed.</param>
    /// <param name="current">
    /// The placement chosen last time, which the engine favours — pass <c>null</c> on the first
    /// call.
    /// </param>
    /// <returns>The chosen placement, or <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clear"/> is null.</exception>
    /// <remarks>
    /// **A transcription of `CEnvSoundscape::UpdateForPlayer` (<c>soundscape.cpp:258</c>), not a
    /// summary of it.** Summarising it as "the nearest visible one wins" is nearly right and drops
    /// the hysteresis, which is what stops the ambience flickering between two rooms on a threshold:
    ///
    /// <code>
    /// range = (playerPosition - EarPosition()).Length()
    /// if ( current == this )
    ///     currentDistance = range; bInRange = withinRadius &amp;&amp; traceClear
    /// else if ( (!bInRange || range &lt; currentDistance) &amp;&amp; withinRadius &amp;&amp; traceClear )
    ///     current = this; bInRange = true; currentDistance = range
    /// </code>
    ///
    /// The state carries forward through the walk, so an entity considered later compares against
    /// whatever was taken earlier in the same pass — which is why the entity ORDER is preserved.
    ///
    /// **The trace is a delegate rather than a BSP reference**, so this type stays testable without
    /// a map: the rule and the geometry are separate questions, and the rule is the one with the
    /// hysteresis bug in it if there is one.
    /// </remarks>
    /// <param name="listenerCluster">
    /// The visibility cluster the listener stands in, or −1 when it is unknown.
    /// </param>
    /// <param name="visibility">
    /// The map's PVS, or <c>null</c> to consider every placement. **Both this and
    /// <paramref name="listenerCluster"/> are needed for filtering to happen at all**, and any of
    /// the three ways of not knowing — no vis data, a listener in solid space, a placement with no
    /// cluster — falls back to considering the placement rather than dropping it. Dropping on
    /// missing information would make a map without vis silent, which is far worse than the
    /// over-wide selection this exists to narrow.
    /// </param>
    public SoundscapePlacement? Choose(
        float x,
        float y,
        float z,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool> clear,
        SoundscapePlacement? current = null,
        int listenerCluster = -1,
        BspVisibility? visibility = null)
    {
        ArgumentNullException.ThrowIfNull(clear);

        // **The engine considers only the soundscapes in the listener's cluster** —
        // `m_soundscapesInCluster[clusterIndex]`, built at map load from each soundscape's PVS
        // (`soundscape_system.cpp:352-362`). Without this every entity on the map contends, so one
        // across the map can win on a long clear traceline, and the choice changes far more often
        // than the engine's would (B177).
        //
        // **Valve tests "is cluster j visible FROM the soundscape"; this asks the transpose.** The
        // PVS is symmetric — `vvis` computes mutual visibility — so the two agree, and asking it
        // this way round needs one decompressed row per listener rather than one per soundscape.
        bool Reachable(SoundscapePlacement placement) =>
            visibility is not { HasData: true } pvs ||
            listenerCluster < 0 ||
            placement.Cluster < 0 ||
            pvs.Visible(listenerCluster, placement.Cluster);

        SoundscapePlacement? chosen = current;

        // **Zero, and the current entity is measured FIRST — both are the engine's, and getting
        // either wrong destroys the hysteresis.** `CSoundscapeSystem::Update` seeds
        // `currentDistance = 0`, `bInRange = false`, then calls `UpdateForPlayer` on the CURRENT
        // soundscape before looping over the contenders and skipping it
        // (`soundscape_system.cpp:339-362`).
        //
        // Walking the list in order instead lets every placement before the current one compete
        // against `bInRange == false`, which nothing can lose to — so the current is displaced
        // before its own range is ever established, and the choice flips between co-named entities
        // on almost every update. cp_process has 21 entities named `Gorge.Outside`, and the fade
        // restarting on each flip is why the outdoor ambience never became audible.
        float currentDistance = 0f;
        bool inRange = false;

        if (current is { } held)
        {
            Consider(held);
        }

        foreach (SoundscapePlacement placement in _placements)
        {
            if (current is { } already && already.Id == placement.Id)
            {
                continue;
            }

            Consider(placement);
        }

        return chosen;

        void Consider(SoundscapePlacement placement)
        {
            // `if ( !IsEnabled() ) { if ( update.pCurrentSoundscape == this ) { pCurrentSoundscape = NULL;
            // currentDistance = 0; bInRange = false; } return; }` (`soundscape.cpp:247-256`). The player's params are
            // NOT rewritten, so `chosen` keeps the held one until a contender in range takes over (B464).
            //
            // **The inner reset is not ported because it changes nothing**: the held soundscape is measured FIRST, on
            // the state `FrameUpdatePostEntityThink` has just seeded to exactly those values (`soundscape_system.cpp:
            // 343-348`), and clearing `pCurrentSoundscape` only stops the walk skipping an entity that returns here
            // anyway. Ported, it was a mutant nothing could kill.
            if (!placement.Enabled)
            {
                return;
            }

            float dx = placement.X - x;
            float dy = placement.Y - y;
            float dz = placement.Z - z;

            float range = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            // `m_flRadius > range || m_flRadius == -1` — unlimited when negative.
            bool withinRadius = placement.Radius < 0f || placement.Radius > range;

            // **Against the placement passed in, not against whatever has been taken since.** The
            // engine tests `update.pCurrentSoundscape == this`, and it reaches the current one
            // before anything can have replaced it — so this branch is the current's own
            // measurement, and it must not fire for a contender that happens to be sitting in
            // `chosen`.
            if (current is { } held && held.Id == placement.Id)
            {
                currentDistance = range;
                inRange = withinRadius &&
                    clear((placement.X, placement.Y, placement.Z), (x, y, z));

                return;
            }

            if ((inRange && range >= currentDistance) || !withinRadius)
            {
                return;
            }

            // **Before the traceline, because that is the expensive one.** The engine never even
            // offers a soundscape outside the cluster list to `UpdateForPlayer`, so it never traces
            // to one either.
            if (!Reachable(placement))
            {
                return;
            }

            if (!clear((placement.X, placement.Y, placement.Z), (x, y, z)))
            {
                return;
            }

            chosen = placement;
            inRange = true;
            currentDistance = range;
        }
    }

    /// <summary>What a <c>CEnvSoundscape</c> holds once it has spawned, and a proxy once it has activated.</summary>
    /// <param name="Name">The soundscape's name, for the log; empty when it names none.</param>
    /// <param name="Index"><c>m_soundscapeIndex</c>.</param>
    /// <param name="PositionsFrom">The entity whose <c>position&lt;N&gt;</c> keys are its <c>m_positionNames</c>.</param>
    private readonly record struct Held(string Name, int Index, BspEntity PositionsFrom);

    /// <summary>A <c>CEnvSoundscape</c> or a class derived from it (<c>soundscape.cpp:23,69,406</c>).</summary>
    private static bool IsSoundscape(BspEntity entity) =>
        entity.ClassName.Equals("env_soundscape", StringComparison.OrdinalIgnoreCase) ||
        IsProxy(entity) ||
        IsTriggerable(entity);

    private static bool IsProxy(BspEntity entity) =>
        entity.ClassName.Equals("env_soundscape_proxy", StringComparison.OrdinalIgnoreCase);

    private static bool IsTriggerable(BspEntity entity) =>
        entity.ClassName.Equals("env_soundscape_triggerable", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the server puts an entity: its <c>origin</c> key through <see cref="StringToVector"/>, or the world origin.</summary>
    /// <remarks>
    /// `CBaseEntity::KeyValue` is the only thing that moves a map entity at spawn (`baseentity_shared.cpp:427-430`), so one
    /// with no key stays where it was made — the origin — and is still an entity, still found by name and still placed.
    /// </remarks>
    private static (float X, float Y, float Z) Origin(BspEntity entity) =>
        entity.TryGetValue("origin", out string origin) ? StringToVector(origin) : default;

    /// <summary>An entity's radius; -1, meaning unlimited, when it declares none.</summary>
    private static float Radius(BspEntity entity) =>
        entity.TryGetValue("radius", out string radius) &&
        float.TryParse(radius, NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
            ? read
            : -1f;

    /// <summary>Where an entity's numbered position targets are.</summary>
    /// <remarks>
    /// **`position0` to `position7` name other entities**, and the engine looks each up by
    /// targetname — `m_positionNames[NUM_AUDIO_LOCAL_SOUNDS]` in `soundscape.h:62`. A soundscape's
    /// `"position" "3"` then plays at whatever entity `position3` named, which is how one soundscape
    /// scatters its loops across a whole map.
    ///
    /// **The FIRST entity of the name, wherever it stands.** `FindEntityByName( NULL, m_positionNames[i], this, this )`
    /// then `GetAbsOrigin()` (`soundscape.cpp:222-226`): a name nothing holds leaves the bit clear, and one held by an
    /// entity with no `origin` key is at the world origin (B482). This took the first holder WITH an origin, and skipped
    /// one without — no installed map names such a target, so it is the rule a third-party map meets.
    ///
    /// **Slot N stays at index N.** `audio.localBits |= 1&lt;&lt;i; audio.localSound.Set( i, ... )`
    /// (`soundscape.cpp:225-226`) — an unset or unresolved slot is a clear bit, not a removed entry. This appended only
    /// the resolved ones, so a gap shifted every later target down a slot: 109 installed soundscapes have one, and on
    /// them a loop played at another loop's target or not at all (B464).
    /// </remarks>
    private static (float X, float Y, float Z)?[] Targets(
        BspEntity entity, Dictionary<string, BspEntity> byTargetName)
    {
        (float X, float Y, float Z)?[] targets = new (float X, float Y, float Z)?[LocalSounds];

        for (int slot = 0; slot < LocalSounds; slot++)
        {
            if (entity.TryGetValue($"position{slot.ToString(CultureInfo.InvariantCulture)}", out string named) &&
                named.Length > 0 &&
                byTargetName.TryGetValue(named, out BspEntity? target))
            {
                targets[slot] = Origin(target);
            }
        }

        return targets;
    }

    /// <summary><c>UTIL_StringToVector</c>: <c>atof</c> of each whitespace-separated field, a missing field zero.</summary>
    /// <remarks>
    /// <c>UTIL_StringToFloatArray( pVector, 3, ... )</c> (<c>util_shared.cpp:919-954</c>): fields are split at any byte up
    /// to a space, each is <c>atof</c>'d into a float, and the loop stops at the end of the text, zeroing the rest.
    /// *Not reproduced:* the copy into a 128-byte buffer first, which cuts a value past 127 characters.
    /// </remarks>
    private static (float X, float Y, float Z) StringToVector(string text)
    {
        Span<float> fields = stackalloc float[3];
        int at = 0;

        for (int field = 0; field < fields.Length; field++)
        {
            while (at < text.Length && text[at] <= ' ')
            {
                at++;
            }

            int start = at;

            while (at < text.Length && text[at] > ' ')
            {
                at++;
            }

            if (at == start)
            {
                break;
            }

            fields[field] = (float)CStdlib.Atof(text.AsSpan(start, at - start));
        }

        return (fields[0], fields[1], fields[2]);
    }
}
