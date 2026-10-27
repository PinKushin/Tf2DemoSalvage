using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A frame `demo_gototick` renders while skipping: the HUD hears its messages, and nothing is shown (B504).</summary>
public sealed class VguiHudSkipFrameTests
{
    /// <remarks>
    /// `ProcessGameEvent` (engine.dll FUN_1801f9510) and the user-message hooks have no skip test, so the chat's
    /// `HudChat.Message` is emitted — and deals — during a skip; the frame's paint is the viewer's to leave out.
    /// </remarks>
    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public void Frame_ASayTextOnASkipFrame_EmitsTheChatSoundAndPaintsOnlyWhenAsked(bool paint, int drawsAnything)
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        List<string> sounds = [];
        hud.Chat.SoundEmitter = sounds.Add;
        HudState state = new(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)]);

        host.BeginFrame(640, 480);
        hud.Frame(
            state,
            userMessages: [new SceneUserMessage(10, SceneUserMessage.SayText, (byte[])[0,.. Encoding.UTF8.GetBytes("hi"), 0, 1])],
            paint: paint);

        (string.Join(',', sounds), host.List.Quads.Count > 0 ? 1 : 0).ShouldBe(("HudChat.Message", drawsAnything));
    }

    /// <remarks>
    /// **Finding 105 B5.7: no sound from the half a HUD fork would move.** The same say-text through the split: `Think`
    /// emits the chat's sound and draws nothing; `Paint` draws the line and emits nothing, so a paint on a worker cannot
    /// reach `PlayHudSound`. The quads are the control that the paint drew what the think made.
    /// </remarks>
    [Test]
    public void Paint_AfterAThinkThatEmittedTheChatSound_DrawsTheLineAndEmitsNothing()
    {
        VguiSurfaceHost host = VguiHudTests.EmptySchemeHost();
        VguiHud hud = new(host, new EntityModelSet());
        List<string> sounds = [];
        hud.Chat.SoundEmitter = sounds.Add;
        HudState state = new(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: [new ScenePlayer(1, 0f, 0f, 0f, 2, 100, 3)]);

        host.BeginFrame(640, 480);
        hud.Think(state, userMessages: [new SceneUserMessage(10, SceneUserMessage.SayText, (byte[])[0, .. Encoding.UTF8.GetBytes("hi"), 0, 1])])
            .ShouldBeTrue();
        (string.Join(',', sounds), host.List.Quads.Count).ShouldBe(("HudChat.Message", 0), "the think emitted and drew nothing");

        hud.Paint(state, reset: false);

        (sounds.Count, host.List.Quads.Count > 0).ShouldBe((1, true));
    }
}
