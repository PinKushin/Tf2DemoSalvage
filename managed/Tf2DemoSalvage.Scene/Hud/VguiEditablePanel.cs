using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`vgui::EditablePanel`: a panel whose `.res` block reaches its children through its own build group.</summary>
/// <remarks>
/// EditablePanel.cpp: the constructor makes a build group and registers the panel itself first (:63);
/// `OnChildAdded` registers each child (:108); `ApplySettings` is the panel's own, then the group's, then
/// `skip_autoresize` (:803).
/// </remarks>
public class VguiEditablePanel : VguiPanel
{
    /// <summary>`EditablePanel( parent, panelName )`.</summary>
    /// <param name="parent">The parent, or null.</param>
    /// <param name="name">The panel's name, or null.</param>
    public VguiEditablePanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        OwnGroup = new VguiBuildGroup(this);
        OwnGroup.PanelAdded(this);
        BuildGroup = OwnGroup;
    }

    /// <inheritdoc/>
    public override string ClassName => "EditablePanel";

    /// <summary>`skip_autoresize`.</summary>
    public bool SkipAutoResize { get; private set; }

    /// <summary>`EditablePanel::_buildGroup` — distinct from the group this panel is registered in as someone's child.</summary>
    internal VguiBuildGroup OwnGroup { get; }

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);
        OwnGroup.ApplySettings(block, context);
        SkipAutoResize = Int(block, "skip_autoresize", 0) != 0;
    }

    /// <summary>`LoadControlSettings`: a loaded `.res` (resolution keys already applied) through the build group.</summary>
    /// <param name="resource">The file's root.</param>
    /// <param name="context">The scheme and screen.</param>
    /// <param name="conditions">`pConditions`: the condition blocks to promote, such as `if_mvm`.</param>
    public void LoadControlSettings(KeyValuesTree resource, VguiContext context, IReadOnlyList<string>? conditions = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (conditions is { Count: > 0 })
        {
            VguiBuildGroup.ProcessConditionalKeys(resource, conditions);
        }

        OwnGroup.ApplySettings(resource, context);
    }

    /// <inheritdoc/>
    protected override void OnChildAdded(VguiPanel child)
    {
        ArgumentNullException.ThrowIfNull(child);

        // The base constructor parents before `OwnGroup` exists; the constructor registers in the same order after.
        if (OwnGroup is null)
        {
            return;
        }

        child.BuildGroup = OwnGroup;
        OwnGroup.PanelAdded(child);
    }
}

/// <summary>`vgui::BuildGroup`, the part that applies a `.res` (BuildGroup.cpp).</summary>
public sealed class VguiBuildGroup(VguiEditablePanel parent)
{
    private readonly List<VguiPanel> _panels = [];

    /// <summary>`PanelAdded`: registered once, in order.</summary>
    /// <param name="panel">The panel.</param>
    internal void PanelAdded(VguiPanel panel)
    {
        if (!_panels.Contains(panel))
        {
            _panels.Add(panel);
        }
    }

    /// <summary>`ApplySettings` (:1234): each block to the first registered panel of its name, else a new control.</summary>
    /// <param name="resource">The blocks.</param>
    /// <param name="context">The scheme and screen.</param>
    internal void ApplySettings(KeyValuesTree resource, VguiContext context)
    {
        foreach (KeyValuesTree block in resource.Children)
        {
            // `GetDataType() != TYPE_NONE`: atomic keys are skipped.
            if (block.Value is not null)
            {
                continue;
            }

            VguiPanel? panel = _panels.Find(candidate => string.Equals(candidate.Name, block.Name, StringComparison.OrdinalIgnoreCase));

            if (panel is not null)
            {
                panel.ApplySettings(block, context);
            }
            else
            {
                NewControl(block, context);
            }
        }
    }

    /// <summary>`NewControl( controlKeys )` (:1331): made by `ControlName`, parented, named, then set.</summary>
    private void NewControl(KeyValuesTree block, VguiContext context)
    {
        if (VguiControlFactory.Create(block.Find("ControlName")?.Value ?? string.Empty) is not { } panel)
        {
            return;
        }

        panel.SetParent(parent);
        panel.Name = block.Name;
        panel.ApplySettings(block, context);
    }

    /// <summary>`ProcessConditionalKeys` (:1021): in every block at every depth, a sub-block named by a condition has its keys promoted.</summary>
    /// <param name="data">The block.</param>
    /// <param name="conditions">The conditions' names.</param>
    public static void ProcessConditionalKeys(KeyValuesTree data, IReadOnlyList<string> conditions)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(conditions);

        foreach (KeyValuesTree subKey in data.Children)
        {
            ProcessConditionalKeys(subKey, conditions);

            foreach (string condition in conditions)
            {
                if (subKey.Find(condition) is not { } conditionBlock)
                {
                    continue;
                }

                foreach (KeyValuesTree overriding in conditionBlock.Children)
                {
                    if (subKey.Find(overriding.Name) is { } existing)
                    {
                        existing.SetValue(overriding.Value ?? string.Empty);
                    }
                    else
                    {
                        subKey.AddCopy(overriding);
                    }
                }
            }
        }
    }
}

/// <summary>`CBuildFactoryHelper`: `DECLARE_BUILD_FACTORY`'s registry of control classes by name.</summary>
public static class VguiControlFactory
{
    // `InstancePanel` compares with `Q_stricmp` (BuildFactoryHelper.cpp:82).
    private static readonly Dictionary<string, Func<VguiPanel>> Factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Panel"] = () => new VguiPanel(null, null),
        ["EditablePanel"] = () => new VguiEditablePanel(null, null),
        ["Label"] = () => new VguiLabel(null, null),
        ["ImagePanel"] = () => new VguiImagePanel(null, null),
    };

    /// <summary>`InstancePanel`: a new control of that class, or null when none is registered.</summary>
    /// <param name="className">The `ControlName`.</param>
    /// <returns>The control, unparented.</returns>
    public static VguiPanel? Create(string className) => Factories.TryGetValue(className, out Func<VguiPanel>? factory) ? factory() : null;

    /// <summary>`DECLARE_BUILD_FACTORY`.</summary>
    /// <param name="className">The class name.</param>
    /// <param name="factory">What makes one.</param>
    public static void Register(string className, Func<VguiPanel> factory) => Factories[className] = factory;
}
