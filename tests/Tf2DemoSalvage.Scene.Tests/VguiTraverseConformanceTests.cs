using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CMatSystemSurface::SolveTraverse`'s three passes, as `vguimatsurface.dll` runs them.</summary>
/// <remarks>
/// Closed code, renamed in `tf2vguimatsurface`: `InternalSchemeSettingsTraverse` (0x1800109e0) recurses into visible
/// children (all of them when forced) BEFORE applying the panel's own scheme; `InternalThinkTraverse` (0x180010d90) thinks
/// the panel first, then visible children; `InternalSolveTraverse` (0x180010c10) solves the panel, then visible children.
/// `Panel::Think` (Panel.cpp) lays out a visible panel that needs it — never before its scheme is applied.
/// </remarks>
public sealed class VguiTraverseConformanceTests
{
    [Test]
    public void SolveTraverse_SchemeSettings_ReachChildrenBeforeTheirParent()
    {
        List<string> log = [];
        LoggingPanel parent = new(null, "Parent", log);
        _ = new LoggingPanel(parent, "Child", log);

        VguiLayout.SolveTraverse(parent, Context());

        log.ShouldBe(["scheme Child", "scheme Parent", "layout Parent", "layout Child"]);
    }

    [Test]
    public void SolveTraverse_AnInvisibleChild_IsNeitherSchemedNorLaidOutNorSolved()
    {
        List<string> log = [];
        LoggingPanel parent = new(null, "Parent", log) { X = 5 };
        LoggingPanel hidden = new(parent, "Hidden", log) { Visible = false, X = 7 };

        VguiLayout.SolveTraverse(parent, Context());

        log.ShouldBe(["scheme Parent", "layout Parent"]);
        hidden.AbsX.ShouldBe(0, "never solved");
    }

    [Test]
    public void SolveTraverse_Forced_SchemesInvisibleChildrenToo()
    {
        List<string> log = [];
        LoggingPanel parent = new(null, "Parent", log);
        _ = new LoggingPanel(parent, "Hidden", log) { Visible = false };

        VguiLayout.SolveTraverse(parent, Context(), forceApplySchemeSettings: true);

        log.ShouldContain("scheme Hidden");
    }

    [Test]
    public void SolveTraverse_ASecondTime_DoesNotLayOutAgainUntilInvalidated()
    {
        List<string> log = [];
        LoggingPanel panel = new(null, "Panel", log);
        VguiContext context = Context();

        VguiLayout.SolveTraverse(panel, context);
        VguiLayout.SolveTraverse(panel, context);
        panel.InvalidateLayout();
        VguiLayout.SolveTraverse(panel, context);

        log.ShouldBe(["scheme Panel", "layout Panel", "layout Panel"]);
    }

    private sealed class LoggingPanel(VguiPanel? parent, string name, List<string> log) : VguiPanel(parent, name)
    {
        public override void ApplySchemeSettings(VguiContext context)
        {
            log.Add($"scheme {Name}");
            base.ApplySchemeSettings(context);
        }

        protected override void PerformLayout() => log.Add($"layout {Name}");
    }

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english");
    }
}
