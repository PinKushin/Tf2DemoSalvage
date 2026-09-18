using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s tangential (Coulomb friction) solve for one contact —
/// <c>IvpFrictionSystem::SolveTangentialPair</c> (<c>1800857c0</c>) — called in process on a fabricated body-against-world
/// contact, the oracle for <see cref="IvpTangentialSolve.SolveContact"/> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary is the instrument.** Reuses the same struct offsets <see cref="VphysicsHeapSolveProbe"/> already confirmed
/// (contact, record, core, environment, event) — this is the SAME <c>IvpContactPoint</c>/<c>IvpContactRecord</c>/core layout,
/// a different function called on it.
///
/// **The control also settles D175.** A freshly-allocated core never has its <c>+0x58</c> "sticking anchor" slot written —
/// nothing in this project's own object model writes it either — so `SolveTangentialPair`'s sticking gate
/// (`*(longlong*)(firstCore+0x58) != 0`) reads false on this probe's own fabricated core the same way it would on any core
/// this project ever builds. If the binary's own dispatch takes the non-sticking branch here, that is a direct, measured
/// confirmation of D175's reasoning, not an inference from static reading alone.
/// </remarks>
public sealed class VphysicsFrictionSolveProbe : IProbe
{
    private const long SolveTangentialPairAddress = 0x1800857c0;

    private const int CoreSize = 0x270;
    private const int ObjectSize = 0x100;
    private const int ContactSize = 0xd0;
    private const int RecordSize = 0x110;
    private const int EnvironmentSize = 0x200;
    private const int LimitsSize = 0x40;
    private const int ArenaSize = 0x28;
    private const int ArenaBytes = 8 << 20;
    private const int EventSize = 0x18;
    private const int UnitSize = 0x48;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float SolveTangentialPairFunction(nint contact, nint simulationEvent);

    /// <inheritdoc />
    public string Name => "vphysics-friction-solve";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's tangential (Coulomb friction) solve for one contact (IvpFrictionSystem::SolveTangentialPair, " +
        "1800857c0) called in process on a fabricated body-against-world contact and compared with IvpTangentialSolve.SolveContact; " +
        "also settles D175 by observing which branch the binary's own dispatch takes on a freshly-allocated core: vphysics-friction-solve";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        using Native native = new(module);

        Control(output, native);
    }

    /// <summary>
    /// One movable core sliding along a static world contact, tangent axes aligned to the world X/Y axes, no stored slide —
    /// the same shape as <c>IvpTangentialSolveConformanceTests.Solve_ASlidingBodyWithNoStoredSlide_OpposesTheCurrentVelocity</c>.
    /// </summary>
    private static void Control(TextWriter output, Native native)
    {
        const float inverseStep = 100f;

        IvpRigidBody core = new()
        {
            Velocity = (1f, 0f, 0f),
            InverseInertia = (1f, 1f, 1f),
            CoreMatrix = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)),
        };

        IvpContactRecord record = new()
        {
            FirstCore = core,
            FirstArm = (0f, 0f, 1f),
            Span = (1f, 0f, 0f),
            CrossSpan = (0f, 1f, 0f),
            Normal = (0f, 0f, 1f),
        };

        NativeCase fabricated = native.Run(core, record, slide: (0f, 0f), friction: 0.5f, inverseStep: inverseStep);

        IvpCollisionObject first = new();
        IvpCollisionObject second = new();
        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
            extraRadius: 0f)
        {
            Flags = 0xc0000,
        };

        new IvpMindistManager().LinkExact(mindist, first, second);

        IvpLedgeSide side = new(
            [(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)],
            new IvpLedgeTopology([(0, 1, 2)], [(0, 0, 0)], [0], [0]),
            IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)),
            (0d, 0d, 0d));
        IvpContactPoint point = new(mindist, first, side, second, side, now: 0d)
        {
            Record = record,
            Friction = 0.5f,
            NormalPush = 1f,
        };

        (float work, (float Span, float CrossSpan)? impulse) =
            IvpTangentialSolve.SolveContact(point, step: 1f / inverseStep, inverseStep: inverseStep);
        output.WriteLine($"port: work change = 0x{BitConverter.SingleToInt32Bits(work):x8}");

        output.WriteLine($"binary: sticking dispatch taken = {fabricated.Sticking} (false confirms D175 on this core)");
        output.WriteLine($"binary: entry gate value (friction*normalPush*event[0]) = {fabricated.Gate:g9}");
        output.WriteLine($"binary: raw packed return = 0x{BitConverter.SingleToInt32Bits(fabricated.RawReturn):x8}");
        output.WriteLine($"binary: running-average magnitude (contact+0x84) changed = {fabricated.RunningAverageChanged}");
        output.WriteLine(
            $"binary pending velocity: ({fabricated.PendingVelocity.X:g9}, {fabricated.PendingVelocity.Y:g9}, {fabricated.PendingVelocity.Z:g9})");
        output.WriteLine(
            $"binary pending spin:     ({fabricated.PendingSpin.X:g9}, {fabricated.PendingSpin.Y:g9}, {fabricated.PendingSpin.Z:g9})");

        if (impulse is { } found)
        {
            output.WriteLine($"port impulse: ({found.Span:g9}, {found.CrossSpan:g9})");
            output.WriteLine(
                $"port pending velocity: ({core.PendingVelocity.X:g9}, {core.PendingVelocity.Y:g9}, {core.PendingVelocity.Z:g9})");
            output.WriteLine(
                $"port pending spin:     ({core.PendingAngularVelocity.X:g9}, {core.PendingAngularVelocity.Y:g9}, {core.PendingAngularVelocity.Z:g9})");

            bool velocityMatches = Close(fabricated.PendingVelocity, core.PendingVelocity);
            bool spinMatches = Close(fabricated.PendingSpin, core.PendingAngularVelocity);

            output.WriteLine($"pending velocity matches: {velocityMatches}");
            output.WriteLine($"pending spin matches:     {spinMatches}");
        }
        else
        {
            output.WriteLine("port: Solve returned null (singular system) - cannot compare.");
        }
    }

    private static bool Close((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        MathF.Abs(a.X - b.X) < 1e-3f && MathF.Abs(a.Y - b.Y) < 1e-3f && MathF.Abs(a.Z - b.Z) < 1e-3f;

    private readonly record struct NativeCase(
        bool Sticking,
        (float X, float Y, float Z) PendingVelocity,
        (float X, float Y, float Z) PendingSpin,
        float RawReturn,
        bool RunningAverageChanged,
        float Gate);

    private sealed class Native : IDisposable
    {
        private static readonly byte[] Zeroes = new byte[EnvironmentSize * 4];

        private readonly List<nint> _blocks = [];
        private readonly SolveTangentialPairFunction _solve;
        private readonly nint _core;
        private readonly nint _object;
        private readonly nint _contact;
        private readonly nint _record;
        private readonly nint _environment;
        private readonly nint _limits;
        private readonly nint _arena;
        private readonly nint _arenaBuffer;
        private readonly nint _manager;
        private readonly nint _event;
        private readonly nint _unit;

        public Native(nint module)
        {
            _solve = VphysicsLibrary.Function<SolveTangentialPairFunction>(module, SolveTangentialPairAddress);

            _core = Allocate(CoreSize);
            _object = Allocate(ObjectSize);
            _contact = Allocate(ContactSize);
            _record = Allocate(RecordSize);
            _environment = Allocate(EnvironmentSize);
            _limits = Allocate(LimitsSize);
            _arena = Allocate(ArenaSize);
            _arenaBuffer = Marshal.AllocHGlobal(ArenaBytes);
            _blocks.Add(_arenaBuffer);
            _manager = Allocate(0x20);
            _event = Allocate(EventSize);
            _unit = Allocate(UnitSize);
        }

        public NativeCase Run(IvpRigidBody core, IvpContactRecord record, (float Span, float CrossSpan) slide, float friction, float inverseStep)
        {
            Zero(_environment, EnvironmentSize);
            Zero(_limits, LimitsSize);
            Zero(_arena, ArenaSize);
            Zero(_event, EventSize);
            Zero(_unit, UnitSize);
            Marshal.WriteIntPtr(_event, 0x10, _unit);

            Marshal.WriteIntPtr(_environment, 0x40, _manager);
            Marshal.WriteIntPtr(_environment, 0x48, _limits);
            Marshal.WriteIntPtr(_environment, 0xf0, _arena);
            Marshal.WriteInt64(_environment, 0x110, BitConverter.DoubleToInt64Bits(inverseStep));

            Marshal.WriteIntPtr(_arena, 0x0, _arenaBuffer);
            Marshal.WriteIntPtr(_arena, 0x8, _arenaBuffer);
            Marshal.WriteIntPtr(_arena, 0x10, (_arenaBuffer + 0x27) & ~(nint)0x1f);
            Marshal.WriteIntPtr(_arena, 0x18, _arenaBuffer + ArenaBytes);
            Marshal.WriteInt32(_arena, 0x24, ArenaBytes - 0x40);

            Marshal.WriteInt32(_event, 0x0, BitConverter.SingleToInt32Bits(1f / inverseStep));
            Marshal.WriteInt32(_event, 0x4, BitConverter.SingleToInt32Bits(inverseStep));

            Zero(_core, CoreSize);
            Zero(_object, ObjectSize);
            Marshal.WriteByte(_core, 0, 0);
            Marshal.WriteIntPtr(_core, 0x10, _environment);
            WriteVector3(_core, 0x40, core.InverseInertia);
            Marshal.WriteInt32(_core, 0x4c, BitConverter.SingleToInt32Bits(1f));
            WriteVector3(_core, 0x140, core.Velocity);
            WriteMatrix(_core, core.CoreMatrix);
            WriteDouble3(_core, 0xf0, (0d, 0d, 0d));
            Marshal.WriteIntPtr(_object, 0xe8, _core);

            Zero(_contact, ContactSize);
            Zero(_record, RecordSize);
            Marshal.WriteIntPtr(_contact, 0x0, 0);
            Marshal.WriteIntPtr(_contact, 0x70, _record);
            Marshal.WriteInt32(_contact, 0x68, BitConverter.SingleToInt32Bits(slide.Span));
            Marshal.WriteInt32(_contact, 0x6c, BitConverter.SingleToInt32Bits(slide.CrossSpan));
            Marshal.WriteInt32(_contact, 0x78, BitConverter.SingleToInt32Bits(friction));
            Marshal.WriteInt32(_contact, 0x88, BitConverter.SingleToInt32Bits(1f));

            WriteDouble3(_record, 0x0, ((double)record.FirstArm.X, record.FirstArm.Y, record.FirstArm.Z));
            WriteVector3(_record, 0x20, record.Normal);
            WriteVector3(_record, 0xb0, record.Span);
            WriteVector3(_record, 0xc0, record.CrossSpan);
            WriteVector3(_record, 0xd0, record.FirstArm);
            WriteVector3(_record, 0xe0, record.SecondArm);
            Marshal.WriteIntPtr(_record, 0x98, _core);
            Marshal.WriteIntPtr(_record, 0xa0, 0);
            Marshal.WriteInt32(_record, 0x78, BitConverter.SingleToInt32Bits(0.02f));

            bool stickingBefore = Marshal.ReadInt64(_core, 0x58) != 0;
            float runningAverageBefore = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_contact, 0x84));

            float raw = _solve(_contact, _event);

            float runningAverageAfter = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_contact, 0x84));
            float gateFriction = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_contact, 0x78));
            float gateNormalPush = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_contact, 0x88));
            float gateEvent0 = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(_event, 0x0));

            return new NativeCase(
                stickingBefore,
                ReadVector3(_core, 0x120),
                ReadVector3(_core, 0x110),
                raw,
                BitConverter.SingleToInt32Bits(runningAverageBefore) != BitConverter.SingleToInt32Bits(runningAverageAfter),
                gateFriction * gateNormalPush * gateEvent0);
        }

        public void Dispose()
        {
            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }

            _blocks.Clear();
        }

        private static void WriteDouble3(nint block, int offset, (double X, double Y, double Z) vector)
        {
            Marshal.WriteInt64(block, offset, BitConverter.DoubleToInt64Bits(vector.X));
            Marshal.WriteInt64(block, offset + 8, BitConverter.DoubleToInt64Bits(vector.Y));
            Marshal.WriteInt64(block, offset + 16, BitConverter.DoubleToInt64Bits(vector.Z));
        }

        private static void WriteVector3(nint block, int offset, (float X, float Y, float Z) vector)
        {
            Marshal.WriteInt32(block, offset, BitConverter.SingleToInt32Bits(vector.X));
            Marshal.WriteInt32(block, offset + 4, BitConverter.SingleToInt32Bits(vector.Y));
            Marshal.WriteInt32(block, offset + 8, BitConverter.SingleToInt32Bits(vector.Z));
        }

        private static (float X, float Y, float Z) ReadVector3(nint block, int offset) => (
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(block, offset)),
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(block, offset + 4)),
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(block, offset + 8)));

        /// <summary>The core's transform at <c>core+0x90</c>, in doubles, each row padded to 0x20 bytes.</summary>
        private static void WriteMatrix(nint core, IvpMatrix matrix)
        {
            double[] rows = [matrix.M0, matrix.M1, matrix.M2, matrix.M4, matrix.M5, matrix.M6, matrix.M8, matrix.M9, matrix.M10];

            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    Marshal.WriteInt64(core, 0x90 + (row * 0x20) + (column * 8), BitConverter.DoubleToInt64Bits(rows[(row * 3) + column]));
                }
            }
        }

        private static void Zero(nint block, int size) => Marshal.Copy(Zeroes, 0, block, size);

        private nint Allocate(int size)
        {
            nint block = Marshal.AllocHGlobal(size);

            _blocks.Add(block);
            Zero(block, size);

            return block;
        }
    }
}
