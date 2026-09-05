using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tf2DemoSalvage.Core.Text;

/// <summary>
/// Reads values out of assembly text, refusing malformed ones in a way the reader can act on.
/// </summary>
/// <remarks>
/// **The exception TYPE is the whole reason this exists** (B344, B345).
/// <c>DemoAssembly.Parse</c> catches <see cref="InvalidDataException"/> and nothing else, for one
/// purpose: to rethrow it with the offending text attached — <c>(assembling: {line})</c>. That is
/// the entire mechanism by which somebody hand-editing a decompiled trace learns *which line* they
/// broke, and the readable form exists so that they can.
///
/// So a bare <c>int.Parse</c> is not a shortcut with a worse message; it is a refusal that loses its
/// context completely. Five files stated the contract properly in seven places and left it in
/// twenty-eight others, each raising a type the handler does not catch — <c>FormatException</c>,
/// <c>ArgumentException</c>, <c>ArgumentOutOfRangeException</c>, <c>KeyNotFoundException</c>. The
/// asymmetry was visible in a single line: a typo in a field NAME reported the file, the line and
/// the field, while a typo in that same line's update type three tokens earlier named nothing.
///
/// **Every message quotes what was written**, not only what was expected. On a hand-edited trace the
/// value is the half the editor can act on, and <c>Command tick '{parts[1]}' is not a number.</c>
/// (<c>DemoAssembly.cs:460</c>) was already the house shape — applied once, in a function that then
/// read its payload with a bare <c>Convert.FromHexString</c> eight lines later.
///
/// **The subject noun is a parameter rather than a wrapper hierarchy.** Each caller passes its own —
/// "An entity line", "A sound", "A string table line" — so the messages keep the voice each file
/// already had while the logic lives once.
/// </remarks>
internal static class AssemblyText
{
    /// <summary>A field's text, or a refusal naming the field that is missing.</summary>
    /// <param name="fields">The line's <c>name=value</c> fields.</param>
    /// <param name="name">The field to read.</param>
    /// <param name="subject">What kind of line this is, for the message.</param>
    /// <returns>The field's text.</returns>
    internal static string Text(Dictionary<string, string> fields, string name, string subject) =>
        fields.TryGetValue(name, out string? value)
            ? value
            : throw new InvalidDataException($"{subject} has no '{name}' field.");

    /// <summary>The token at an index, or a refusal naming what the line lacks.</summary>
    /// <param name="tokens">The line's tokens.</param>
    /// <param name="index">The token to read.</param>
    /// <param name="what">What that token holds, for the message.</param>
    /// <param name="subject">What kind of line this is, for the message.</param>
    /// <returns>The token.</returns>
    internal static string Token(
        IReadOnlyList<string> tokens, int index, string what, string subject) =>
        index >= 0 && index < tokens.Count
            ? tokens[index]
            : throw new InvalidDataException(
                $"{subject} has no {what}: '{string.Join(' ', tokens)}'.");

    /// <summary>A whole number, or a refusal quoting what was written instead.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="what">What the number means, for the message.</param>
    /// <param name="subject">What kind of line this is, for the message.</param>
    /// <returns>The number.</returns>
    internal static int Number(string value, string what, string subject) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number
            : throw new InvalidDataException(
                $"{subject}'s {what} is not a whole number: '{value}'.");

    /// <summary>A wide whole number, or a refusal quoting what was written instead.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="what">What the number means, for the message.</param>
    /// <param name="subject">What kind of line this is, for the message.</param>
    /// <returns>The number.</returns>
    internal static long Wide(string value, string what, string subject) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
            ? number
            : throw new InvalidDataException(
                $"{subject}'s {what} is not a whole number: '{value}'.");

    /// <summary>A real number, or a refusal quoting what was written instead.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="what">What the number means, for the message.</param>
    /// <param name="subject">What kind of line this is, for the message.</param>
    /// <returns>The number.</returns>
    internal static float Real(string value, string what, string subject) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
            ? number
            : throw new InvalidDataException($"{subject}'s {what} is not a number: '{value}'.");

    /// <summary>An enumeration member, or a refusal listing the ones that exist.</summary>
    /// <remarks>
    /// **The valid names are listed because these sets are small and closed.** Somebody who typed
    /// `entre` cannot recover `ENTER` from `Requested value 'entre' was not found`.
    ///
    /// **A numeric value outside the enumeration is refused too.** <c>Enum.Parse</c> accepts `99`
    /// and yields an undefined member, which then decodes against a switch matching no branch — a
    /// wrong answer rather than a refusal. <see cref="Enum.IsDefined{T}(T)"/> makes it total.
    /// </remarks>
    /// <typeparam name="T">The enumeration.</typeparam>
    /// <param name="value">The text to read.</param>
    /// <param name="what">What the member means, for the message.</param>
    /// <param name="subject">What kind of line this is, for the message.</param>
    /// <returns>The member.</returns>
    internal static T Enumeration<T>(string value, string what, string subject)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out T member) && Enum.IsDefined(member)
            ? member
            : throw new InvalidDataException(
                $"{subject}'s {what} is not one of {string.Join(", ", Enum.GetNames<T>())}: " +
                $"'{value}'.");

    /// <summary>Hexadecimal bytes, or a refusal quoting what was written instead.</summary>
    /// <param name="value">The text to read.</param>
    /// <param name="what">What the bytes hold, for the message.</param>
    /// <param name="subject">What kind of line this is, for the message.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] Hex(string value, string what, string subject)
    {
        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException failure)
        {
            throw new InvalidDataException(
                $"{subject}'s {what} is not hexadecimal: '{value}'.", failure);
        }
    }

    /// <summary>A count the line can actually carry, or a refusal saying what it declared.</summary>
    /// <remarks>
    /// **A declared length is not trusted, because trusting one allocates from text** (B345).
    /// `PropertyText` read an array's element count and passed it straight to
    /// `new List&lt;PropertyValue&gt;(count)` before reading a single element, so a trace saying
    /// `a 2000000000` raised `OutOfMemoryException` — measured, not predicted.
    /// `docs/FUZZING.md` names both halves: *"length-prefix decoders are where unbounded allocations
    /// come from"*, and an `OutOfMemoryException` is a defect because *"a caller cannot reasonably
    /// defend against"* it when the input came from a file someone downloaded.
    ///
    /// **The ceiling is exact rather than tuned.** A line cannot hold more elements than it has
    /// tokens left, so no valid input can reach it and no constant needs choosing.
    /// </remarks>
    /// <param name="value">The declared count.</param>
    /// <param name="available">The most the line could carry.</param>
    /// <param name="what">What is being counted, for the message.</param>
    /// <param name="subject">What kind of line this is, for the message.</param>
    /// <returns>The count.</returns>
    internal static int Count(string value, int available, string what, string subject)
    {
        int count = Number(value, what, subject);

        return count >= 0 && count <= available
            ? count
            : throw new InvalidDataException(
                $"{subject} declares {count} {what}, but the line has room for {available}.");
    }
}
