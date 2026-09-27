using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// `attachedparticlesystem_t` (econ_item_schema.h:852) as `BInitAttributeControlledParticleSystems` fills it
/// (econ_item_schema.cpp:6135-6149): an unusual effect, a killstreak eye or a taunt effect, by index.
/// </summary>
/// <param name="Id">`nSystemID`: the index it is keyed by.</param>
public sealed class AttributeParticleSystem(int Id)
{
    /// <summary>`s_particle_controlpoint_names` (econ_item_schema.cpp:6076), `pszControlPoints`' keys.</summary>
    private static readonly string[] ControlPointNames =
        ["attachment", "control_point_1", "control_point_2", "control_point_3", "control_point_4", "control_point_5", "control_point_6"];

    private readonly string?[] _controlPoints = new string?[ControlPointNames.Length];

    /// <summary>`nSystemID`.</summary>
    public int Id { get; } = Id;

    /// <summary>`pszSystemName`: `system`, or null.</summary>
    public string? SystemName { get; private set; }

    /// <summary>`bFollowRootBone`: `attach_to_rootbone` != 0.</summary>
    public bool FollowRootBone { get; private set; }

    /// <summary>`fRefireTime`: `refire_time`, 0 by default.</summary>
    public float RefireTime { get; private set; }

    /// <summary>`bDrawInViewModel`: `draw_in_viewmodel`.</summary>
    public bool DrawInViewModel { get; private set; }

    /// <summary>`bUseSuffixName`: `use_suffix_name`.</summary>
    public bool UseSuffixName { get; private set; }

    /// <summary>`pszControlPoints[0..6]`: the attachment names, null where unset.</summary>
    public IReadOnlyList<string?> ControlPoints => _controlPoints;

    /// <summary>One key of the block.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">Its value.</param>
    internal void Apply(string key, string value)
    {
        int point = Array.FindIndex(ControlPointNames, name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase));

        if (point >= 0)
        {
            _controlPoints[point] = value;
            return;
        }

        switch (key.ToUpperInvariant())
        {
            case "SYSTEM":
                SystemName = value;
                break;
            case "ATTACH_TO_ROOTBONE":
                FollowRootBone = Number(value) != 0f;
                break;
            case "REFIRE_TIME":
                RefireTime = Number(value);
                break;
            case "DRAW_IN_VIEWMODEL":
                DrawInViewModel = Number(value) != 0f;
                break;
            case "USE_SUFFIX_NAME":
                UseSuffixName = Number(value) != 0f;
                break;
            default:
                break;
        }
    }

    /// <summary>`GetFloat`/`GetBool`'s reading: the number, 0 for text.</summary>
    private static float Number(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ? number : 0f;
}
