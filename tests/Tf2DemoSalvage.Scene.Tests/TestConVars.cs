using System.Collections.Generic;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A <see cref="HudConVars"/> where exactly these values were set — by the server for a replicated var, by the
/// watcher's config for a client one; every other name reads its default.</summary>
internal static class TestConVars
{
    public static HudConVars Of(params (string Name, string Value)[] sent)
    {
        Dictionary<string, string> values = [];

        foreach ((string name, string value) in sent)
        {
            values[name] = value;
        }

        return new HudConVars(values.GetValueOrDefault, values.GetValueOrDefault);
    }

    /// <summary>These ConVars with one more value set, the rest as they were.</summary>
    public static HudConVars With(this HudConVars conVars, string name, string value) =>
        new(asked => asked == name ? value : conVars.Find?.Invoke(asked),
            asked => asked == name ? value : conVars.Client?.Invoke(asked));

    /// <summary>`tf_use_match_hud` set to the watcher's choice.</summary>
    public static HudState WithMatchHud(this HudState state, bool useMatchHud) =>
        state with { ConVars = state.ConVars.With("tf_use_match_hud", useMatchHud ? "1" : "0") };

    /// <summary>`Key_LookupBinding` over one bound command.</summary>
    public static System.Func<string, string?> Binding(string command, string key) =>
        asked => asked == command ? key : null;
}
