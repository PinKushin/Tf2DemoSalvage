using System;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The ICE block cipher, tested without a single game file.
/// </summary>
/// <remarks>
/// **279 lines of cipher with no test of any kind before this** — found while auditing Content for
/// synthetic coverage (`docs/MEASUREMENT-PLAN.md`). It is on a live path: `.ctx` class and weapon
/// scripts are ICE-encrypted, and what comes out of them decides a weapon's type, which decides the
/// activity suffix a player's animation uses. A cipher that is subtly wrong produces plausible
/// garbage rather than an error, and the failure would surface as the wrong animation.
///
/// **The whole suite is synthetic**, which is the point: a cipher needs no assets, so nothing here
/// touches the TF2 install and all of it can run on the measurement box.
///
/// **The property is encrypt → decrypt = identity**, which is why `Encrypt` was written. Testing
/// `Decrypt` against hand-built ciphertext would be checking this transcription against my own
/// arithmetic — the trap in `docs/memory/fixtures-are-the-weak-point.md`. A round trip has no such
/// blind spot: only a genuinely inverse pair of transforms satisfies it.
///
/// A round trip alone cannot catch a pair that is inverse but wrong (two transposed transforms
/// still compose to identity), so it is not the only test here. The key-dependence and
/// avalanche cases below pin that the transform is the real cipher rather than any involution.
/// </remarks>
public sealed class IceCipherTests
{
    /// <summary>The key Valve uses, from <c>GetTFEncryptionKey</c> in <c>tf_shareddefs.cpp</c>.</summary>
    private static readonly byte[] TfKey = Encoding.ASCII.GetBytes("E2NcUkG2");

    [Test]
    public void EncryptThenDecryptReturnsTheOriginalBlock()
    {
        byte[] plain = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

        Roundtrip(plain).ShouldBe(plain);
    }

    [Test]
    public void EveryByteValueSurvivesTheRoundTrip()
    {
        // All-zero and all-ones are the blocks a masking or shifting mistake survives: a bug that
        // drops the top bit of every byte is invisible against 0x00 and against 0x7F.
        Roundtrip([0, 0, 0, 0, 0, 0, 0, 0]).ShouldBe(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });

        byte[] ones = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        Roundtrip(ones).ShouldBe(ones);

        // A block where every byte differs, so a transposition inside the block shows up.
        byte[] ascending = [0, 1, 2, 3, 4, 5, 6, 7];
        Roundtrip(ascending).ShouldBe(ascending);
    }

    [Test]
    public void CipherTextIsNotThePlainText()
    {
        // **The control that stops every other test in this file being vacuous.** A cipher whose
        // encrypt and decrypt were both the identity function would satisfy every round trip here.
        byte[] plain = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];
        byte[] cipher = new byte[IceCipher.BlockSize];

        new IceCipher(TfKey).Encrypt(plain, cipher);

        cipher.ShouldNotBe(plain);
    }

    [Test]
    public void ADifferentKeyProducesDifferentCipherText()
    {
        // Pins that the key is actually used. A transform that ignored its key entirely would
        // round-trip perfectly and decrypt every real .ctx file into garbage.
        byte[] plain = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

        byte[] underTfKey = new byte[IceCipher.BlockSize];
        byte[] underOtherKey = new byte[IceCipher.BlockSize];

        new IceCipher(TfKey).Encrypt(plain, underTfKey);
        new IceCipher(Encoding.ASCII.GetBytes("E2NcUkG3")).Encrypt(plain, underOtherKey);

        // One bit of difference in the last key byte, and the whole block should change.
        underOtherKey.ShouldNotBe(underTfKey);
    }

    [Test]
    public void OneChangedInputBitChangesMostOfTheOutput()
    {
        // Avalanche. A weak or half-implemented round function still round-trips and still depends
        // on the key, but leaves the output correlated with the input - so this is the test that
        // distinguishes the real eight-round cipher from a couple of XORs that happen to invert.
        byte[] plain = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];
        byte[] flipped = [.. plain];
        flipped[0] ^= 0x01;

        byte[] first = new byte[IceCipher.BlockSize];
        byte[] second = new byte[IceCipher.BlockSize];

        IceCipher cipher = new(TfKey);
        cipher.Encrypt(plain, first);
        cipher.Encrypt(flipped, second);

        int differingBits = 0;

        for (int i = 0; i < IceCipher.BlockSize; i++)
        {
            differingBits += System.Numerics.BitOperations.PopCount((uint)(first[i] ^ second[i]));
        }

        // A good 64-bit block cipher changes about half the output bits; the threshold is set well
        // below that so it states "diffusion happened" rather than pinning this exact cipher's
        // statistics, which would be a change-detector.
        differingBits.ShouldBeGreaterThan(16, "a one-bit input change should avalanche");
    }

    [Test]
    public void DecryptAllLeavesATrailingPartialBlockEncrypted()
    {
        // **Valve's behaviour, reproduced deliberately.** UTIL_DecodeICE loops while at least a
        // whole block remains, so a file whose length is not a multiple of eight ends in up to
        // seven bytes of untouched ciphertext. A reader that "helpfully" handled the tail would
        // disagree with the engine about the end of every odd-length file.
        byte[] plain = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF, 0xAA, 0xBB, 0xCC];

        IceCipher cipher = new(TfKey);

        byte[] encrypted = new byte[plain.Length];
        cipher.Encrypt(plain.AsSpan(0, IceCipher.BlockSize), encrypted.AsSpan(0, IceCipher.BlockSize));

        // The three-byte tail is copied across untouched, as a real file's would be.
        plain.AsSpan(IceCipher.BlockSize).CopyTo(encrypted.AsSpan(IceCipher.BlockSize));

        byte[] decrypted = cipher.DecryptAll(encrypted);

        // The whole block came back...
        decrypted.AsSpan(0, IceCipher.BlockSize).ToArray()
            .ShouldBe(plain.AsSpan(0, IceCipher.BlockSize).ToArray());

        // ...and the tail was passed through, not decrypted into something else.
        decrypted.AsSpan(IceCipher.BlockSize).ToArray().ShouldBe(new byte[] { 0xAA, 0xBB, 0xCC });
    }

    [Test]
    public void DecryptAllHandlesSeveralBlocksIndependently()
    {
        // ICE here is ECB - Valve chains nothing - so two identical plaintext blocks must encrypt
        // identically. That is a real property of the format, and it is also how a mistakenly
        // stateful implementation (one that carried the previous block into the next) is caught.
        byte[] block = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];
        byte[] plain = [.. block, .. block];

        IceCipher cipher = new(TfKey);
        byte[] encrypted = new byte[plain.Length];

        cipher.Encrypt(plain.AsSpan(0, 8), encrypted.AsSpan(0, 8));
        cipher.Encrypt(plain.AsSpan(8, 8), encrypted.AsSpan(8, 8));

        encrypted.AsSpan(0, 8).ToArray().ShouldBe(encrypted.AsSpan(8, 8).ToArray());

        cipher.DecryptAll(encrypted).ShouldBe(plain);
    }

    /// <summary>Encrypts a block and decrypts it again with the same key.</summary>
    [Test]
    public void AKnownAnswerPinsTheTablesThemselves()
    {
        // **The test the round trips could not be, and the mutation report is what showed it.**
        // After the property suite above, `IceCipher` still had 53 surviving mutants — and the
        // reason is structural rather than an oversight in any one test: this file is mostly
        // TABLES (four S-box moduli, four S-box XORs, a 32-entry P-box, the key schedule), and a
        // mutated table entry STILL ROUND-TRIPS. Encrypt and decrypt both read the mutated table,
        // so they remain exact inverses of each other while computing a different cipher.
        //
        // That is the "inverse but wrong" hole this file's own remarks warned about and then left
        // open. A known answer closes it: one fixed input under one fixed key must produce these
        // exact bytes, and no table can change without changing them.
        //
        // **Provenance, because a vector invented by the implementation it tests proves nothing.**
        // These bytes were produced by this implementation — but by an implementation independently
        // validated against Valve's own output: `WeaponRolesTests` and `PlayerClassModelsTests`
        // decrypt real ICE-encrypted `.ctx` files from the game install into meaningful weapon and
        // class data, 8 tests with nothing skipped. A decrypt that reads Valve's ciphertext
        // correctly, plus an encrypt proven to be its exact inverse, fixes this vector as the one
        // the real cipher produces. It is a regression lock on behaviour already shown correct, not
        // a claim derived from itself.
        byte[] plain = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];
        byte[] cipher = new byte[IceCipher.BlockSize];

        new IceCipher(TfKey).Encrypt(plain, cipher);

        Convert.ToHexString(cipher).ShouldBe("4C2CAE406D4D1995");
    }

    [Test]
    public void TheKnownAnswerDecryptsBackToItsPlainText()
    {
        // The other direction against the same fixed pair, so a table mutation cannot hide in
        // decrypt alone either.
        byte[] cipher = Convert.FromHexString("4C2CAE406D4D1995");
        byte[] plain = new byte[IceCipher.BlockSize];

        new IceCipher(TfKey).Decrypt(cipher, plain);

        Convert.ToHexString(plain).ShouldBe("0123456789ABCDEF");
    }

    private static byte[] Roundtrip(byte[] plain)
    {
        IceCipher cipher = new(TfKey);

        byte[] encrypted = new byte[IceCipher.BlockSize];
        byte[] decrypted = new byte[IceCipher.BlockSize];

        cipher.Encrypt(plain, encrypted);
        cipher.Decrypt(encrypted, decrypted);

        return decrypted;
    }
}
