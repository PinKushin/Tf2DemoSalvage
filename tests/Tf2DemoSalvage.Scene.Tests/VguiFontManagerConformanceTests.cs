using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CFontManager`: which `CWin32Font` draws which character of a font handle.</summary>
/// <remarks>
/// vguimatsurface.dll, renamed in `tf2vguimatsurface`: `CFontManager_SetFontGlyphSet` (0x180017590) — a face that is not
/// foreign-capable (only Marlett is, of Win32 fonts) takes 0x00–0xFF, or the custom font file's range, and Tahoma the
/// rest; a face that will not create falls to Tahoma, then along `g_FallbackFonts`. `CreateOrFindWin32Font`
/// (0x180016770) shares one font between every handle asking for the same face and size. `CFontAmalgam`
/// (0x18001c570, 0x18001c5c0, 0x18001c3b0): the first range holding the character, else the first font.
/// </remarks>
public sealed class VguiFontManagerConformanceTests
{
    [Test]
    public void SetFontGlyphSet_AnOrdinaryFace_DrawsLatin1ItselfAndTahomaTheRest()
    {
        VguiFontManager manager = new(new FakeGdi());
        VguiFontAmalgam handle = manager.CreateFont();

        manager.SetFontGlyphSet(handle, Set("TF2 Build"), 0, 0).ShouldBeTrue();

        handle.GetFontForChar('A')!.GlyphSet.Name.ShouldBe("TF2 Build");
        handle.GetFontForChar(0xff)!.GlyphSet.Name.ShouldBe("TF2 Build");
        handle.GetFontForChar(0x100)!.GlyphSet.Name.ShouldBe("Tahoma");
    }

    [Test]
    public void SetFontGlyphSet_ARange_PutsTheFaceInsideItAndTahomaEitherSide()
    {
        VguiFontManager manager = new(new FakeGdi());
        VguiFontAmalgam handle = manager.CreateFont();

        manager.SetFontGlyphSet(handle, Set("TF2"), 0x7f, 0x20);

        handle.GetFontForChar(0x1f)!.GlyphSet.Name.ShouldBe("Tahoma");
        handle.GetFontForChar(0x20)!.GlyphSet.Name.ShouldBe("TF2", "the bounds are put in order");
        handle.GetFontForChar(0x7f)!.GlyphSet.Name.ShouldBe("TF2");
        handle.GetFontForChar(0x80)!.GlyphSet.Name.ShouldBe("Tahoma");
    }

    [TestCase("Tahoma")]
    [TestCase("Marlett")]
    public void SetFontGlyphSet_TahomaOrMarlett_CoversEverythingAlone(string face)
    {
        FakeGdi gdi = new();
        VguiFontManager manager = new(gdi);
        VguiFontAmalgam handle = manager.CreateFont();

        manager.SetFontGlyphSet(handle, Set(face), 0, 0);

        handle.GetFontForChar(0x4e00)!.GlyphSet.Name.ShouldBe(face);
        gdi.Created.ShouldBe([face]);
    }

    [Test]
    public void SetFontGlyphSet_AFaceGdiDoesNotHave_IsTahomaThroughout()
    {
        VguiFontManager manager = new(new FakeGdi { Families = ["Tahoma"] });
        VguiFontAmalgam handle = manager.CreateFont();

        manager.SetFontGlyphSet(handle, Set("Missing Face"), 0, 0).ShouldBeTrue();

        handle.GetFontForChar('A')!.GlyphSet.Name.ShouldBe("Tahoma");
    }

    [Test]
    public void SetFontGlyphSet_WithoutTahoma_TheFallbackTableLeadsNowhere()
    {
        // Times New Roman → Courier New, which creates — but a face is kept only with its Tahoma extension, so it is
        // rejected; Courier New → Courier → (unlisted) Tahoma → nothing.
        FakeGdi gdi = new() { Families = ["Courier New"] };
        VguiFontManager manager = new(gdi);

        manager.SetFontGlyphSet(manager.CreateFont(), Set("Times New Roman"), 0, 0).ShouldBeFalse();
        gdi.Created.ShouldContain("Courier New", "the walk did reach it");
    }

    [Test]
    public void SetFontGlyphSet_NothingCreates_Fails() =>
        new VguiFontManager(new FakeGdi { Families = [] }).SetFontGlyphSet(new VguiFontManager(new FakeGdi()).CreateFont(), Set("Anything"), 0, 0).ShouldBeFalse();

    [Test]
    public void CreateOrFind_TheSameFaceAndSize_IsOneFontSharedByBothHandles()
    {
        FakeGdi gdi = new();
        VguiFontManager manager = new(gdi);
        VguiFontAmalgam first = manager.CreateFont();
        VguiFontAmalgam second = manager.CreateFont();

        manager.SetFontGlyphSet(first, Set("Tahoma"), 0, 0);
        manager.SetFontGlyphSet(second, Set("Tahoma"), 0, 0);

        second.GetFontForChar('A').ShouldBeSameAs(first.GetFontForChar('A'));
        gdi.Created.Count.ShouldBe(1);
    }

    [Test]
    public void GetCharAbcWide_AnEmptyHandle_IsZeroGlyphZero()
    {
        VguiFontManager manager = new(new FakeGdi());

        manager.GetCharAbcWide(manager.CreateFont(), 'A').ShouldBe((0, 0, 0));
    }

    [Test]
    public void GetFontTall_IsTheFirstFontsHeight()
    {
        VguiFontManager manager = new(new FakeGdi());
        VguiFontAmalgam handle = manager.CreateFont();

        manager.SetFontGlyphSet(handle, Set("TF2 Build", VguiFont.Outline), 0, 0);

        manager.GetFontTall(handle).ShouldBe(20 + 2);
    }

    private static VguiFont Set(string face, int flags = 0) => new(face, 20, 400, 0, 0, flags, 1f, 1f);
}
