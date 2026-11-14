using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`GetMapDisplayName` (src/game/shared/tf/tf_gamerules.cpp:18974): a level's name as the HUD prints it.</summary>
/// <remarks>
/// A shipped map is looked up in `s_ValveMaps` (:264) and then `s_CommunityMaps` (:300); anything else has `_final` or
/// `_final1` cut from its end, a `workshop/` prefix and `.ugc&lt;digits&gt;` suffix dropped, its game-mode prefix stripped,
/// underscores made spaces, and is upper-cased — "tf maps are LOUD" (:19101). The title-case branch is not ported: no
/// caller here asks for it.
/// </remarks>
public static class TfMapDisplayName
{
    private const string Final = "_final";
    private const string Workshop = "workshop/";
    private const string Ugc = ".ugc";

    // `s_ValveMaps` then `s_CommunityMaps` (:264-436), disk name to display name; the game-type column is unread here.
    private static readonly Dictionary<string, string> Shipped = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctf_2fort"] = "2Fort", ["cp_dustbowl"] = "Dustbowl", ["cp_granary"] = "Granary", ["cp_well"] = "Well",
        ["cp_foundry"] = "Foundry", ["cp_gravelpit"] = "Gravel Pit", ["tc_hydro"] = "Hydro", ["ctf_well"] = "Well",
        ["cp_badlands"] = "Badlands", ["pl_goldrush"] = "Gold Rush", ["pl_badwater"] = "Badwater Basin",
        ["plr_pipeline"] = "Pipeline", ["cp_gorge"] = "Gorge", ["ctf_doublecross"] = "Double Cross",
        ["pl_thundermountain"] = "Thunder Mountain", ["tr_target"] = "Target", ["tr_dustbowl"] = "Dustbowl",
        ["cp_manor_event"] = "Mann Manor", ["cp_mountainlab"] = "Mountain Lab", ["cp_degrootkeep"] = "DeGroot Keep",
        ["pl_barnblitz"] = "Barnblitz", ["pl_upward"] = "Upward", ["plr_hightower"] = "Hightower", ["koth_viaduct"] = "Viaduct",
        ["koth_viaduct_event"] = "Eyeaduct", ["koth_king"] = "Kong King", ["koth_lakeside_event"] = "Ghost Fort",
        ["plr_hightower_event"] = "Helltower", ["rd_asteroid"] = "Asteroid", ["pl_cactuscanyon"] = "Cactus Canyon",
        ["sd_doomsday"] = "Doomsday", ["sd_doomsday_event"] = "Carnival of Carnage", ["cp_mercenarypark"] = "Mercenary Park",

        ["pl_borneo"] = "Borneo", ["koth_suijin"] = "Suijin", ["cp_snowplow"] = "Snowplow", ["koth_probed"] = "Probed",
        ["pd_watergate"] = "Watergate", ["arena_byre"] = "Byre", ["ctf_2fort_invasion"] = "2Fort Invasion",
        ["cp_sunshine_event"] = "Sinshine", ["pl_millstone_event"] = "Hellstone", ["cp_gorge_event"] = "Gorge Event",
        ["koth_moonshine_event"] = "Moonshine Event", ["pl_snowycoast"] = "Snowycoast", ["cp_vanguard"] = "Vanguard",
        ["ctf_landfall"] = "Landfall", ["koth_highpass"] = "Highpass", ["koth_maple_ridge_event"] = "Maple Ridge Event",
        ["pl_fifthcurve_event"] = "Brimstone", ["pd_pit_of_death_event"] = "Pit of Death", ["cp_mossrock"] = "Mossrock",
        ["koth_lazarus"] = "Lazarus", ["plr_bananabay"] = "Banana Bay", ["pl_enclosure_final"] = "Enclosure",
        ["koth_brazil"] = "Brazil", ["koth_bagel_event"] = "Cauldron", ["pl_rumble_event"] = "Gravestone",
        ["koth_slasher"] = "Slasher", ["pd_cursed_cove_event"] = "Cursed Cove", ["pd_monster_bash"] = "Monster Bash",
        ["koth_slaughter_event"] = "Laughter", ["pl_precipice_event_final"] = "Precipice", ["koth_megalo"] = "Megalo",
        ["pl_hasslecastle"] = "Hassle Castle", ["pl_bloodwater"] = "Bloodwater", ["koth_undergrove_event"] = "Moldergrove",
        ["pl_pier"] = "Pier", ["pd_snowville_event"] = "SnowVille", ["ctf_snowfall_final"] = "Snowfall",
        ["pl_wutville_event"] = "Wutville", ["pd_farmageddon"] = "Farmageddon", ["koth_los_muertos"] = "Los Muertos",
        ["cp_ambush_event"] = "Erebus", ["pl_terror_event"] = "Terror", ["arena_lumberyard_event"] = "Graveyard",
        ["koth_synthetic_event"] = "Sinthetic", ["pl_coal_event"] = "Polar", ["pl_breadspace"] = "Bread Space",
        ["pl_chilly"] = "Chilly", ["koth_cascade"] = "Cascade", ["cp_altitude"] = "Altitude",
        ["ctf_doublecross_snowy"] = "Doublefrost", ["ctf_crasher"] = "Crasher!", ["ctf_helltrain_event"] = "Helltrain",
        ["pl_sludgepit_event"] = "Ghoulpit", ["cp_spookeyridge"] = "Spookeyridge", ["koth_sawmill_event"] = "Soul-Mill",
        ["plr_hacksaw_event"] = "Bonesaw", ["cp_frostwatch"] = "Frostwatch", ["pl_frostcliff"] = "Frostcliff",
        ["pl_rumford_event"] = "Rumford", ["ctf_frosty"] = "Frosty", ["cp_gravelpit_snowy"] = "Coal Pit",
        ["koth_sharkbay"] = "Sharkbay", ["koth_rotunda"] = "Rotunda", ["pl_phoenix"] = "Phoenix", ["pl_cashworks"] = "Cashworks",
        ["pl_venice"] = "Venice", ["cp_reckoner"] = "Reckoner", ["cp_sulfur"] = "Sulfur", ["cp_hardwood_final"] = "Hardwood",
        ["ctf_pelican_peak"] = "Pelican Peak", ["pd_selbyen"] = "Selbyen", ["vsh_tinyrock"] = "Tiny Rock",
        ["vsh_distillery"] = "Distillery", ["vsh_skirmish"] = "Skirmish", ["vsh_nucleus"] = "Nucleus VSH",
        ["arena_perks"] = "Perks", ["koth_slime"] = "Slime", ["cp_lavapit_final"] = "Lava Pit",
        ["pd_mannsylvania"] = "Mannsylvania", ["cp_degrootkeep_rats"] = "Sandcastle", ["pl_spineyard"] = "Spineyard",
        ["pl_corruption"] = "Corruption", ["zi_murky"] = "Murky", ["zi_atoll"] = "Atoll", ["zi_woods"] = "Woods",
        ["zi_sanitarium"] = "Sanitarium", ["zi_devastation_final1"] = "Devastation", ["koth_snowtower"] = "Snowtower",
        ["koth_krampus"] = "Krampus", ["ctf_haarp"] = "Haarp", ["cp_brew"] = "Brew", ["plr_hacksaw"] = "Hacksaw",
        ["ctf_turbine_winter"] = "Turbine Event", ["cp_carrier"] = "Carrier", ["pd_galleria"] = "Galleria",
        ["pl_emerge"] = "Emerge", ["pl_camber"] = "Camber", ["pl_embargo"] = "Embargo", ["pl_odyssey"] = "Odyssey",
        ["koth_megaton"] = "Megaton", ["koth_cachoeira"] = "Cachoeira", ["cp_overgrown"] = "Overgrown", ["cp_hadal"] = "Hadal",
        ["ctf_applejack"] = "Applejack", ["pd_atom_smash"] = "Atom Smash", ["cp_canaveral_5cp"] = "Canaveral",
        ["cp_burghausen"] = "Burghausen", ["koth_toxic"] = "Toxic", ["cp_darkmarsh"] = "Darkmarsh",
        ["cp_freaky_fair"] = "Freaky Fair", ["tow_dynamite"] = "Dynamite", ["pd_circus"] = "Circus", ["vsh_outburst"] = "Outburst",
        ["zi_blazehattan"] = "Blazehattan", ["koth_overcast_final"] = "Overcast", ["cp_fortezza"] = "Fortezza",
        ["ctf_penguin_peak"] = "Penguin Peak", ["pl_patagonia"] = "Patagonia", ["plr_cutter"] = "Cutter", ["vsh_maul"] = "Maul",
        ["pl_citadel"] = "Citadel", ["pl_aquarius"] = "Aquarius", ["cp_fulgur"] = "Fulgur", ["cp_cargo"] = "Cargo",
        ["cp_conifer"] = "Conifer", ["koth_boardwalk"] = "Boardwalk", ["koth_blowout"] = "Blowout", ["koth_mannhole"] = "Mannhole",
        ["koth_demolition"] = "Demolition", ["ctf_pressure"] = "Pressure", ["cp_cowerhouse"] = "Cowerhouse",
        ["koth_dusker"] = "Dusker", ["arena_afterlife"] = "Afterlife", ["ctf_doublecross_event"] = "Devilcross",
        ["sd_marshlands"] = "Marshlands",
    };

    // The prefixes stripped in order (:19048-19081); `raid_` is under `TF_RAID_MODE`, which TF2 does not build with.
    private const string KothPrefix = "koth_";
    private const string PassPrefix = "pass_";

    private static readonly string[] Prefixes =
        ["cp_", "tc_", "pl_", "ad_", "sd_", "rd_", "pd_", "ctf_", "plr_", KothPrefix, PassPrefix, "mvm_", "arena_"];

    /// <summary>`GetMapDisplayName( mapName, false )`.</summary>
    /// <param name="mapName">The level's base name, as `Q_FileBase` leaves it.</param>
    /// <returns>The display name.</returns>
    public static string Of(string mapName)
    {
        ArgumentNullException.ThrowIfNull(mapName);

        // `Q_strlower`, ASCII only.
        string name = string.Create(mapName.Length, mapName, static (span, source) =>
        {
            for (int index = 0; index < span.Length; index++)
            {
                span[index] = char.IsAsciiLetterUpper(source[index]) ? (char)(source[index] + 32) : source[index];
            }
        });

        if (Shipped.TryGetValue(name, out string? shipped))
        {
            return shipped;
        }

        // "truncate the _final (or _final1) part of the filename if it's at the end of the name" — at the FIRST `_final`.
        int final = name.IndexOf(Final, StringComparison.Ordinal);

        if (final >= 0 && name.AsSpan(final + Final.Length) is "" or "1")
        {
            name = name[..final];
        }

        // `koth_` and `pass_` are tested against `szTempName`, not `pszSrc` (:19063): a workshop KOTH map keeps its prefix.
        string beforeWorkshop = name;

        if (name.StartsWith(Workshop, StringComparison.Ordinal))
        {
            name = name[Workshop.Length..];
            int ugc = name.IndexOf(Ugc, StringComparison.Ordinal);

            // Only a `.ugc` followed by digits to the end, and at least one of them.
            if (ugc >= 0 && name.Length - ugc > Ugc.Length && !name.AsSpan(ugc + Ugc.Length).ContainsAnyExceptInRange('0', '9'))
            {
                name = name[..ugc];
            }
        }

        foreach (string prefix in Prefixes)
        {
            string tested = prefix is KothPrefix or PassPrefix ? beforeWorkshop : name;

            if (tested.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        // Underscores to spaces, then `Q_strupr`, ASCII only.
        // Not `System.Text.Ascii.ToUpper`: it stops at the first non-ASCII char (OperationStatus.InvalidData), where
        // `Q_strupr` passes it through — `MapDisplayName_ALevel_IsGetMapDisplayNamesAnswer("CP_CAFé_final", …)` (B733).
        return string.Create(name.Length, name, static (span, source) =>
        {
            for (int index = 0; index < span.Length; index++)
            {
                char c = source[index] == '_' ? ' ' : source[index];

                span[index] = char.IsAsciiLetterLower(c) ? (char)(c - 32) : c;
            }
        });
    }
}
