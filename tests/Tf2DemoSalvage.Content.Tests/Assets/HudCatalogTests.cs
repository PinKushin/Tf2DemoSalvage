using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The HUD picker's list (D193) — every folder or <c>.vpk</c> in the program's OWN <c>custom/</c>
/// folder that carries <c>resource/</c> or <c>scripts/</c>, never anything from a user's TF2 install.
/// </summary>
public sealed class HudCatalogTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "tf2ds-hudcatalog-" + Guid.NewGuid());

    [Test]
    public void Find_NoCustomFolder_IsEmpty()
    {
        HudCatalog.Find(null).ShouldBeEmpty();
        HudCatalog.Find(Path.Combine(Path.GetTempPath(), "tf2ds-does-not-exist-" + Guid.NewGuid())).ShouldBeEmpty();
    }

    [Test]
    public void Find_AFolderWithResource_IsAHud()
    {
        string root = TempRoot();
        string hud = Path.Combine(root, "my_hud");
        Directory.CreateDirectory(Path.Combine(hud, "resource"));

        try
        {
            HudOption found = HudCatalog.Find(root).Single();

            found.Name.ShouldBe("my_hud");
            found.Path.ShouldBe(hud);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Find_AFolderWithScriptsOnly_IsAlsoAHud()
    {
        string root = TempRoot();
        string hud = Path.Combine(root, "scripts_only_hud");
        Directory.CreateDirectory(Path.Combine(hud, "scripts"));

        try
        {
            HudCatalog.Find(root).Single().Path.ShouldBe(hud);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Find_AFolderWithNeitherMarker_IsNotAHud()
    {
        string root = TempRoot();
        Directory.CreateDirectory(Path.Combine(root, "not_a_hud", "materials"));

        try
        {
            HudCatalog.Find(root).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Find_ANestedSubfolder_IsStillFound()
    {
        // D193: "we can sub folder" — a HUD need not sit directly under custom/.
        string root = TempRoot();
        string hud = Path.Combine(root, "group", "nested_hud");
        Directory.CreateDirectory(Path.Combine(hud, "resource"));

        try
        {
            HudCatalog.Find(root).Single().Path.ShouldBe(hud);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Options_AlwaysStartsWithTf2Default()
    {
        HudOption first = HudCatalog.Options(null)[0];

        first.Name.ShouldBe("TF2 default");
        first.Path.ShouldBeNull();
    }

    [Test]
    public void Options_WithAHudFound_ListsBothEntries()
    {
        string root = TempRoot();
        Directory.CreateDirectory(Path.Combine(root, "frag_hud", "resource"));

        try
        {
            HudCatalog.Options(root).Count.ShouldBe(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
