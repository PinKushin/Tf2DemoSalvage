using System;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One of TF2's shipped weapon scripts, decrypted and ready to be asked for a key.</summary>
/// <remarks>
/// **The reading half of `ReadEncryptedKVFile`** (`weapon_parse.cpp:196-268`), whose order is the whole of it:
///
/// <code>
/// Q_snprintf( szFullName, sizeof( szFullName ), "%s.txt", szFilenameWithoutExtension );
/// if ( bForceReadEncryptedFile || !pKV-&gt;LoadFromFile( pFilesystem, szFullName, pSearchPath ) )
/// {
///     if ( pICEKey )
///     {
///         Q_snprintf( szFullName, sizeof( szFullName ), "%s.ctx", szFilenameWithoutExtension );
///         ...
/// </code>
///
/// **The plain `.txt` first is not a fallback the other way round.** A loose `.txt` is how a mod overrides a weapon,
/// and the game ships only `.ctx` — which is why a `ls` of `tf/scripts/` finds no weapon at all.
///
/// **Extracted because a second caller appeared** (B415). <see cref="WeaponRoles"/> has read these since B283 for
/// `WeaponType`, and the explosion effects need `ExplosionEffect`, `ExplosionPlayerEffect` and
/// `ExplosionWaterEffect` out of the same files — a second copy of the extension order and the cipher key is a
/// second place for them to drift.
/// </remarks>
public sealed class WeaponScript
{
    /// <summary>Valve's own key, <c>GetTFEncryptionKey</c>, <c>tf_shareddefs.cpp:1616</c>.</summary>
    private static readonly byte[] EncryptionKey = "E2NcUkG2"u8.ToArray();

    /// <summary>The script's text, decrypted if it needed to be.</summary>
    private readonly byte[] _text;

    private WeaponScript(string name, byte[] text)
    {
        Name = name;
        _text = text;
    }

    /// <summary>The script's own name, without the folder or the extension.</summary>
    public string Name { get; }

    /// <summary>The whole script, for a probe or a dump. Decrypted.</summary>
    public ReadOnlyMemory<byte> Text => _text;

    /// <summary>Reads one weapon script by name.</summary>
    /// <param name="readFile">Opens a path out of the game's content, or answers null.</param>
    /// <param name="name">
    /// The script's bare name — either an entity name such as <c>tf_weapon_bat</c> or the alias
    /// <see cref="TfWeaponAliases"/> answers, which is the same file under a different case.
    /// </param>
    /// <returns>The script, or <c>null</c> when neither form exists.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static WeaponScript? Read(Func<string, byte[]?> readFile, string name)
    {
        ArgumentNullException.ThrowIfNull(readFile);
        ArgumentNullException.ThrowIfNull(name);

        string path = "scripts/" + name;

        if (readFile(path + ".txt") is { Length: > 0 } plain)
        {
            return new WeaponScript(name, plain);
        }

        return readFile(path + ".ctx") is { Length: > 0 } encrypted
            ? new WeaponScript(name, new IceCipher(EncryptionKey).DecryptAll(encrypted))
            : null;
    }

    /// <summary>One key's value.</summary>
    /// <param name="key">The key, without quotes; matched case-insensitively as Valve's reader does.</param>
    /// <returns>The value, or <c>null</c> when the script does not declare it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public string? Value(string key) => ScriptKeyValue.First(_text, key);
}
