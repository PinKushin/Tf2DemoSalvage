using System.Drawing;
using System.Threading;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>The windows wear the project's own icon, every size of it.</summary>
/// <remarks>
/// **Read through the pixels, not the reference**: a form whose icon fell back to WinForms' default would still hold an
/// <see cref="Icon"/>, so the test reads the ring's gold where the repo's `assets/icon.svg` draws it and the transparent hole beside
/// it. Coordinates are the SVG's 256 units scaled to the size asked: the ring spans y 12 to 30 at x 128, and the top hole
/// is centred at (128, 52).
/// </remarks>
[Apartment(ApartmentState.STA)]
public sealed class ProgramIconTests
{
    /// <remarks>
    /// 256 is checked by <see cref="ProgramIcon_Directory_ListsEverySizeFrom16To256"/> instead: every entry is
    /// PNG-compressed, but <see cref="Icon"/>'s size lookup passes over the width-0 entry the 256 uses, so asking for 256
    /// returns the 128 entry (measured in review, 2026-10-18).
    /// </remarks>
    [TestCase(16)]
    [TestCase(24)]
    [TestCase(32)]
    [TestCase(48)]
    [TestCase(64)]
    [TestCase(128)]
    public void ProgramIcon_AtEachSize_HasThatSizeDrawn(int size)
    {
        using Icon sized = new(MainForm.ProgramIcon, size, size);
        using Bitmap pixels = sized.ToBitmap();

        pixels.Width.ShouldBe(size);
        Color ring = pixels.GetPixel(size / 2, size * 21 / 256);
        Color hole = pixels.GetPixel(size / 2, size * 52 / 256);

        // Not exactly 255: at 24 px the ring's 18-unit band straddles two pixel rows, so the sampled row is ~75% covered
        // (measured: 191). A blank or hole-filled icon reads 0.
        ring.A.ShouldBeGreaterThan((byte)128);
        ring.R.ShouldBeGreaterThan(ring.B, "the ring is the brown-to-gold gradient, warm rather than blue");
        hole.A.ShouldBe((byte)0);
    }

    [Test]
    public void ProgramIcon_Directory_ListsEverySizeFrom16To256()
    {
        using System.IO.Stream file = typeof(MainForm).Assembly.GetManifestResourceStream("Tf2DemoSalvage.Viewer3D.icon.ico")!;
        using System.IO.BinaryReader read = new(file);

        read.ReadUInt16().ShouldBe((ushort)0);
        read.ReadUInt16().ShouldBe((ushort)1, "type 1 is an icon");
        int count = read.ReadUInt16();
        int[] sizes = new int[count];

        for (int entry = 0; entry < count; entry++)
        {
            byte width = read.ReadByte();
            sizes[entry] = width == 0 ? 256 : width;
            read.ReadBytes(15);
        }

        sizes.ShouldBe([16, 24, 32, 48, 64, 128, 256]);
    }

    [Test]
    public void MainForm_WhenBuilt_WearsTheProgramIcon()
    {
        using MainForm form = new();

        form.Icon.ShouldBeSameAs(MainForm.ProgramIcon);
    }

    [Test]
    public void StartupSplash_WhenBuilt_WearsTheProgramIcon()
    {
        using StartupSplash splash = new();

        splash.Icon.ShouldBeSameAs(MainForm.ProgramIcon);
    }
}
