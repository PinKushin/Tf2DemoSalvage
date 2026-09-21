using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which particle system a blast draws — <c>TFExplosionCallback</c>'s own order (B415).</summary>
/// <remarks>
/// **Synthetic scripts rather than the game's**, so each test knows the right answer because it put the value
/// there (D38). Whether TF2's shipped rocket launcher really declares `ExplosionCore_MidAir` is a separate claim,
/// answered by the `weapon-script` probe and recorded in `docs/findings/58` — and it is a claim about Valve, which
/// a test here could not establish.
///
/// **The branch order is the whole subject.** `tf_fx_explosions.cpp:96-128` is a custom index, then water, then
/// player-or-air, then the wall — each falling back to `ExplosionCore_wall` when the script's string is empty
/// rather than to the branch below it.
/// </remarks>
public sealed class ExplosionEffectsConformanceTests
{
    /// <summary>The soldier's, whose id needs no remapping.</summary>
    private const int RocketLauncher = 22;

    /// <summary>A demoman's sticky, which has no script of its own and reads the pipebomb launcher's.</summary>
    private const int GrenadeDemoman = 53;

    /// <summary>`TF_WEAPON_PIPEBOMBLAUNCHER`, the alias the three grenade ids remap to.</summary>
    private const string PipebombLauncher = "TF_WEAPON_PIPEBOMBLAUNCHER";

    /// <summary>`TF_WEAPON_ROCKETLAUNCHER`.</summary>
    private const string RocketLauncherAlias = "TF_WEAPON_ROCKETLAUNCHER";

    [Test]
    public void NameFor_ABlastAgainstAWall_IsTheScriptsExplosionEffect()
    {
        Effects(RocketLauncherAlias, wall: "wall_fx", player: "air_fx")
            .NameFor(Blast(RocketLauncher, normal: (0f, 0f, 1f)), struckPlayer: false)
            .ShouldBe("wall_fx");
    }

    /// <remarks>
    /// **Mid air shares the PLAYER branch**, which is the detail the whole weapon-script layer exists for: the
    /// rocket launcher's `ExplosionEffect` is the same string as the default, so only this branch can tell a
    /// correct implementation from one that never read a script at all.
    /// </remarks>
    [Test]
    public void NameFor_ABlastInMidAir_IsTheScriptsPlayerEffect()
    {
        Effects(RocketLauncherAlias, wall: "wall_fx", player: "air_fx")
            .NameFor(Blast(RocketLauncher, normal: (0.01f, -0.02f, 0.0f)), struckPlayer: false)
            .ShouldBe("air_fx");
    }

    /// <remarks>A blast on a wall that hit a player takes the same branch, by the `||`.</remarks>
    [Test]
    public void NameFor_ABlastThatStruckAPlayer_IsTheScriptsPlayerEffect()
    {
        Effects(RocketLauncherAlias, wall: "wall_fx", player: "air_fx")
            .NameFor(Blast(RocketLauncher, normal: (0f, 0f, 1f)), struckPlayer: true)
            .ShouldBe("air_fx");
    }

    /// <remarks>
    /// **Water is an `else if` ahead of player-or-air, not a modifier on it.** A blast underwater that also hit a
    /// player takes the water effect, and a reader that tested water last would answer the player's.
    /// </remarks>
    [Test]
    public void NameFor_ABlastUnderwaterThatStruckAPlayer_IsTheScriptsWaterEffect()
    {
        Effects(RocketLauncherAlias, wall: "wall_fx", player: "air_fx", water: "wet_fx")
            .NameFor(Blast(RocketLauncher, normal: (0f, 0f, 1f)), struckPlayer: true, inWater: true)
            .ShouldBe("wet_fx");
    }

    /// <remarks>
    /// **An empty string in the script falls to the DEFAULT, not to the next branch.** `Q_strlen( … ) > 0` guards
    /// each assignment separately, so a weapon declaring only a wall effect gets `ExplosionCore_wall` in mid air —
    /// not its own wall effect.
    /// </remarks>
    [Test]
    public void NameFor_AScriptMissingThatBranchsEffect_IsTheDefault()
    {
        Effects(RocketLauncherAlias, wall: "wall_fx")
            .NameFor(Blast(RocketLauncher, normal: (0f, 0f, 0f)), struckPlayer: false)
            .ShouldBe(ExplosionEffects.DefaultEffect);
    }

    /// <remarks>A weapon with no script at all is the same answer, reached a different way.</remarks>
    [Test]
    public void NameFor_AWeaponWithNoScript_IsTheDefault()
    {
        Effects("TF_WEAPON_SOMETHING_ELSE", wall: "wall_fx")
            .NameFor(Blast(RocketLauncher, normal: (0f, 0f, 1f)), struckPlayer: false)
            .ShouldBe(ExplosionEffects.DefaultEffect);
    }

    /// <remarks>
    /// **The four remapped ids reach another weapon's script** (`tf_fx_explosions.cpp:45-59`). A sticky is the
    /// commonest explosion in a match and `TF_WEAPON_GRENADE_DEMOMAN` has no script, so without the remap every
    /// demoman blast falls to the default.
    /// </remarks>
    [Test]
    public void NameFor_AStickyBomb_ReadsThePipebombLaunchersScript()
    {
        Effects(PipebombLauncher, wall: "sticky_fx")
            .NameFor(Blast(GrenadeDemoman, normal: (0f, 0f, 1f)), struckPlayer: false)
            .ShouldBe("sticky_fx");
    }

    /// <remarks>
    /// **A custom particle index overrides everything, including a script that would have answered.** It is read
    /// before the weapon is even looked up.
    /// </remarks>
    [Test]
    public void NameFor_ABlastNamingItsOwnParticle_IgnoresTheWeaponScript()
    {
        Effects(RocketLauncherAlias, wall: "wall_fx", player: "air_fx")
            .NameFor(
                Blast(RocketLauncher, normal: (0f, 0f, 1f), custom: 12),
                struckPlayer: false,
                customParticleName: index => index == 12 ? "custom_fx" : null)
            .ShouldBe("custom_fx");
    }

    /// <remarks>
    /// An index this project cannot resolve — the `ParticleEffectNames` table is not decoded — answers the
    /// default rather than the weapon's, because the engine had already overwritten `pszEffect` by then. Measured
    /// on `demostf-cp_process_f12`: no blast in it takes this branch at all.
    /// </remarks>
    [Test]
    public void NameFor_ACustomIndexThatCannotBeResolved_IsTheDefault()
    {
        Effects(RocketLauncherAlias, wall: "wall_fx", player: "air_fx")
            .NameFor(Blast(RocketLauncher, normal: (0f, 0f, 1f), custom: 12), struckPlayer: false)
            .ShouldBe(ExplosionEffects.DefaultEffect);
    }

    /// <remarks>
    /// `angExplosion.Init()` in mid air, `VectorAngles( vecNormal, angExplosion )` otherwise. A floor's normal
    /// answers a pitch of 270 because Valve measures pitch from <c>-z</c> and wraps into <c>[0, 360)</c>.
    /// </remarks>
    [Test]
    public void AnglesFor_AFloorNormal_AreVectorAnglesOfIt()
    {
        (float Pitch, float Yaw, float Roll) angles =
            ExplosionEffects.AnglesFor(Blast(RocketLauncher, normal: (0f, 0f, 1f)));

        angles.Pitch.ShouldBe(270f, 1e-3f);
        angles.Yaw.ShouldBe(0f, 1e-3f);
        angles.Roll.ShouldBe(0f);
    }

    /// <remarks>
    /// Mid air is three zeros, and NOT the angles of the tiny normal — which would be a real direction, computed
    /// from a value the server sent precisely because it had none to send.
    /// </remarks>
    [Test]
    public void AnglesFor_ABlastInMidAir_AreZero()
    {
        ExplosionEffects.AnglesFor(Blast(RocketLauncher, normal: (0.01f, 0.02f, -0.03f)))
            .ShouldBe((0f, 0f, 0f));
    }

    /// <summary>An <see cref="ExplosionEffects"/> over one synthetic weapon script.</summary>
    private static ExplosionEffects Effects(
        string alias, string? wall = null, string? player = null, string? water = null)
    {
        StringBuilder script = new("WeaponData\n{\n");

        Append(script, "ExplosionEffect", wall);
        Append(script, "ExplosionPlayerEffect", player);
        Append(script, "ExplosionWaterEffect", water);

        script.Append("}\n");

        byte[] bytes = Encoding.UTF8.GetBytes(script.ToString());

        return new ExplosionEffects(path =>
            string.Equals(path, "scripts/" + alias + ".txt", StringComparison.Ordinal) ? bytes : null);
    }

    private static void Append(StringBuilder script, string key, string? value)
    {
        if (value is { Length: > 0 })
        {
            script.Append('"').Append(key).Append("\"\t\"").Append(value).Append("\"\n");
        }
    }

    /// <summary>One blast, built rather than decoded.</summary>
    private static SceneExplosion Blast(
        int weapon,
        (float X, float Y, float Z) normal = default,
        int custom = SceneExplosion.NoCustomParticle) =>
        new(
            Tick: 1,
            X: 0f,
            Y: 0f,
            Z: 0f,
            Normal: normal,
            WeaponId: weapon,
            Entity: SceneExplosion.NoEntity,
            CustomParticleIndex: custom);
}
