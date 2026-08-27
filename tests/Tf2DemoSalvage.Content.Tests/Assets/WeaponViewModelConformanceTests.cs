using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Where the model of the weapon in a player's hands comes from.
/// </summary>
/// <remarks>
/// **It is not in the demo, and that is the finding this suite exists to record.** In modern TF2 the
/// networked viewmodel entity carries the player's ARMS — <c>c_sniper_arms.mdl</c>,
/// <c>c_pyro_arms.mdl</c> — and the weapon itself is a second model the CLIENT creates:
///
/// <code>
/// C_ViewmodelAttachmentModel *pEnt = new class C_ViewmodelAttachmentModel;
/// pEnt->InitializeAsClientEntity( pItem->GetPlayerDisplayModel( iClass, pOwner->GetTeamNumber() ), ... );
/// m_hViewmodelAttachment->SetParent( vm );
/// </code>
///
/// (<c>econ_entity.cpp:1153</c>.) <c>InitializeAsClientEntity</c> means exactly what it says: no
/// edict, nothing networked, nothing recorded. A demo cannot carry it.
///
/// **Nor does the held weapon entity help.** Measured on z1800 across three ticks: sixteen players
/// holding a weapon, and not one of those weapon entities produces a drawable track — a carried
/// weapon is bone-merged onto its owner and sends no origin of its own
/// (<c>docs/memory/bone-merge-sends-no-position.md</c>).
///
/// **So the model has to come from the shipped scripts, which is where the engine gets it too.**
/// <c>weapon_parse.cpp:366</c>:
///
/// <code>
/// Q_strncpy( szViewModel, pKeyValuesData->GetString( "viewmodel" ), MAX_WEAPON_STRING );
/// </code>
///
/// and <c>CBaseCombatWeapon::GetViewModel</c> (<c>basecombatweapon_shared.cpp:333</c>) returns that
/// string. The demo names the weapon's server class; this project already translates that to a
/// script name for the animation work, and the same script carries the model.
///
/// **The known limitation, stated rather than discovered later:** an item definition can override
/// the script's model, which is how reskins and festive variants work. Reading the script alone
/// gives the stock weapon for those. That is a wrong-looking gun rather than no gun, and it is
/// honest about which it is.
/// </remarks>
public sealed class WeaponViewModelConformanceTests
{
    /// <summary>Where the game is, on this machine.</summary>
    private static string GameDirectory => GameInstall.Require();

    /// <summary>The key the engine reads, verbatim.</summary>
    private const string ViewModelKey = "viewmodel";

    [Test]
    public void ViewModelKey_InEveryStockWeaponScript_NamesAModel()
    {
        // Server classes a demo really mentions, taken from the corpus survey rather than invented:
        // z1800 alone carries wrenches, shotguns, revolvers, pipebomb launchers, flare guns,
        // grenade launchers, flamethrowers, knives, sniper rifles and a Direct Hit.
        // **Paired with a player class, because a weapon's script name is not a property of the
        // weapon alone.** `CTFShotgun` is `tf_weapon_shotgun_soldier` in a soldier's hands,
        // `_hwg` in a heavy's and `_pyro` in a pyro's — asking without the class resolves no
        // script at all, which is what this test reported the first time it ran.
        (string Weapon, int? Class)[] serverClasses =
        [
            ("CTFScattergun", 1), ("CTFRocketLauncher", 3), ("CTFFlameThrower", 7),
            ("CTFGrenadeLauncher", 4), ("CTFSniperRifle", 2), ("CTFKnife", 8),
            ("CTFRevolver", 8), ("CTFWrench", 9), ("CTFShotgun", 3),
        ];

        Func<string, byte[]?>? read = Reader();

        if (read is null)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        List<string> resolved = [];

        foreach ((string serverClass, int? playerClass) in serverClasses)
        {
            string? model = null;
            string note = string.Empty;
            bool foundScript = false;

            foreach (string candidate in WeaponScriptName.Candidates(serverClass, playerClass))
            {
                if (Script(read, "scripts/" + candidate) is not { } script)
                {
                    continue;
                }

                foundScript = true;

                model = ScriptKeyValue.First(script, ViewModelKey);

                // **Read from the raw text too, because the value is not quoted and a reader
                // looking for one walks straight past it.** `ScriptKeyValue.First` takes the next
                // quoted token, which here is the NEXT KEY — it answered 'playermodel' for the
                // scattergun. That is the shape of a silent wrong answer, and it is why this test
                // looks at the bytes as well.
                string text = System.Text.Encoding.UTF8.GetString(script);
                int at = text.IndexOf(
                    '"' + ViewModelKey + '"', StringComparison.OrdinalIgnoreCase);

                if (at >= 0)
                {
                    note = text.Substring(at, Math.Min(80, text.Length - at));
                }

                if (model is { Length: > 0 })
                {
                    break;
                }
            }

            // The script has to EXIST — that is the part this project controls, since it is the
            // server-class-to-script-name translation. Whether the script then carries a model is
            // Valve's business and is what the split below measures.
            foundScript.ShouldBeTrue(
                $"{serverClass} resolved no weapon script at all, so the name translation is wrong");

            // **Two shapes, and a weapon is in one or the other.** Some scripts still carry the
            // path outright —
            //
            //     "viewmodel"  "models/weapons/c_models/c_flamethrower/c_flamethrower.mdl"
            //
            // and others hold a note where the value used to be:
            //
            //     "viewmodel"     -viewmodel is now defined in _items_main.txt
            //
            // The engine reads `GetWpnData().szViewModel` either way; for the second kind the
            // shipped data stopped supplying it and the model comes from the item definition
            // instead, keyed by definition index.
            //
            // That decides the implementation and it is why this is asserted rather than noted:
            // the script is worth reading FIRST, because when it answers it answers cheaply, and
            // the schema lookup is only needed for the rest. "The scripts are useless now" was the
            // conclusion after one weapon and it was wrong.
            bool fromScript =
                model is { Length: > 0 } &&
                model.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase);

            if (!fromScript && note.Length > 0)
            {
                note.ShouldContain(
                    "_items_main",
                    Case.Insensitive,
                    $"{serverClass}'s {ViewModelKey} is neither a model nor the usual signpost: " +
                    $"'{note.ReplaceLineEndings(" ")}'");
            }

            resolved.Add(
                $"{serverClass} -> {(fromScript ? model : "(item schema)")}");
        }

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, resolved));

        // A positive control: an empty list satisfies every assertion above vacuously.
        resolved.Count.ShouldBe(serverClasses.Length);
    }

    /// <summary>Reads a weapon script, plain or ICE-encrypted, as the engine does.</summary>
    /// <remarks>
    /// Plain text first and then the encrypted form, which is <c>ReadEncryptedKVFile</c>'s own
    /// order — a loose <c>.txt</c> is how a mod overrides a weapon.
    /// </remarks>
    private static byte[]? Script(Func<string, byte[]?> read, string name)
    {
        if (read(name + ".txt") is { } plain)
        {
            return plain;
        }

        return read(name + ".ctx") is { } encrypted
            ? new IceCipher(WeaponRoles.EncryptionKey).DecryptAll(encrypted)
            : null;
    }

    /// <summary>Opens files out of the game's archives, or null when it is not installed.</summary>
    private static Func<string, byte[]?>? Reader()
    {
        List<VpkArchive> archives = [.. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
            .Select(name => Path.Combine(GameDirectory, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)];

        if (archives.Count == 0)
        {
            return null;
        }

        return path =>
        {
            byte[]? found = null;

            foreach (VpkArchive archive in archives)
            {
                found ??= archive.ReadFile(path);
            }

            return found;
        };
    }
}
