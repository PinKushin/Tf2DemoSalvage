using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The user messages `CBaseHudChat` hooks — `SayText`, `SayText2`, `TextMsg` — kept with their bodies, in stream order.</summary>
public sealed class SceneUserMessageTests
{
    [Test]
    public void UserMessages_ChatAndText_AreKeptVerbatimInOrder()
    {
        byte[] sayText2 = [1, 1, .. "TF_Chat_All\0"u8, .. "Blue\0"u8, .. "hi\0"u8, 0, 0];
        byte[] textMsg = [3, .. "#game_msg\0"u8, 0, 0, 0, 0];
        byte[] sayText = [0, .. "server\0"u8, 1];

        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoWithMessages(
            new UserMessage(ChatMessage.SayText2Type, "SayText2", sayText2.Length * 8, Body: sayText2),
            new UserMessage(SceneUserMessage.TextMsg, "TextMsg", textMsg.Length * 8, Body: textMsg),
            new UserMessage(SceneUserMessage.SayText, "SayText", sayText.Length * 8, Body: sayText),
            new UserMessage(6, "ResetHUD", 8, Body: new byte[] { 0 })));

        timeline.UserMessages.Select(message => (message.Tick, message.Type, System.Convert.ToHexString(message.Body.Span)))
            .ShouldBe(
            [
                (10, SceneUserMessage.SayText2, System.Convert.ToHexString(sayText2)),
                (10, SceneUserMessage.TextMsg, System.Convert.ToHexString(textMsg)),
                (10, SceneUserMessage.SayText, System.Convert.ToHexString(sayText)),
            ]);
    }
}
