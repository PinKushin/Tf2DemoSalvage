using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>Whether every key TF2 names reaches the console under a name TF2 knows (B826).</summary>
/// <remarks>
/// **TF2's console refuses any other name** (`"%s" isn't a valid key`, `ButtonCodeNames.KeyOf`), so a key the view names
/// in WinForms spelling could be neither bound nor pressed: `NumPad3`, where TF2's table has `KP_PGDN` (B826 review 1,
/// Fable, Sonnet). The keys are those of TF2's table (`inputsystem.dll` x64, 0x180034430) that the view can tell apart:
/// not `RSHIFT`, `RALT`, `RCTRL` or `KP_ENTER`, which WinForms reports as the left key or as `Enter` (B826 review 2).
/// </remarks>
public sealed class KeyNamesConformanceTests
{
    /// <summary>The keyboard keys TF2's table names: letters, digits, keypad, punctuation, editing and function keys.</summary>
    private static IEnumerable<Keys> Tf2Keys() =>
    [
        .. Enumerable.Range(0, 26).Select(i => Keys.A + i),
        .. Enumerable.Range(0, 10).Select(i => Keys.D0 + i),
        .. Enumerable.Range(0, 10).Select(i => Keys.NumPad0 + i),
        .. Enumerable.Range(0, 12).Select(i => Keys.F1 + i),
        Keys.Divide, Keys.Multiply, Keys.Subtract, Keys.Add, Keys.Decimal,
        Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemSemicolon, Keys.OemQuotes, Keys.Oemtilde, Keys.Oemcomma,
        Keys.OemPeriod, Keys.OemQuestion, Keys.OemPipe, Keys.OemMinus, Keys.Oemplus,
        Keys.Enter, Keys.Space, Keys.Back, Keys.Tab, Keys.CapsLock, Keys.NumLock, Keys.Escape, Keys.Scroll,
        Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Pause,
        Keys.ShiftKey, Keys.Menu, Keys.ControlKey, Keys.LWin, Keys.RWin, Keys.Apps,
        Keys.Up, Keys.Left, Keys.Down, Keys.Right,
    ];

    /// <remarks>
    /// **Named as TF2 names it, and read back to the same key** (B826): `NameOf` is what the view presses into the
    /// console, and `Resolve` is how a bound name becomes a menu shortcut. The control is the table itself: `KeyOf`
    /// refuses `NumPad3`.
    /// </remarks>
    [Test]
    public void NameOf_EveryKeyTf2Names_IsATf2NameThatResolvesBack()
    {
        ButtonCodeNames.KeyOf("NumPad3").ShouldBeNull("the control: TF2's table has no such name");

        foreach (Keys key in Tf2Keys())
        {
            string name = KeyNames.NameOf(key);

            ButtonCodeNames.KeyOf(name).ShouldNotBeNull($"{key} is named {name}");
            KeyNames.Resolve(name).ShouldBe(key, $"{name} resolves back");
        }
    }

    /// <remarks>
    /// **The pairs themselves, against TF2's table** (B826 review 2, Sonnet): the round trip above passes a swapped pair,
    /// since one table is read both ways. `KEY_PAD_0` to `KEY_PAD_9` are `KP_INS` … `KP_PGUP` in `inputsystem.dll`'s
    /// table (0x180034430), the keypad's `/ * - + .` are `KP_SLASH KP_MULTIPLY KP_MINUS KP_PLUS KP_DEL`, and the OEM keys
    /// are the characters printed on them.
    /// </remarks>
    [TestCase(Keys.NumPad0, "KP_INS")]
    [TestCase(Keys.NumPad1, "KP_END")]
    [TestCase(Keys.NumPad2, "KP_DOWNARROW")]
    [TestCase(Keys.NumPad3, "KP_PGDN")]
    [TestCase(Keys.NumPad4, "KP_LEFTARROW")]
    [TestCase(Keys.NumPad5, "KP_5")]
    [TestCase(Keys.NumPad6, "KP_RIGHTARROW")]
    [TestCase(Keys.NumPad7, "KP_HOME")]
    [TestCase(Keys.NumPad8, "KP_UPARROW")]
    [TestCase(Keys.NumPad9, "KP_PGUP")]
    [TestCase(Keys.Divide, "KP_SLASH")]
    [TestCase(Keys.Multiply, "KP_MULTIPLY")]
    [TestCase(Keys.Subtract, "KP_MINUS")]
    [TestCase(Keys.Add, "KP_PLUS")]
    [TestCase(Keys.Decimal, "KP_DEL")]
    [TestCase(Keys.Oemcomma, ",")]
    [TestCase(Keys.OemPeriod, ".")]
    [TestCase(Keys.OemMinus, "-")]
    [TestCase(Keys.Oemplus, "=")]
    [TestCase(Keys.OemPipe, "\\")]
    [TestCase(Keys.Oemtilde, "`")]
    [TestCase(Keys.Scroll, "SCROLLLOCK")]
    [TestCase(Keys.Apps, "APP")]
    public void NameOf_AKeyWhoseTf2NameIsNotItsWinFormsName_IsTf2sName(Keys key, string name) =>
        KeyNames.NameOf(key).ShouldBe(name);
}
