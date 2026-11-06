namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudTraining` (game/client/tf/tf_hud_training.cpp:123): the training goal, message and "press space" prompt.</summary>
/// <remarks>
/// **In a demo it carries no text**: `CHudTrainingMsg`'s `TrainingMsg` and `TrainingObjective` handlers return first when
/// `engine->IsPlayingDemo()` (tf_hud_trainingmessage.cpp:83, :101), so `SetTrainingText` and `SetTrainingObjective` never run
/// and the goal stays as `Reset` (:157) left it. What a demo can change is the prompt, by `m_bIsWaitingForTrainingContinue`.
/// **Not ported:** the freeze-cam screenshot check.
/// </remarks>
public sealed class TfHudTraining(VguiPanel parent) : VguiEditablePanel(parent, "ObjectiveStatusTraining")
{
    private VguiPanel? _pressSpace;

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:171).</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/HudTraining.res", context);
        _pressSpace = FindChildByName("PressSpacebarToContinue");
    }

    /// <summary>`Reset` (:157): the goal emptied and the prompt hidden.</summary>
    public void Reset()
    {
        SetDialogVariable("goal", string.Empty);

        if (_pressSpace is not null)
        {
            _pressSpace.Visible = false;
        }
    }

    /// <summary>`OnTick` (:291): the prompt follows `IsWaitingForTrainingContinue()`, blinking as it shows.</summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        bool show = state.Rules.WaitingForTrainingContinue;

        if (_pressSpace is null || show == _pressSpace.Visible)
        {
            return;
        }

        _pressSpace.Visible = show;
        HudViewport.Of(this)?.Animations.StartAnimationSequence(show ? "TrainingPressSpacebarBlink" : "TrainingPressSpacebarBlinkStop");
    }
}
