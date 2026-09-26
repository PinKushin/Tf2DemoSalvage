using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The scheme pass over a panel whose scheme settings add a sibling, as the death notice adds its streak banner.</summary>
public sealed class VguiLayoutSchemeTraverseTests
{
    [Test]
    public void SolveTraverse_APanelAddingASiblingInItsSchemePass_AppliesTheSiblingsSchemeToo()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english");
        VguiPanel root = new(null, "Root") { Wide = 640, Tall = 480 };
        Spawner spawner = new(root);

        VguiLayout.SolveTraverse(root, context);

        spawner.Spawned.ShouldNotBeNull().Applied.ShouldBeTrue();
    }

    private sealed class Spawner(VguiPanel parent) : VguiPanel(parent, "Spawner")
    {
        public Recorder? Spawned { get; private set; }

        public override void ApplySchemeSettings(VguiContext context)
        {
            base.ApplySchemeSettings(context);
            Spawned ??= new Recorder(Parent!);
        }
    }

    private sealed class Recorder(VguiPanel parent) : VguiPanel(parent, "Recorder")
    {
        public bool Applied { get; private set; }

        public override void ApplySchemeSettings(VguiContext context)
        {
            base.ApplySchemeSettings(context);
            Applied = true;
        }
    }
}
