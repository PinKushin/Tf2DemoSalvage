using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>The output-level check for demo voice: a real recording's speech reaching a sink through the viewer's path.</summary>
/// <remarks>
/// **z1800 because it is committed and carries voice** — 806 <c>vaudio_celt</c> packets (findings/02), so this runs on
/// every machine with the CELT library. The unit tests prove the plan and the presenter with values they chose; this is
/// the only one that fails if the timeline, the framing, the decoder and the presenter are not wired to each other.
/// </remarks>
public sealed class CorpusVoicePlaybackTests
{
    [Test]
    public void Update_PlayingZ1800ThroughItsVoice_StartsEachSpurtWhereThePlanPutsIt()
    {
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(Corpus.Demo("z1800")));
        VoicePlanResult plan = VoicePlan.From(timeline.VoiceInits, timeline.Voice, timeline.IntervalPerTick);

        if (plan.Refusal is { } refusal && plan.Spurts.Count == 0)
        {
            Assert.Ignore("voice could not be decoded on this machine: " + refusal);
        }

        int view = timeline.RecorderEntityIndex ?? 0;
        Sink sink = new();
        VoicePresenter presenter = new()
        {
            Plan = plan.Spurts,
            Interval = timeline.IntervalPerTick,

            // Inline, so each start lands on its own tick; the viewer decodes off the frame.
            Renderer = render => System.Threading.Tasks.Task.FromResult(render()),
        };
        int last = timeline.Frames[^1].Tick;

        for (int tick = timeline.Frames[0].Tick; tick <= last; tick++)
        {
            sink.Tick = tick;
            presenter.Update(sink, tick, jumped: false, (0f, 0f, 0f), (0f, -1f, 0f), tick * (double)timeline.IntervalPerTick, view, _ => null);
        }

        plan.Spurts.ShouldNotBeEmpty("z1800 carries 806 voice packets");
        sink.Played.Select(played => (played.Entity, played.Channel, played.Tick)).ShouldBe(
            plan.Spurts.Where(spurt => spurt.Length > 0).Select(spurt => (spurt.Proximity ? spurt.Entity : view, VoicePlan.EngineChannel(spurt.Channel), spurt.StartTick)),
            ignoreOrder: true);
        sink.Played.ShouldAllBe(played => played.Peak > 0f, "every spurt carries speech, not silence");
    }

    private sealed class Sink : IAudioSink
    {
        public int Tick { get; set; }

        public List<(int Entity, int Channel, int Tick, float Peak)> Played { get; } = [];

        public float MasterGain { get; set; } = 1f;

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel)
        {
            Played.Add((entity, channel, Tick, sample.Samples.ToArray().Max(System.MathF.Abs)));
            return 0;
        }

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetVoiceGain(int voice, float gain) => false;

        public bool SetVoicePitch(int voice, float pitch) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll()
        {
            // A straight play-through never seeks; nothing to record.
        }

        public int Reclaim() => 0;

        public void Silence(int entity, int channel)
        {
            // Only starts are asserted.
        }

        public int Silence(int entity, int channel, SoundSample sample) => 0;
    }
}
