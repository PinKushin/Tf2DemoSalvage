using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>One ConVar as the engine declares it: a name, a default, and who may change it.</summary>
/// <param name="Name">The engine's own name, exactly as it appears on the wire.</param>
/// <param name="Default">Valve's default, as the STRING the declaration carries.</param>
/// <param name="Replicated">Whether <c>FCVAR_REPLICATED</c> is set — a server may enforce it.</param>
/// <param name="Cheat">Whether <c>FCVAR_CHEAT</c> is set — a player cannot change it themselves.</param>
/// <param name="UserInfo">
/// Whether <c>FCVAR_USERINFO</c> is set — the RECORDER's value travels in the <c>userinfo</c> string
/// table, so a demo carries it even though no server sent it.
/// </param>
/// <param name="Min">The declared hard lower bound (`bHasMin, fMin`), or null when there is none.</param>
/// <param name="Max">The declared hard upper bound (`bHasMax, fMax`), or null when there is none.</param>
/// <remarks>
/// **The bounds are what `ConVar::ClampValue` applies** (`tier1/convar.cpp:891`, `:897`): a set outside them stores
/// `"%f"` of the bound, and every other set keeps its string as written (`:794-806`). The competitive pair is not
/// carried; it applies only under competitive restrictions. Read from the SDK declaration, or for an engine-side cvar
/// from TF2's own `help name`, which prints `min.`/`max.` when they are set (`convar.cpp:1432-1436`).
///
/// **The default is a string because Valve's is a string.** `ConVar( "sv_maxspeed", "320", … )`
/// takes a literal, and the engine parses it on demand — so a declaration that stored `320f` would
/// have made a decision the engine leaves open, and would be unable to carry `sv_downloadurl`.
/// <see cref="Number"/> is the parse, at the point of use.
/// </remarks>
public sealed record EngineConVar(
    string Name, string Default, bool Replicated, bool Cheat, bool UserInfo = false, float? Min = null, float? Max = null)
{
    /// <summary>The default as a number, for the ones that are numbers.</summary>
    /// <exception cref="FormatException">The default is not numeric, so a caller asked wrongly.</exception>
    /// <remarks>
    /// **Throws rather than returning zero.** A zero `sv_maxspeed` is a camera that does not move
    /// and a reason nobody can find; the memory directory calls this the sentinel trap. Invariant
    /// culture because a ConVar's default is engine text, not the reader's locale — `"0.1"` is
    /// `snd_mixahead` on every machine.
    /// </remarks>
    public float Number => float.Parse(Default, CultureInfo.InvariantCulture);
}

/// <summary>
/// What Valve declares for the ConVars this viewer depends on, rather than the numbers it used to
/// bake.
/// </summary>
/// <remarks>
/// **D106: nothing is hardcoded that Valve does not hardcode.** The owner, auditing the convar
/// surface under D104:
///
/// > *"the vast majority of the cvars are going to be constants that never change, but i still dont
/// > want anything hardcoded that valve does not hard code, because doing so makes the rendering
/// > engine we are using less agnostic"*
///
/// and, on the form that takes:
///
/// > *"baked default is never the right answer i dont think, at least not if its not a baked default
/// > valve has"*
///
/// **A default is right as a VALUE and wrong as a `const float`.** Valve wrote three things — a
/// name, a default, and the ability for a server to change it. Copying only the number discards the
/// two that make it portable across the nineteen years of builds this project reads, and it fails
/// silently: a server that raised `sv_maxspeed` sends the new value, this project decodes it, and a
/// baked constant ignores it.
///
/// **Every entry here was read from two sources thirteen years apart** — `source-sdk-2013` and the
/// installed build's own `tf/cvarlist.log` — and `MovementConVarConformanceTests` checks both on
/// every run. They agree today; the test is what notices if a TF2 update moves one.
///
/// **What this deliberately is not.** It is not a registry of everything TF2 ships (that is
/// `cvarlist.log`, 3,668 entries, and `docs/CVAR-COVERAGE.md` is the map of it). It holds only the
/// ConVars whose VALUE this project depends on, which is the set that was invisible to
/// `CvarNameConformanceTests` — that test measures names this viewer answers to, and a convar we
/// silently depend on never appears in it.
/// </remarks>
public static class EngineConVars
{
    /// <summary>
    /// The movement and spectator speeds, all replicated, all cheat-protected.
    /// </summary>
    /// <remarks>
    /// **The three `cl_` speeds are the surprise.** Despite the prefix they are
    /// `FCVAR_REPLICATED | FCVAR_CHEAT` — server-controlled, and a player cannot change them
    /// without `sv_cheats`. So for these the watcher's own config does not enter the answer at all:
    /// `FCVAR_REPLICATED` is documented in `iconvar.h` as *"server setting enforced on clients"*,
    /// and *"if a change is requested it must come from the console"*.
    ///
    /// **`in_main.cpp` declares each of them twice**, and the first is Counter-Strike's. Lines
    /// 70–73 are the `CSTRIKE_DLL` branch — 400, `FCVAR_CHEAT`, not replicated — and 76–79 are the
    /// `#else` that TF2 compiles. A grep that takes the first match gets the wrong game.
    /// </remarks>
    private static readonly EngineConVar[] Declarations =
    [
        // src/game/client/in_main.cpp:76-79, the non-CSTRIKE branch.
        new("cl_forwardspeed", "450", Replicated: true, Cheat: true),
        new("cl_backspeed", "450", Replicated: true, Cheat: true),
        new("cl_sidespeed", "450", Replicated: true, Cheat: true),
        new("cl_upspeed", "320", Replicated: true, Cheat: true),

        // src/game/shared/movevars_shared.cpp:47-52.
        new("sv_maxspeed", "320", Replicated: true, Cheat: false),
        new("sv_specspeed", "3", Replicated: true, Cheat: false),
        new("sv_specaccelerate", "5", Replicated: true, Cheat: false),
        new("sv_specnoclip", "1", Replicated: true, Cheat: false),

        // **What CGameMovement reads** (D205), movevars_shared.cpp:37-100, the non-CSTRIKE branches.
        new("sv_gravity", "800", Replicated: true, Cheat: false),
        new("sv_stopspeed", "100", Replicated: true, Cheat: false),
        new("sv_accelerate", "10", Replicated: true, Cheat: false),
        new("sv_airaccelerate", "10", Replicated: true, Cheat: false),
        new("sv_friction", "4", Replicated: true, Cheat: false),
        new("sv_bounce", "0", Replicated: true, Cheat: false),
        new("sv_maxvelocity", "3500", Replicated: true, Cheat: false),
        new("sv_stepsize", "18", Replicated: true, Cheat: false),

        // CTFGameMovement::DuckOverrides' gate on the duck timer and the one-duck-per-air rule (tf_gamemovement.cpp:49, :3190).
        new("tf_clamp_airducks", "1", Replicated: true, Cheat: false),

        // **The interpolation set, and the effective amount is none of them on its own.**
        // `GetClientInterpAmount()` in src/game/client/cdll_bounded_cvars.cpp:126 is
        // `MAX( cl_interp, cl_interp_ratio / cl_updaterate )`, and each half is separately clamped
        // by the server's two ratio bounds. Declared together because a reader that takes one and
        // not the others gets a plausible number and the wrong one — see
        // InterpolationConVarConformanceTests, which writes the formula down without implementing
        // it. Nothing consumes these yet, deliberately.
        new("cl_interp","0.1", Replicated: false, Cheat: false, UserInfo: true, Min: 0f, Max: 0.5f), // cdll_bounded_cvars.cpp:104
        new("cl_interp_ratio", "2.0", Replicated: false, Cheat: false, UserInfo: true),
        new("cl_updaterate", "20", Replicated: false, Cheat: false, UserInfo: true),
        new("sv_client_min_interp_ratio", "1", Replicated: true, Cheat: false),
        new("sv_client_max_interp_ratio", "5", Replicated: true, Cheat: false),
        // engine.dll 0x180009460 / 0x1800093e0: "10" and "66", FCVAR_REPLICATED — the bounds
        // `CBoundedCvar_UpdateRate` clamps cl_updaterate to.
        new("sv_minupdaterate", "10", Replicated: true, Cheat: false),
        new("sv_maxupdaterate", "66", Replicated: true, Cheat: false),

        // **The sound curve's parameters** (`SoundGain`). All four read out of `engine.dll`'s own
        // ConVar registrations, which is the only source that has them: the engine-side sound
        // cvars are absent from the whole SDK checkout.
        new("snd_refdist", "36", Replicated: false, Cheat: true),
        new("snd_refdb", "60", Replicated: false, Cheat: true),
        new("snd_gain", "1", Replicated: false, Cheat: true),
        new("snd_gain_min", "0.01", Replicated: false, Cheat: true),

        // **The watcher's own mix**, all `archive`: TF2's `help name`, 2026-10-14 (MixerConVarConformanceTests).
        new("volume", "1.0", Replicated: false, Cheat: false, Min: 0f, Max: 1f),
        new("snd_musicvolume", "1.0", Replicated: false, Cheat: false, Min: 0f, Max: 1f),
        new("snd_mute_losefocus", "1", Replicated: false, Cheat: false),
        new("voice_scale", "1", Replicated: false, Cheat: false),

        // **The client voice path's own** (`VoicePlan`), read from engine.dll's registrations (VoiceConVarConformanceTests).
        new("voice_enable", "1", Replicated: false, Cheat: false),
        new("voice_maxgain", "10", Replicated: false, Cheat: false),
        new("voice_avggain", "0.5", Replicated: false, Cheat: false),
        new("voice_buffer_ms", "100", Replicated: false, Cheat: false),
        new("voice_fadeouttime", "0.1", Replicated: false, Cheat: false),

        // Replicated, declared, and read by nothing yet — the shape is the point (D106).
        // `sv_downloadurl` is the one with a use waiting: a demo that carries it names where its
        // own map came from, which `MapDownloader` currently substitutes a public mirror for.
        new("sv_cheats", "0", Replicated: true, Cheat: false),
        new("host_timescale", "1", Replicated: true, Cheat: false),

        // **The default was "0" until 2026-09-11, copied from its neighbours above rather than
        // read.** `engine.dll`'s own registration — `FUN_18000a350` in the decompile, the only
        // function referencing the string "sv_downloadurl" — passes `&DAT_18035d128` as the
        // default, and that address holds one zero byte: the empty string, as a URL ConVar with
        // nothing configured would be. (D:\ghidra-proj, project tf2engine.)
        new("sv_downloadurl", "", Replicated: true, Cheat: false),

        // `tf_fx_shared.cpp:22`. Decides whether a shotgun's pellets follow `g_vecFixedWpnSpreadPellets` or the seed's
        // random draws (B415) — competitive servers set it, and `demostf-cp_process_f12` sends "1".
        new("tf_use_fixed_weaponspreads", "0", Replicated: true, Cheat: false),

        // Client-only: the watcher's, so the source is their config and the fallback is this.
        // src/game/client/vgui_fpspanel.cpp:28, clientleafsystem.cpp:32, c_baseplayer.cpp:118.
        new("cl_showpos", "0", Replicated: false, Cheat: false),
        new("cl_drawleaf", "-1", Replicated: false, Cheat: true),
        new("cl_first_person_uses_world_model", "0", Replicated: false, Cheat: false),

        // **Every other ConVar `ViewerSettings` implements**, declared so a set keeps its string as written, as the
        // engine does (B543). Defaults and bounds from the SDK line cited; engine-side ones (no SDK line) from TF2's own
        // `help <name>`, 2026-10-14, which prints `def.` and `min.`/`max.` (convar.cpp:1432-1436).
        new("viewmodel_fov", "54", Replicated: false, Cheat: false, Min: 0.1f, Max: 179.9f), // view.cpp:111
        new("viewmodel_fov_demo", "54", Replicated: false, Cheat: false),                    // tf/clientmode_tf.cpp:570
        new("developer", "0", Replicated: false, Cheat: false),                              // util_shared.cpp:45
        new("fps_max", "400", Replicated: false, Cheat: false),                              // engine; help
        new("engine_no_focus_sleep", "50", Replicated: false, Cheat: false),                 // engine; help
        new("mat_vsync", "0", Replicated: false, Cheat: false, Min: 0f, Max: 1f),            // engine; help
        new("cl_showfps", "0", Replicated: false, Cheat: false),                             // vgui_fpspanel.cpp:27
        new("r_3dsky", "1", Replicated: false, Cheat: false),                                // viewrender.cpp:113
        new("cl_detaildist", "1200", Replicated: false, Cheat: false),                       // detailobjectsystem.cpp:52
        new("cl_detailfade", "400", Replicated: false, Cheat: false),                        // detailobjectsystem.cpp:53
        new("r_drawviewmodel", "1", Replicated: false, Cheat: false),                        // viewrender.cpp:116
        new("mat_phong", "1", Replicated: false, Cheat: false),                              // engine; help

        // materialsystem.dll's registration, the default string at 0x1800b69b4 (finding 99; D229): threaded on 2+
        // physical cores (`MaterialQueueMode.Resolve`).
        new("mat_queue_mode", "-1", Replicated: false, Cheat: false),
        new("r_threaded_particles", "1", Replicated: false, Cheat: false),                   // particlemgr.cpp:1543
        new("cl_threaded_bone_setup", "0", Replicated: false, Cheat: false),                 // c_baseanimating.cpp:2743
        new("cl_drawmonitors", "1", Replicated: false, Cheat: false),                        // viewrender.cpp:168
        new("cl_flipviewmodels", "0", Replicated: false, Cheat: false, UserInfo: true),      // c_baseviewmodel.cpp:44
        new("tf_use_min_viewmodels", "0", Replicated: false, Cheat: false),                  // tf/tf_viewmodel.cpp:125
        new("mat_hdr_level", "2", Replicated: false, Cheat: false),                          // viewpostprocess.cpp:41
        new("spec_autodirector", "1", Replicated: false, Cheat: false),                      // hltvcamera.cpp:25
        new("mat_disable_bloom", "0", Replicated: false, Cheat: false),                      // viewpostprocess.cpp:57
        new("mat_force_tonemap_scale", "0.0", Replicated: false, Cheat: true),               // viewpostprocess.cpp:1128
        new("mat_specular", "1", Replicated: false, Cheat: false),                           // engine; help
        new("mat_fullbright", "0", Replicated: false, Cheat: true),                          // c_rope.cpp:82
        new("r_dopixelvisibility", "1", Replicated: false, Cheat: false),                    // c_pixel_visibility.cpp:24
        new("cl_drawhud", "1", Replicated: false, Cheat: true),                              // clientmode_shared.cpp:81
        new("hud_medichealtargetmarker", "0", Replicated: false, Cheat: false),              // tf/tf_weapon_medigun.cpp:205
        new("demo_fov_override", "0", Replicated: false, Cheat: false),                      // c_baseplayer.cpp:120
        new("r_decals", "2048", Replicated: false, Cheat: false),                            // EffectsClient.cpp:77
        new("r_maxmodeldecal", "50", Replicated: false, Cheat: false),                       // matsys_controls/mdlpanel.cpp:276
        new("crosshair", "1", Replicated: false, Cheat: false),                              // hud_crosshair.cpp:32
        new("cl_observercrosshair", "1", Replicated: false, Cheat: false),                   // hud_crosshair.cpp:33
        new("cl_crosshair_red", "200", Replicated: false, Cheat: false),                     // tf/tf_hud_crosshair.cpp:23
        new("cl_crosshair_green", "200", Replicated: false, Cheat: false),                   // tf/tf_hud_crosshair.cpp:24
        new("cl_crosshair_blue", "200", Replicated: false, Cheat: false),                    // tf/tf_hud_crosshair.cpp:25
        new("cl_crosshair_scale", "32.0", Replicated: false, Cheat: false),                  // tf/tf_hud_crosshair.cpp:29
        new("tf_hud_no_crosshair_on_scope_zoom", "0", Replicated: false, Cheat: false),      // tf/clientmode_tf.cpp:599

        // **What `CDemoPlayer::InterpolateViewpoint` reads** (`0x180072180`, B56). Engine ConVars, absent from the SDK:
        // `ConVar::ConVar` calls at 0x180003bea, 0x180003baa, 0x180003a2a and 0x180003c2a, every one with no flags —
        // the watcher's, never the server's. `DemoViewConVarConformanceTests` holds the registrations and the dump.
        new("demo_interpolateview", "1", Replicated: false, Cheat: false),
        new("demo_interplimit", "4000", Replicated: false, Cheat: false),
        new("demo_avellimit", "2000", Replicated: false, Cheat: false),
        new("demo_legacy_rollback", "1", Replicated: false, Cheat: false),

        // **Tournament mode, so `IsInTournamentMode` can be modelled instead of taken as off.**
        // All three `FCVAR_REPLICATED`, none `FCVAR_CHEAT` — a server sets them, not a player.
        // src/game/shared/teamplayroundbased_gamerules.cpp:202 and :227,
        // src/game/shared/tf/tf_gamerules.cpp:797.
        new("mp_tournament", "0", Replicated: true, Cheat: false),
        new("mp_tournament_stopwatch", "1", Replicated: true, Cheat: false),
        new("mp_winlimit", "0", Replicated: true, Cheat: false),
        new("mp_timelimit", "0", Replicated: true, Cheat: false), // multiplay_gamerules.cpp:86

        // src/game/shared/tf/tf_gamerules.cpp:782-783, which C_TFTeam::UpdateTeamName shows in a tournament.
        new("mp_tournament_redteamname", "RED", Replicated: true, Cheat: false),
        new("mp_tournament_blueteamname", "BLU", Replicated: true, Cheat: false),

        // `GetNextRespawnWave` and `GetRespawnWaveMaxLength` (teamplayroundbased_gamerules.cpp:200,233; c_baseplayer.cpp:112-113,
        // the non-CSTRIKE branch): what a revive marker's respawn time is built from.
        new("mp_respawnwavetime", "10.0", Replicated: true, Cheat: false),
        new("mp_disable_respawn_times", "0", Replicated: true, Cheat: false),
        new("spec_freeze_time", "4.0", Replicated: true, Cheat: true),
        new("spec_freeze_traveltime", "0.4", Replicated: true, Cheat: true),

        // `CTFPlayerShared::IsLoser` (tf_player_shared.cpp:127,13656): a cheat that forces every player's loser state.
        new("tf_always_loser", "0", Replicated: true, Cheat: true),

        // `CTFPlayer::CanPickupBuilding`'s training-mode branch (tf_gamerules.cpp:699-702): server-set, not a player's.
        new("training_can_pickup_sentry", "1", Replicated: true, Cheat: false),
        new("training_can_pickup_dispenser", "1", Replicated: true, Cheat: false),
        new("training_can_pickup_tele_entrance", "1", Replicated: true, Cheat: false),
        new("training_can_pickup_tele_exit", "1", Replicated: true, Cheat: false),

        // The HUD's client cvars: the watcher's config sets them, never the demo.
        new("hud_deathnotice_time", "6", Replicated: false, Cheat: false),                     // tf/hud_basedeathnotice.cpp:31
        new("hud_saytext_time", "12", Replicated: false, Cheat: false),                        // hud_basechat.cpp:37
        new("cl_chatfilters", "63", Replicated: false, Cheat: false),                          // hud_basechat.cpp:39
        new("cl_hud_minmode", "0", Replicated: false, Cheat: false),                           // tf/clientmode_tf.cpp:288
        new("cl_use_tournament_specgui", "0", Replicated: false, Cheat: false),                // tf/vgui/tf_spectatorgui.cpp:77
        new("cl_hud_killstreak_display_time", "3", Replicated: false, Cheat: false),           // tf/tf_hud_deathnotice.cpp:53
        new("cl_hud_killstreak_display_fontsize", "0", Replicated: false, Cheat: false),       // tf/tf_hud_deathnotice.cpp:54
        new("cl_hud_killstreak_display_alpha", "120", Replicated: false, Cheat: false),        // tf/tf_hud_deathnotice.cpp:55
        new("tf_use_match_hud", "1", Replicated: false, Cheat: false),                         // tf/tf_hud_match_status.cpp:33
        new("tf_hud_show_servertimelimit", "0", Replicated: false, Cheat: false),              // tf/tf_hud_objectivestatus.cpp:42
        new("cl_hud_playerclass_use_playermodel", "1", Replicated: false, Cheat: false),       // tf/tf_hud_playerstatus.cpp:39
        new("tf_hud_target_id_disable_floating_health", "0", Replicated: false, Cheat: false), // tf/tf_hud_target_id.cpp:72
        new("tf_hud_target_id_alpha", "100", Replicated: false, Cheat: false),                 // tf/tf_hud_target_id.cpp:73
        new("tf_hud_target_id_offset", "0", Replicated: false, Cheat: false),                  // tf/tf_hud_target_id.cpp:74
    ];

    private static readonly Dictionary<string, EngineConVar> ByEngineName =
        BuildIndex(Declarations);

    /// <summary>Every ConVar this project depends on the value of.</summary>
    public static IReadOnlyList<EngineConVar> All => Declarations;

    /// <summary>The declaration for one ConVar.</summary>
    /// <param name="name">The engine's name for it.</param>
    /// <returns>What Valve declares.</returns>
    /// <exception cref="KeyNotFoundException">Nothing here declares that name.</exception>
    /// <remarks>
    /// **Throws for an unknown name rather than inventing a declaration.** A caller asking for a
    /// ConVar nobody declared has a typo or a missing entry, and both are defects; handing back an
    /// empty default would turn either into a plausible number somewhere far away.
    ///
    /// Ordinal comparison, because ConVar names are engine identifiers rather than words — the same
    /// reason the wire compares them byte for byte.
    /// </remarks>
    public static EngineConVar ByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByEngineName.TryGetValue(name, out EngineConVar? found)
            ? found
            : throw new KeyNotFoundException(
                $"no engine ConVar named '{name}' is declared here; add it to EngineConVars with " +
                "its default read from the SDK and from cvarlist.log");
    }

    /// <summary>The declaration for one ConVar, when there is one.</summary>
    /// <param name="name">The engine's name for it.</param>
    /// <param name="declared">What Valve declares, or null.</param>
    /// <returns>Whether anything here declares that name.</returns>
    /// <remarks>
    /// **For the caller that has a name off the wire rather than one it chose.** A demo sends
    /// whatever the server changed — forty values on a real match, against the eight declared here —
    /// so asking about an arbitrary name is ordinary rather than a defect, and
    /// <see cref="ByName"/>'s throw would be wrong for it.
    /// </remarks>
    public static bool TryByName(
        string name, [NotNullWhen(true)] out EngineConVar? declared)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByEngineName.TryGetValue(name, out declared);
    }

    private static Dictionary<string, EngineConVar> BuildIndex(EngineConVar[] declarations)
    {
        Dictionary<string, EngineConVar> index = new(declarations.Length, StringComparer.Ordinal);

        foreach (EngineConVar declaration in declarations)
        {
            index.Add(declaration.Name, declaration);
        }

        return index;
    }
}
