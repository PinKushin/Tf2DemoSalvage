using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// Which model each TF2 class wears, read from the game's own class scripts.
/// </summary>
/// <remarks>
/// **A player's model is never sent, so a demo alone cannot say what anyone looked like.** Valve's
/// client resolves it locally — <c>CTFPlayerClassShared::GetModelName</c> returns
/// <c>GetPlayerClassData(m_iClass)->GetModelName()</c>, and only <c>m_iszCustomModel</c> travels on
/// the wire as an override. The recording carries a class number and nothing more.
///
/// **Read from the install rather than hardcoded.** <c>tf_classdata.cpp</c> parses
/// <c>scripts/playerclasses/&lt;class&gt;.txt</c> and takes the <c>"model"</c> key; a hardcoded
/// table of nine paths is one that goes stale silently when Valve moves a file, and is wrong for
/// any mod. Only the script *names* are hardcoded, because those are in the engine's own source
/// rather than in data.
///
/// The class order is the engine's, from <c>tf_shareddefs.h</c>, and it is deliberately not the
/// order the class-selection menu uses.
/// </remarks>
public sealed class PlayerClassModels
{
    /// <summary>The first class a player can actually be.</summary>
    /// <remarks><c>TF_FIRST_NORMAL_CLASS</c>; zero is <c>TF_CLASS_UNDEFINED</c>.</remarks>
    public const int FirstClass = 1;

    /// <summary>The last class that appears in a match.</summary>
    /// <remarks>
    /// Engineer. <c>TF_CLASS_CIVILIAN</c> follows him and is <c>TF_LAST_NORMAL_CLASS</c>, but no
    /// player is ever it outside of a mod, and its script may be absent.
    /// </remarks>
    public const int LastPlayingClass = 9;

    /// <summary>Where the class scripts live, without the extension the engine appends.</summary>
    /// <remarks>
    /// **Index is the class number**, so the order is <c>tf_shareddefs.h</c>'s and the blank at
    /// zero holds <c>TF_CLASS_UNDEFINED</c>'s place. Transcribed from <c>s_aPlayerClassFiles</c>
    /// in <c>tf_classdata.cpp</c>.
    ///
    /// Sniper before Soldier is not a mistake: the enum reads Scout, Sniper, Soldier, Demoman,
    /// Medic, Heavyweapons, Pyro, Spy, Engineer. Reordering it into the familiar menu order labels
    /// every player with a plausible wrong class and errors nowhere.
    /// </remarks>
    private static readonly string[] ClassScripts =
    [
        "",
        "scout",
        "sniper",
        "soldier",
        "demoman",
        "medic",
        "heavyweapons",
        "pyro",
        "spy",
        "engineer",
        "civilian",
    ];

    /// <summary>The key TF2 obfuscates its class scripts with.</summary>
    /// <remarks>
    /// **Valve's own, from their own published source.** <c>GetTFEncryptionKey</c> in
    /// <c>tf_shareddefs.cpp</c> is four lines long and returns the literal <c>"E2NcUkG2"</c>. The
    /// obfuscation exists to stop casual editing of class stats on a server, not to keep a secret
    /// — the algorithm, the key and the call site are all in the SDK.
    /// </remarks>
    private static readonly byte[] EncryptionKey = "E2NcUkG2"u8.ToArray();

    private readonly Dictionary<int, string> _models = [];

    private readonly Dictionary<int, string> _hands = [];

    /// <summary>Which classes refuse the air-walk animation.</summary>
    private readonly HashSet<int> _noAirwalk = [];

    private PlayerClassModels()
    {
    }

    /// <summary>Reads every class script the install carries.</summary>
    /// <param name="readFile">Opens a game file by its path, or answers null when absent.</param>
    /// <returns>The models, with any class whose script is missing simply absent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="readFile"/> is null.</exception>
    /// <remarks>
    /// **Takes a reader rather than a folder or an archive**, because the scripts live inside a
    /// VPK on a normal install and loose on disk on a modified one, and this type has no business
    /// knowing which. It also lets the whole thing be tested without an install.
    ///
    /// A missing script is skipped rather than thrown on. Civilian has no shipped script in TF2,
    /// and a reader that insisted on all eleven would fail on every real install.
    /// </remarks>
    public static PlayerClassModels Read(Func<string, byte[]?> readFile)
    {
        ArgumentNullException.ThrowIfNull(readFile);

        PlayerClassModels models = new();

        IceCipher cipher = new(EncryptionKey);

        for (int playerClass = FirstClass; playerClass < ClassScripts.Length; playerClass++)
        {
            string name = $"scripts/playerclasses/{ClassScripts[playerClass]}";

            // **Plain text first, then the encrypted form — the engine's own order.**
            // ReadEncryptedKVFile tries "<name>.txt" and falls back to "<name>.ctx", which is what
            // lets a mod override a class by dropping a loose file. A stock install ships only the
            // .ctx, so reversing this would work everywhere except where someone customised it.
            byte[]? script = readFile(name + ".txt");

            if (script is null)
            {
                script = readFile(name + ".ctx") is { } encrypted
                    ? cipher.DecryptAll(encrypted)
                    : null;
            }

            if (script is null)
            {
                continue;
            }

            if (ClassScript.Model(script) is { } model)
            {
                models._models[playerClass] = model;
            }

            // Same pass again: the hands decide whether a first-person weapon is drawn as one model
            // or two, and re-reading the script to ask would be a second decrypt of the same bytes.
            if (ClassScript.Hands(script) is { } hands)
            {
                models._hands[playerClass] = hands;
            }

            // Read in the same pass, because the script is already decrypted and in hand — this
            // decides whether a rising player air-walks or plays the jump.
            if (ClassScript.DontDoAirwalk(script))
            {
                models._noAirwalk.Add(playerClass);
            }
        }

        return models;
    }

    /// <summary>The model a class wears.</summary>
    /// <param name="playerClass">The class number, as the demo reports it.</param>
    /// <returns>The model path, or <c>null</c> when the class is not one the game defines.</returns>
    /// <remarks>
    /// **Null rather than falling back to Scout.** The engine does default the undefined class to
    /// <c>models/player/scout.mdl</c> — its own comment is "Undefined players still need a model" —
    /// but that is a rendering decision for the caller to make knowingly. Hidden here, it would
    /// report every unrecognised class as a Scout and look entirely reasonable.
    /// </remarks>
    public string? Model(int playerClass) =>
        _models.TryGetValue(playerClass, out string? model) ? model : null;

    /// <summary>Whether a class plays the air-walk animation while rising.</summary>
    /// <param name="playerClass">The class number, as the demo reports it.</param>
    /// <returns>True unless the class script sets <c>DontDoAirwalk</c>.</returns>
    /// <remarks>
    /// **True by default, which is the engine's default and not an optimistic guess.**
    /// <c>bValidAirWalkClass</c> is <c>pData &amp;&amp; pData->m_bDontDoAirwalk == false</c>, and
    /// <c>GetInt( "DontDoAirwalk", 0 )</c> means a script that omits the key describes a class that
    /// does air-walk. A class whose script is missing entirely therefore answers true here, which
    /// matches what the engine would draw if it somehow loaded one.
    /// </remarks>
    public bool Airwalks(int playerClass) => !_noAirwalk.Contains(playerClass);

    /// <summary>The class number of the demoman, from <c>tf_shareddefs.h</c>'s order.</summary>
    /// <remarks>
    /// Named because the enum's order is not the menu's — Scout, Sniper, Soldier, Demoman — so a
    /// literal 4 in a caller reads as the wrong class to anyone who knows the menu.
    /// </remarks>
    public const int Demoman = 4;

    /// <summary>The first-person hands this class holds its weapons with.</summary>
    /// <param name="playerClass">The class number, as the demo reports it.</param>
    /// <returns>The model path, or <c>null</c> when the class declares none.</returns>
    /// <remarks>
    /// **This is what tells the two viewmodel schemes apart.**
    /// <c>CTFWeaponBase::GetViewModel</c> (<c>tf_weaponbase.cpp:651</c>) returns the hands when the
    /// item attaches to them and the weapon's own <c>v_</c> model when it does not, and only the
    /// first case has a second model to draw. A viewer that always draws two puts the gun on screen
    /// twice for every weapon of the second kind — measured on a 2011 recording as
    /// <c>v_stickybomb_launcher_demo</c> and <c>c_stickybomb_launcher</c> at one point in space.
    ///
    /// Null for a class with no script, deliberately: a caller comparing a networked viewmodel
    /// against null gets "not the hands", which takes the single-model branch and draws one weapon.
    /// That is the safe direction — the failure is a missing gun rather than a doubled one.
    /// </remarks>
    public string? Hands(int playerClass) =>
        _hands.TryGetValue(playerClass, out string? hands) ? hands : null;
}
