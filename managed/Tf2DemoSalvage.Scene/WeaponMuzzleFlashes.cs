using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>What one weapon's muzzle flash starts — `CTFWeaponBase::CreateMuzzleFlashEffects` (B415).</summary>
/// <param name="Particle">The particle system on its `muzzle` attachment, or null.</param>
/// <param name="Model">The muzzle flash model, or null.</param>
/// <param name="ModelLifetime">How long the model lives, `MuzzleFlashModelDuration`.</param>
/// <param name="Backblast">
/// Whether it also starts `rocketbackblast` on the weapon's own `backblast` attachment — `CTFRocketLauncher`'s override.
/// </param>
public readonly record struct WeaponMuzzleFlash(string? Particle, string? Model, float ModelLifetime, bool Backblast = false);

/// <summary>Resolves a weapon's muzzle flash from its item and script (B415).</summary>
/// <remarks>
/// <code>
/// // tf_weaponbase.cpp:2816, :2801, :2841
/// particle = item's muzzle_flash( team ) ?? script "MuzzleFlashParticleEffect"          // empty is none
/// model    = script "MuzzleFlashModel";  lifetime = script "MuzzleFlashModelDuration", default 0.2
/// // CreateMuzzleFlashEffects: nothing unless the model has a "muzzle" attachment; then
/// //   DispatchParticleEffect( particle, PATTACH_POINT_FOLLOW, model, "muzzle" )
/// </code>
/// The 3rd-person dispatch effect `GetMuzzleFlashEffectName_3rd` names is null for every TF weapon — the base returns
/// null and no TF class overrides it — so there is none. A rocket launcher then adds its backblast:
/// <code>
/// // tf_weapon_rocketlauncher.cpp:378 — not for the local player
/// ParticleProp()->Create( "rocketbackblast", PATTACH_POINT_FOLLOW, "backblast" );
/// </code>
/// </remarks>
/// <param name="readFile">Reads a file from the game's archives.</param>
/// <param name="items">The item schema, or null when it did not load.</param>
public sealed class WeaponMuzzleFlashes(Func<string, byte[]?> readFile, ItemSchema? items)
{
    /// <summary>`m_flMuzzleFlashModelDuration`'s default (`tf_weapon_parse.cpp:187`).</summary>
    public const float DefaultModelLifetime = 0.2f;

    /// <summary>The system `CTFRocketLauncher::CreateMuzzleFlashEffects` adds, and the attachment it follows.</summary>
    public const string BackblastSystem = "rocketbackblast", BackblastAttachment = "backblast";

    /// <summary>
    /// The classes that inherit `CTFRocketLauncher`'s override (`tf_weapon_rocketlauncher.cpp:378`), by the name
    /// `LINK_ENTITY_TO_CLASS` gives them, which is their `item_class` — every subclass but `tf_weapon_particle_cannon`,
    /// whose own override skips "back blast effects".
    /// </summary>
    private static readonly HashSet<string> Backblasting = new(StringComparer.OrdinalIgnoreCase)
    {
        "tf_weapon_rocketlauncher", "tf_weapon_rocketlauncher_directhit", "tf_weapon_rocketlauncher_airstrike",
        "tf_weapon_crossbow", "tf_weapon_grapplinghook", "tf_weapon_raygun", "tf_weapon_drg_pomson",
    };

    private readonly Dictionary<string, WeaponScript?> _scripts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a weapon's flash starts.</summary>
    /// <param name="item">The weapon's item definition.</param>
    /// <param name="team">The weapon's team.</param>
    /// <returns>The particle and model, either of which may be absent.</returns>
    public WeaponMuzzleFlash For(int? item, int team)
    {
        if (item is not { } definition || items?.ItemClass(definition) is not { Length: > 0 } itemClass)
        {
            return new WeaponMuzzleFlash(null, null, DefaultModelLifetime);
        }

        if (!_scripts.TryGetValue(itemClass, out WeaponScript? script))
        {
            script = WeaponScript.Read(readFile, itemClass);
            _scripts[itemClass] = script;
        }

        string? particle = items.MuzzleFlash(definition, team) ??
                           (script?.Value("MuzzleFlashParticleEffect") is { Length: > 0 } named ? named : null);
        string? model = script?.Value("MuzzleFlashModel") is { Length: > 0 } path ? path : null;
        float lifetime = float.TryParse(
            script?.Value("MuzzleFlashModelDuration"), NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
            ? read
            : DefaultModelLifetime;

        return new WeaponMuzzleFlash(particle, model, lifetime, Backblasting.Contains(itemClass));
    }
}
