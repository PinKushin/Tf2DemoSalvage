using System;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The ICE block cipher, as Source uses it to obfuscate script files.
/// </summary>
/// <remarks>
/// **Public-domain cryptography that Valve ships the source of.** ICE is Matthew Kwan's design
/// (1996); Valve's copy is <c>src/mathlib/IceKey.cpp</c> in <c>source-sdk-2013</c>, and this is a
/// transcription of it. The key TF2 uses for player class scripts is in Valve's source too —
/// <c>GetTFEncryptionKey</c> in <c>tf_shareddefs.cpp</c> returns the literal <c>"E2NcUkG2"</c>.
///
/// Nothing here is a circumvention of anything: the algorithm, the key and the call site are all
/// published by Valve, and the files being read are ones already installed on the machine. The
/// obfuscation exists to stop casual editing of class stats, not to protect a secret.
///
/// **Eight-byte blocks with a 64-bit key means eight rounds.** Valve constructs
/// <c>IceKey ice(0)</c>, and level 0 is the "thin" variant: size 1, eight rounds. Every other
/// level is unused here and is not implemented, because an untested branch of a cipher is worse
/// than an absent one.
/// </remarks>
internal sealed class IceCipher
{
    /// <summary>Bytes per block. ICE is a 64-bit block cipher at every key size.</summary>
    public const int BlockSize = 8;

    private const int Rounds = 8;

    /// <summary>Modulo values for the S-boxes.</summary>
    private static readonly int[][] SboxModulo =
    [
        [333, 313, 505, 369],
        [379, 375, 319, 391],
        [361, 445, 451, 397],
        [397, 425, 395, 505],
    ];

    /// <summary>XOR values for the S-boxes.</summary>
    private static readonly int[][] SboxXor =
    [
        [0x83, 0x85, 0x9b, 0xcd],
        [0xcc, 0xa7, 0xad, 0x41],
        [0x4b, 0x2e, 0xd4, 0x33],
        [0xea, 0xcb, 0x2e, 0x04],
    ];

    /// <summary>Permutation values for the P-box.</summary>
    private static readonly uint[] Pbox =
    [
        0x00000001, 0x00000080, 0x00000400, 0x00002000,
        0x00080000, 0x00200000, 0x01000000, 0x40000000,
        0x00000008, 0x00000020, 0x00000100, 0x00004000,
        0x00010000, 0x00800000, 0x04000000, 0x20000000,
        0x00000004, 0x00000010, 0x00000200, 0x00008000,
        0x00020000, 0x00400000, 0x08000000, 0x10000000,
        0x00000002, 0x00000040, 0x00000800, 0x00001000,
        0x00040000, 0x00100000, 0x02000000, 0x80000000,
    ];

    /// <summary>The key rotation schedule.</summary>
    private static readonly int[] KeyRotation =
        [0, 1, 2, 3, 2, 1, 3, 0, 1, 3, 2, 0, 3, 1, 0, 2];

    /// <summary>The S-boxes, built once from the tables above.</summary>
    private static readonly uint[][] Sbox = BuildSboxes();

    private readonly uint[][] _schedule = [.. new uint[Rounds][]];

    /// <summary>Builds a key schedule from an eight-byte key.</summary>
    /// <param name="key">The key; exactly <see cref="BlockSize"/> bytes.</param>
    /// <exception cref="ArgumentException">The key is the wrong length.</exception>
    public IceCipher(ReadOnlySpan<byte> key)
    {
        if (key.Length != BlockSize)
        {
            throw new ArgumentException(
                $"An eight-round ICE key is {BlockSize} bytes, not {key.Length}.", nameof(key));
        }

        for (int round = 0; round < Rounds; round++)
        {
            _schedule[round] = new uint[3];
        }

        // The key is read into four 16-bit words in REVERSE order: kb[3 - i]. Filling them
        // forwards produces a valid schedule that decrypts to noise, with nothing to report.
        ushort[] keyBlock = new ushort[4];

        for (int i = 0; i < 4; i++)
        {
            keyBlock[3 - i] = (ushort)((key[i * 2] << 8) | key[(i * 2) + 1]);
        }

        BuildSchedule(keyBlock);
    }

    /// <summary>Encrypts one block.</summary>
    /// <param name="plainText">Eight bytes in.</param>
    /// <param name="cipherText">Eight bytes out.</param>
    /// <remarks>
    /// **This is not needed to read anything, and it is here to make the reader testable.**
    /// Nothing ICE-encrypted ever enters a <c>.dem</c> — the only ciphertext this project meets is
    /// in <c>.ctx</c> script files already installed on the machine, which are read and never
    /// written. So unlike <c>NetMessageWriter</c> or <c>StringTableCodec.WriteEntries</c>, this is
    /// not part of the bit-identical recompilation path.
    ///
    /// What it buys is the round trip. Without it, testing <see cref="Decrypt"/> means hand-building
    /// ciphertext and checking this project's arithmetic against my own — which is the trap in
    /// <c>docs/memory/fixtures-are-the-weak-point.md</c>, and the reason the rule there is to prefer
    /// a round-trip property wherever an encoder exists. Now one does.
    ///
    /// Transcribed from <c>IceKey::encrypt</c> (<c>src/mathlib/IceKey.cpp:238</c>). It is
    /// <see cref="Decrypt"/> with the key schedule walked FORWARD rather than backward — a Feistel
    /// network, so the two directions differ only in that order.
    /// </remarks>
    public void Encrypt(ReadOnlySpan<byte> plainText, Span<byte> cipherText)
    {
        uint left = ((uint)plainText[0] << 24) | ((uint)plainText[1] << 16) |
                    ((uint)plainText[2] << 8) | plainText[3];
        uint right = ((uint)plainText[4] << 24) | ((uint)plainText[5] << 16) |
                     ((uint)plainText[6] << 8) | plainText[7];

        for (int round = 0; round < Rounds; round += 2)
        {
            left ^= Round(right, _schedule[round]);
            right ^= Round(left, _schedule[round + 1]);
        }

        for (int i = 0; i < 4; i++)
        {
            cipherText[3 - i] = (byte)(right & 0xff);
            cipherText[7 - i] = (byte)(left & 0xff);

            right >>= 8;
            left >>= 8;
        }
    }

    /// <summary>Decrypts one block in place-compatible fashion.</summary>
    /// <param name="cipherText">Eight bytes in.</param>
    /// <param name="plainText">Eight bytes out.</param>
    public void Decrypt(ReadOnlySpan<byte> cipherText, Span<byte> plainText)
    {
        uint left = ((uint)cipherText[0] << 24) | ((uint)cipherText[1] << 16) |
                    ((uint)cipherText[2] << 8) | cipherText[3];
        uint right = ((uint)cipherText[4] << 24) | ((uint)cipherText[5] << 16) |
                     ((uint)cipherText[6] << 8) | cipherText[7];

        // Decryption is encryption with the schedule walked backwards - a Feistel network, so the
        // rounds are their own inverse in reverse order.
        for (int round = Rounds - 1; round > 0; round -= 2)
        {
            left ^= Round(right, _schedule[round]);
            right ^= Round(left, _schedule[round - 1]);
        }

        for (int i = 0; i < 4; i++)
        {
            plainText[3 - i] = (byte)(right & 0xff);
            plainText[7 - i] = (byte)(left & 0xff);

            right >>= 8;
            left >>= 8;
        }
    }

    /// <summary>Decrypts a whole buffer the way <c>UTIL_DecodeICE</c> does.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <returns>The decrypted bytes.</returns>
    /// <remarks>
    /// **The trailing partial block is left encrypted, and that is Valve's behaviour, not an
    /// oversight here.** <c>UTIL_DecodeICE</c> loops <c>while (bytesLeft >= blockSize)</c> and then
    /// copies back only <c>size - bytesLeft</c> bytes, so a file whose length is not a multiple of
    /// eight ends in up to seven bytes of ciphertext. The KeyValues parser never notices because
    /// by then it has closed the last brace.
    ///
    /// Reproduced deliberately: a reader that decrypted the tail some other way would disagree
    /// with the engine about the end of every odd-length file.
    /// </remarks>
    public byte[] DecryptAll(ReadOnlySpan<byte> data)
    {
        byte[] result = data.ToArray();

        for (int at = 0; at + BlockSize <= data.Length; at += BlockSize)
        {
            Decrypt(data.Slice(at, BlockSize), result.AsSpan(at, BlockSize));
        }

        return result;
    }

    /// <summary>The single-round ICE f function.</summary>
    private static uint Round(uint value, uint[] subkey)
    {
        // Left and right halves expanded to 40 bits.
        uint left = ((value >> 16) & 0x3ff) | (((value >> 14) | (value << 18)) & 0xffc00);
        uint right = (value & 0x3ff) | ((value << 2) & 0xffc00);

        // The salt permutation, in Valve's own condensed form rather than the commented-out
        // select-by-mask version above it.
        uint saltedLeft = subkey[2] & (left ^ right);
        uint saltedRight = saltedLeft ^ right;

        saltedLeft ^= left;

        saltedLeft ^= subkey[0];
        saltedRight ^= subkey[1];

        return Sbox[0][saltedLeft >> 10] | Sbox[1][saltedLeft & 0x3ff] |
               Sbox[2][saltedRight >> 10] | Sbox[3][saltedRight & 0x3ff];
    }

    /// <summary>Eight-bit Galois field multiplication, modulo m.</summary>
    /// <remarks>Ordinary multiplication with the additions replaced by XOR.</remarks>
    private static uint GaloisMultiply(uint a, uint b, uint modulus)
    {
        uint result = 0;

        while (b != 0)
        {
            if ((b & 1) != 0)
            {
                result ^= a;
            }

            a <<= 1;
            b >>= 1;

            if (a >= 256)
            {
                a ^= modulus;
            }
        }

        return result;
    }

    /// <summary>Raises the base to the seventh power in the Galois field.</summary>
    private static uint GaloisExponent7(uint b, uint modulus)
    {
        if (b == 0)
        {
            return 0;
        }

        uint x = GaloisMultiply(b, b, modulus);

        x = GaloisMultiply(b, x, modulus);
        x = GaloisMultiply(x, x, modulus);

        return GaloisMultiply(b, x, modulus);
    }

    /// <summary>The ICE 32-bit P-box permutation.</summary>
    private static uint Permute32(uint x)
    {
        uint result = 0;

        for (int bit = 0; x != 0; bit++, x >>= 1)
        {
            if ((x & 1) != 0)
            {
                result |= Pbox[bit];
            }
        }

        return result;
    }

    private static uint[][] BuildSboxes()
    {
        uint[][] boxes = [new uint[1024], new uint[1024], new uint[1024], new uint[1024]];

        for (int i = 0; i < 1024; i++)
        {
            int column = (i >> 1) & 0xff;
            int row = (i & 0x1) | ((i & 0x200) >> 8);

            for (int box = 0; box < 4; box++)
            {
                // Each box shifts its result into a different byte lane, so the four together
                // cover the whole word: 24, 16, 8, then 0.
                uint x = GaloisExponent7(
                    (uint)(column ^ SboxXor[box][row]), (uint)SboxModulo[box][row]);

                boxes[box][i] = Permute32(x << (8 * (3 - box)));
            }
        }

        return boxes;
    }

    /// <summary>Builds the eight-round schedule from the key words.</summary>
    private void BuildSchedule(ushort[] keyBlock)
    {
        for (int i = 0; i < 8; i++)
        {
            int rotation = KeyRotation[i];
            uint[] subkey = _schedule[i];

            for (int j = 0; j < 15; j++)
            {
                int target = j % 3;

                for (int k = 0; k < 4; k++)
                {
                    int word = (rotation + k) & 3;
                    int bit = keyBlock[word] & 1;

                    subkey[target] = (subkey[target] << 1) | (uint)bit;

                    // The key word rotates right with the COMPLEMENT of the consumed bit fed back
                    // into the top. Feeding the bit itself back builds a schedule that is stable,
                    // plausible and wrong.
                    keyBlock[word] = (ushort)((keyBlock[word] >> 1) | ((bit ^ 1) << 15));
                }
            }
        }
    }
}
