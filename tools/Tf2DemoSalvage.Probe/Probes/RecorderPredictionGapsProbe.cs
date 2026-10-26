using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// B604: prediction against the server's own answer, packet gap by packet gap, and what the gaps it zeroes have in common.
/// </summary>
/// <remarks>
/// `CorpusRecorderPredictionTests`' control on a whole demo: from each packet, the commands the NEXT packet acknowledges —
/// sequences (ack, next ack], selected by sequence because this demo's acknowledgement lags its reads — are re-run through
/// `RecorderPrediction.PredictFrom` (the production route, wired as the viewer wires it) and set beside that next packet's
/// networked velocity. A gap is "zeroed" where prediction's horizontal speed is under `MOVING_MINIMUM_SPEED` and the server's
/// over it — B604's failure. Every compared gap is tagged by what an unswept hull at the restored origin, and a hull swept
/// the way he was going, meet in `MovementWorld.At` — the world prediction traces — so a zeroed gap's tags have the
/// agreeing gaps' rates beside them as the baseline.
/// </remarks>
public sealed class RecorderPredictionGapsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "recorder-prediction-gaps";

    /// <inheritdoc/>
    public string Summary => "B604: predicted vs the next packet's networked velocity on every packet gap, zeroed gaps classified: recorder-prediction-gaps <demo> [print]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        if (!ProbeArguments.TryCount(arguments, 1, 20, 2, output, out int print) || PovMatch.Open(path, output) is not { } match)
        {
            return;
        }

        DemoTimeline timeline = match.Timeline;
        _ = match.Moments(out _, out RecorderPrediction prediction, wired: true);
        MapLevel level = match.Map.Level ?? throw new InvalidOperationException("no level");
        IReadOnlyList<(ScenePropTrack Track, int HeadNode)> brushTracks =
            level.BrushModels is { } models ? MovementWorld.BrushTracks(timeline.Props, models) : [];
        IReadOnlyList<(int Tick, int Acknowledged)> packets = timeline.PacketAcknowledgements;
        IReadOnlyList<RecordedUserCommand> commands = timeline.UserCommands;
        Dictionary<int, int> tickOfSequence = [];

        foreach (RecordedUserCommand command in commands)
        {
            tickOfSequence[command.Sequence] = command.Tick;
        }

        int alive = 0, compared = 0, declined = 0, zeroed = 0, agree = 0, dormantEnemies = 0;
        List<float> errors = [];
        Dictionary<string, (int Zeroed, int Agreeing)> tags = [];
        int printed = 0;

        for (int index = 0; index + 1 < packets.Count; index++)
        {
            (int tick, int acknowledged) = packets[index];
            (int nextTick, int nextAcknowledged) = packets[index + 1];

            if (Alive(timeline, match.Recorder, tick) is not { Velocity: { } held } before ||
                Alive(timeline, match.Recorder, nextTick) is not { Velocity: { } truth })
            {
                continue;
            }

            alive++;

            if (nextAcknowledged <= acknowledged || !tickOfSequence.TryGetValue(nextAcknowledged, out int through))
            {
                continue;
            }

            IReadOnlyList<RecordedUserCommand> pending = RecorderPrediction.Pending(commands, acknowledged, through);

            if (pending.Count == 0 || pending[^1].Sequence != nextAcknowledged)
            {
                continue;
            }

            if (prediction.PredictFrom((tick, acknowledged), through) is not { } predicted)
            {
                declined++;
                continue;
            }

            compared++;
            float error = Xy((predicted.X - truth.X, predicted.Y - truth.Y, 0f));
            errors.Add(error);
            bool agrees = error < 1f;
            agree += agrees ? 1 : 0;
            bool isZeroed = Xy(predicted) < 0.5f && Xy(truth) > 0.5f;
            zeroed += isZeroed ? 1 : 0;

            foreach (ScenePlayer other in timeline.PlayersAt(tick))
            {
                if (other.IsAlive && other.Team is 2 or 3 && other.Team != before.Team && timeline.Dormancy.IsDormant(other.EntityIndex, tick))
                {
                    dormantEnemies++;
                }
            }

            if (!isZeroed && !agrees)
            {
                continue;
            }

            // The restored state, and an unswept hull at the restored origin through the world prediction traces.
            int flags = before.Flags ?? 0;
            bool ducked = before.Movement?.Ducked ?? (flags & 2) != 0;
            MovementWorld world = MovementWorld.At(timeline, brushTracks, level, tick, before);
            int mask = MovementWorld.MaskPlayerSolid | (before.Team switch { 2 => MovementWorld.ContentsBlueTeam, 3 => MovementWorld.ContentsRedTeam, _ => 0 });
            Vector3 origin = new(before.X, before.Y, before.Z);
            Vector3 mins = new(-24f, -24f, 0f);
            Vector3 maxs = new(24f, 24f, ducked ? 62f : 82f);
            BspTrace? stuck = world.Trace(origin, origin, mins, maxs, mask);

            // The way he was going, as far as the commands would carry him at the held speed: what it meets first.
            Vector3 along = origin + (new Vector3(held.X, held.Y, 0f) * (pending.Count * timeline.IntervalPerTick));
            BspTrace? ahead = world.Trace(origin, along, mins, maxs, mask);

            string Met(BspTrace? trace) => trace switch
            {
                null => "none",
                { StartSolid: true, BrushEntity: var e } => $"start-in {Kind(timeline, e, tick)}",
                { Fraction: < 1f, BrushEntity: var e } => $"hit {Kind(timeline, e, tick)}",
                _ => "clear",
            };

            foreach (string tag in new[]
                     {
                         $"origin {Met(stuck)}",
                         $"ahead {Met(ahead)}",
                         $"ground {(flags & 1) != 0}",
                         $"ducked {ducked}",
                         $"water {before.WaterLevel ?? 0}",
                         $"cmds {Math.Min(pending.Count, 6)}",
                         $"held {(Xy(held) > 0.5f ? "moving" : "still")}",
                     })
            {
                (int z, int a) = tags.GetValueOrDefault(tag);
                tags[tag] = isZeroed ? (z + 1, a) : (z, a + 1);
            }

            if (isZeroed && printed++ < print)
            {
                int firstStill = -1;

                for (int k = 0; k < pending.Count; k++)
                {
                    if (prediction.PredictFrom((tick, acknowledged), pending[k].Tick) is { } partial && Xy(partial) < 0.5f)
                    {
                        firstStill = k;
                        break;
                    }
                }

                UserCommand first = pending[0].Command;
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  gap {tick}->{nextTick} (cmds to {through}): cmds {pending.Count}, held {Xy(held):0.#}, predicted {Xy(predicted):0.#} ({predicted.X:0.#},{predicted.Y:0.#},{predicted.Z:0.#}), server {Xy(truth):0.#}; " +
                    $"origin ({before.X:0.##},{before.Y:0.##},{before.Z:0.##}), flags 0x{flags:x}, class {before.PlayerClass}, state {before.PlayerState}, maxspeed {before.MaxSpeed}, cond 0x{before.Conditions.Cond:x}/{before.Conditions.Ex:x}, movement {(before.Movement is null ? "absent" : "present")}; " +
                    $"cmd0 fwd {first.ForwardMove} side {first.SideMove} buttons 0x{first.Buttons:x}; first still cmd {firstStill}; origin {Met(stuck)}; ahead {Met(ahead)}"));
            }
        }

        errors.Sort();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: recorder {match.Recorder}; {packets.Count} packets, {alive} gaps with him alive at both ends, {compared} compared, {declined} declined; " +
            $"|dv| xy mean {errors.DefaultIfEmpty().Average():0.###}, median {(errors.Count == 0 ? 0 : errors[errors.Count / 2]):0.###}; " +
            $"within 1 u/s {agree} ({100d * agree / Math.Max(1, compared):0.0}%); zeroed (predicted < 0.5, server > 0.5) {zeroed} ({100d * zeroed / Math.Max(1, compared):0.0}%)"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"control: dormant living enemies summed over compared gaps {dormantEnemies} (zero would mean the dormancy reading is broken)"));
        output.WriteLine("  tag: zeroed (% of zeroed) | agreeing (% of agreeing)");

        foreach ((string tag, (int z, int a)) in tags.OrderByDescending(pair => pair.Value.Zeroed))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {tag,-40} {z,6} ({100d * z / Math.Max(1, zeroed),5:0.0}%) | {a,6} ({100d * a / Math.Max(1, agree),5:0.0}%)"));
        }
    }

    /// <summary>What a trace's entity is — the world, a player, a building or a brush entity by class — and whether dormant then.</summary>
    private static string Kind(DemoTimeline timeline, int entity, int tick)
    {
        if (entity <= 0)
        {
            return "world";
        }

        string what = "player";

        if (entity > timeline.MaxClients)
        {
            what = timeline.BuildingsAt(tick).Any(building => building.EntityIndex == entity)
                ? "building"
                : timeline.Props.FirstOrDefault(track => track.EntityIndex == entity && track.Alive(tick))?.ClassName ?? "entity?";
        }

        return timeline.Dormancy.IsDormant(entity, tick) ? what + " DORMANT" : what;
    }

    private static ScenePlayer? Alive(DemoTimeline timeline, int recorder, int tick) =>
        timeline.PlayersAt(tick).FirstOrDefault(player => player.EntityIndex == recorder && player.IsAlive);

    private static float Xy((float X, float Y, float Z) v) => MathF.Sqrt((v.X * v.X) + (v.Y * v.Y));
}
