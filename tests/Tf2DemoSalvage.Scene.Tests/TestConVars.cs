using System.Collections.Generic;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A <see cref="HudConVars"/> as a server that sent exactly these values: every other name reads its default.</summary>
internal static class TestConVars
{
    public static HudConVars Of(params (string Name, string Value)[] sent)
    {
        Dictionary<string, string> values = [];

        foreach ((string name, string value) in sent)
        {
            values[name] = value;
        }

        return new HudConVars(values.GetValueOrDefault);
    }

    /// <summary>`Key_LookupBinding` over one bound command.</summary>
    public static System.Func<string, string?> Binding(string command, string key) =>
        asked => asked == command ? key : null;
}
