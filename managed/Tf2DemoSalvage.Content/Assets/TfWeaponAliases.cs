using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>TF2's own weapon names, indexed by the id a temp entity puts on the wire.</summary>
/// <remarks>
/// **`g_aWeaponNames`, `tf_shareddefs.cpp:599`**, read by `WeaponIdToAlias` (`:1224`):
///
/// <code>
/// const char *WeaponIdToAlias( int iWeapon )
/// {
///     COMPILE_TIME_ASSERT( TF_WEAPON_COUNT == ARRAYSIZE( g_aWeaponNames ) );
///     if ( ( iWeapon &gt;= ARRAYSIZE( g_aWeaponNames ) ) || ( iWeapon &lt; 0 ) )
///         return NULL;
///     return g_aWeaponNames[iWeapon];
/// }
/// </code>
///
/// **The alias IS the script's file name.** `ReadWeaponDataFromFileForSlot` (`weapon_parse.cpp:286`) builds
/// `scripts/&lt;alias&gt;` from it and reads that, so `22` reaches `scripts/TF_WEAPON_ROCKETLAUNCHER.txt` — the same file a
/// case-insensitive archive serves as `tf_weapon_rocketlauncher.txt`. Nothing translates the case.
///
/// **This exists because a temp entity has no entity to ask.** Everywhere else this project deliberately keys on the
/// weapon's SERVER CLASS, which a demo carries and which is one-to-one with the id
/// (<see cref="WeaponScriptName"/>). `CTETFExplosion` and `CTEFireBullets` carry the bare integer and no handle to a
/// weapon at all, so the table has to be reproduced.
///
/// **Valve's typo is kept.** Index 109 is `TF_WEPON_FLAME_BALL`, missing its A, and it is the name the engine looks
/// the file up by — correcting it here would look tidy and find nothing.
///
/// **The indices are stable across eras, and that is Valve's own written rule rather than an inference.** An id is a
/// bare integer on the wire, so the demo's embedded schema cannot date it the way it dates a send-table field
/// (`docs/memory/the-demo-dates-its-own-fields.md`) — which would make an old demo's explosions name the wrong
/// weapon with nothing to report it. `tf_shareddefs.h:401` says not to:
///
/// <code>
/// // NOTE: Inserting to most or all of the enums in this file will BREAK DEMOS -
/// // please add to the end instead.
/// enum ETFWeaponType
/// </code>
///
/// and again at `:519`, immediately above `TF_WEAPON_COUNT`: *"ADD NEW WEAPONS HERE TO AVOID BREAKING DEMOS"*. So a
/// demo recorded against a shorter table reads correctly here, and one recorded against a LONGER one — a build newer
/// than this file — names an id past the end, which <see cref="Of"/> answers with null rather than a wrong weapon.
///
/// *Not established*: whether Valve ever broke their own rule. `TF_WEAPON_GRENADE_JAR_MILK` sits at 40 among the 2007
/// grenades while Mad Milk shipped in 2010, which is either a slot reserved from the start or one insertion — and
/// telling those apart needs a period `tf_shareddefs.h`, which neither `hl2sdk-tf2` nor `source-sdk-2013` is.
/// </remarks>
public static class TfWeaponAliases
{
    /// <summary>Every alias, in the wire's own order — index is <c>m_iWeaponID</c>.</summary>
    private static readonly string[] Names =
    [
        "TF_WEAPON_NONE",
        "TF_WEAPON_BAT",
        "TF_WEAPON_BAT_WOOD",
        "TF_WEAPON_BOTTLE",
        "TF_WEAPON_FIREAXE",
        "TF_WEAPON_CLUB",
        "TF_WEAPON_CROWBAR",
        "TF_WEAPON_KNIFE",
        "TF_WEAPON_FISTS",
        "TF_WEAPON_SHOVEL",
        "TF_WEAPON_WRENCH",
        "TF_WEAPON_BONESAW",
        "TF_WEAPON_SHOTGUN_PRIMARY",
        "TF_WEAPON_SHOTGUN_SOLDIER",
        "TF_WEAPON_SHOTGUN_HWG",
        "TF_WEAPON_SHOTGUN_PYRO",
        "TF_WEAPON_SCATTERGUN",
        "TF_WEAPON_SNIPERRIFLE",
        "TF_WEAPON_MINIGUN",
        "TF_WEAPON_SMG",
        "TF_WEAPON_SYRINGEGUN_MEDIC",
        "TF_WEAPON_TRANQ",
        "TF_WEAPON_ROCKETLAUNCHER",
        "TF_WEAPON_GRENADELAUNCHER",
        "TF_WEAPON_PIPEBOMBLAUNCHER",
        "TF_WEAPON_FLAMETHROWER",
        "TF_WEAPON_GRENADE_NORMAL",
        "TF_WEAPON_GRENADE_CONCUSSION",
        "TF_WEAPON_GRENADE_NAIL",
        "TF_WEAPON_GRENADE_MIRV",
        "TF_WEAPON_GRENADE_MIRV_DEMOMAN",
        "TF_WEAPON_GRENADE_NAPALM",
        "TF_WEAPON_GRENADE_GAS",
        "TF_WEAPON_GRENADE_EMP",
        "TF_WEAPON_GRENADE_CALTROP",
        "TF_WEAPON_GRENADE_PIPEBOMB",
        "TF_WEAPON_GRENADE_SMOKE_BOMB",
        "TF_WEAPON_GRENADE_HEAL",
        "TF_WEAPON_GRENADE_STUNBALL",
        "TF_WEAPON_GRENADE_JAR",
        "TF_WEAPON_GRENADE_JAR_MILK",
        "TF_WEAPON_PISTOL",
        "TF_WEAPON_PISTOL_SCOUT",
        "TF_WEAPON_REVOLVER",
        "TF_WEAPON_NAILGUN",
        "TF_WEAPON_PDA",
        "TF_WEAPON_PDA_ENGINEER_BUILD",
        "TF_WEAPON_PDA_ENGINEER_DESTROY",
        "TF_WEAPON_PDA_SPY",
        "TF_WEAPON_BUILDER",
        "TF_WEAPON_MEDIGUN",
        "TF_WEAPON_GRENADE_MIRVBOMB",
        "TF_WEAPON_FLAMETHROWER_ROCKET",
        "TF_WEAPON_GRENADE_DEMOMAN",
        "TF_WEAPON_SENTRY_BULLET",
        "TF_WEAPON_SENTRY_ROCKET",
        "TF_WEAPON_DISPENSER",
        "TF_WEAPON_INVIS",
        "TF_WEAPON_FLAREGUN",
        "TF_WEAPON_LUNCHBOX",
        "TF_WEAPON_JAR",
        "TF_WEAPON_COMPOUND_BOW",
        "TF_WEAPON_BUFF_ITEM",
        "TF_WEAPON_PUMPKIN_BOMB",
        "TF_WEAPON_SWORD",
        "TF_WEAPON_ROCKETLAUNCHER_DIRECTHIT",
        "TF_WEAPON_LIFELINE",
        "TF_WEAPON_LASER_POINTER",
        "TF_WEAPON_DISPENSER_GUN",
        "TF_WEAPON_SENTRY_REVENGE",
        "TF_WEAPON_JAR_MILK",
        "TF_WEAPON_HANDGUN_SCOUT_PRIMARY",
        "TF_WEAPON_BAT_FISH",
        "TF_WEAPON_CROSSBOW",
        "TF_WEAPON_STICKBOMB",
        "TF_WEAPON_HANDGUN_SCOUT_SECONDARY",
        "TF_WEAPON_SODA_POPPER",
        "TF_WEAPON_SNIPERRIFLE_DECAP",
        "TF_WEAPON_RAYGUN",
        "TF_WEAPON_PARTICLE_CANNON",
        "TF_WEAPON_MECHANICAL_ARM",
        "TF_WEAPON_DRG_POMSON",
        "TF_WEAPON_BAT_GIFTWRAP",
        "TF_WEAPON_GRENADE_ORNAMENT_BALL",
        "TF_WEAPON_FLAREGUN_REVENGE",
        "TF_WEAPON_PEP_BRAWLER_BLASTER",
        "TF_WEAPON_CLEAVER",
        "TF_WEAPON_GRENADE_CLEAVER",
        "TF_WEAPON_STICKY_BALL_LAUNCHER",
        "TF_WEAPON_GRENADE_STICKY_BALL",
        "TF_WEAPON_SHOTGUN_BUILDING_RESCUE",
        "TF_WEAPON_CANNON",
        "TF_WEAPON_THROWABLE",
        "TF_WEAPON_GRENADE_THROWABLE",
        "TF_WEAPON_PDA_SPY_BUILD",
        "TF_WEAPON_GRENADE_WATERBALLOON",
        "TF_WEAPON_HARVESTER_SAW",
        "TF_WEAPON_SPELLBOOK",
        "TF_WEAPON_SPELLBOOK_PROJECTILE",
        "TF_WEAPON_SNIPERRIFLE_CLASSIC",
        "TF_WEAPON_PARACHUTE",
        "TF_WEAPON_GRAPPLINGHOOK",
        "TF_WEAPON_PASSTIME_GUN",
        "TF_WEAPON_CHARGED_SMG",
        "TF_WEAPON_BREAKABLE_SIGN",
        "TF_WEAPON_ROCKETPACK",
        "TF_WEAPON_SLAP",
        "TF_WEAPON_JAR_GAS",
        "TF_WEAPON_GRENADE_JAR_GAS",
        "TF_WEPON_FLAME_BALL",
    ];

    /// <summary>Every alias, in wire order.</summary>
    public static IReadOnlyList<string> All => Names;

    /// <summary>The alias a weapon id names.</summary>
    /// <param name="weaponId"><c>m_iWeaponID</c>, as a temp entity sends it.</param>
    /// <returns>The alias, or <c>null</c> for an id outside the table.</returns>
    /// <remarks>
    /// **Out of range answers null rather than throwing**, which is `WeaponIdToAlias`'s own behaviour — and it is the
    /// case a demo from a newer build than this table would take.
    /// </remarks>
    public static string? Of(int weaponId) =>
        (uint)weaponId < (uint)Names.Length ? Names[weaponId] : null;

    /// <summary>The alias whose weapon script an explosion should be read from.</summary>
    /// <param name="weaponId"><c>m_iWeaponID</c> off the wire.</param>
    /// <returns>The alias to look up, or <c>null</c> when the id names no weapon.</returns>
    /// <remarks>
    /// **`TFExplosionCallback` does not look the weapon up directly** (`tf_fx_explosions.cpp:45-59`): four ids are
    /// remapped first, because the thing that exploded has no script of its own.
    ///
    /// <code>
    /// case TF_WEAPON_GRENADE_PIPEBOMB:
    /// case TF_WEAPON_GRENADE_DEMOMAN:
    /// case TF_WEAPON_PUMPKIN_BOMB:
    ///     pWeaponInfo = GetTFWeaponInfo( TF_WEAPON_PIPEBOMBLAUNCHER );  break;
    /// case TF_WEAPON_FLAMETHROWER_ROCKET:
    ///     pWeaponInfo = GetTFWeaponInfo( TF_WEAPON_FLAMETHROWER );      break;
    /// </code>
    ///
    /// A demoman's stickies are the commonest explosion in a match, so skipping this would take the default
    /// `ExplosionCore_wall` for thousands of blasts that have their own effect.
    /// </remarks>
    public static string? ForExplosion(int weaponId) => Of(weaponId switch
    {
        GrenadePipebomb or GrenadeDemoman or PumpkinBomb => PipebombLauncher,
        FlamethrowerRocket => Flamethrower,
        _ => weaponId,
    });

    /// <summary>A demoman's thrown pipe, which reads the pipebomb launcher's script.</summary>
    private const int GrenadePipebomb = 35;

    /// <summary>A demoman's sticky, likewise.</summary>
    private const int GrenadeDemoman = 53;

    /// <summary>A Halloween pumpkin, likewise — and the one weapon whose explosion sound is fixed.</summary>
    public const int PumpkinBomb = 63;

    /// <summary>The detonator's flare, which reads the flamethrower's script.</summary>
    private const int FlamethrowerRocket = 52;

    /// <summary>What the three grenade ids are remapped TO.</summary>
    private const int PipebombLauncher = 24;

    /// <summary>What the flame rocket is remapped to.</summary>
    private const int Flamethrower = 25;
}
