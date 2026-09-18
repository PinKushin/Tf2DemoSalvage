using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Drives the shipped <c>vphysics.dll</c>'s real <c>IPhysicsCollision::CreateVirtualMesh</c> (the actual API a BSP compile
/// calls for a displacement, <c>utils/vbsp/disp_ivp.cpp</c>'s <c>Disp_BuildVirtualMesh</c>) with the SAME geometry
/// <c>IvpSimulationBroadPhaseTests.Advance_ABodyDroppedOnVirtualTerrain_ComesToRestOnIt</c> (<c>tests/Tf2DemoSalvage.Animation.Tests</c>)
/// builds, drops the same half-cube on it, and reports whether the real engine rests or falls through (B369, D172).
/// </summary>
/// <remarks>
/// <para>
/// **This is the "run it through the real DLL" experiment `docs/HANDOFF.md` names as the one thing left that is not more
/// source reading.** Every other candidate in that file's virtual-terrain section was checked and individually confirmed
/// correct against the disassembly; the only way to tell a real port divergence from a genuine IVP quirk this fixture
/// triggers is to ask the shipped binary the same question.
/// </para>
/// <para>
/// **The real API, not a substitute.** <c>public/vphysics/virtualmesh.h</c> declares exactly the callback boundary this
/// probe implements — <c>IVirtualMeshEvent::GetVirtualMesh</c>/<c>GetWorldspaceBounds</c>/<c>GetTrianglesInSphere</c> — and
/// <c>disp_ivp.cpp</c>'s own <c>CDispMeshEvent</c> is the reference implementation this probe's three delegates mirror
/// line for line (down to <c>GetTrianglesInSphere</c> ignoring its own center/radius and just returning every triangle,
/// exactly as the shipped compiler's own handler does). <c>IPhysicsCollision::CreateVirtualMesh</c> is slot 46 and
/// <c>SupportsVirtualMesh</c> is slot 47, counted the same way <c>VphysicsDropProbe</c>'s already-verified slots 29
/// (<c>BBoxToCollide</c>), 36 (<c>VCollideLoad</c>) and 37 (<c>VCollideUnload</c>) were, from
/// <c>public/vphysics_interface.h</c>'s declaration order — those three land exactly where the doc comment there says,
/// which is what cross-checks the counting method here too.
/// </para>
/// <para>
/// **The geometry is <see cref="DisplacementCollisionTree.Build"/> called with the test's own corners, power and field**
/// — not reimplemented — so this probe cannot disagree with the fixture about what shape it built. Only the SCALE differs
/// deliberately: the C# test's <c>IvpRigidBody</c> positions are already expressed in the metric IVP units
/// <c>PhysicsVirtualMesh.Build</c> converts the mesh INTO (its own doc: <c>x·0.0254f, −(z·0.0254f), y·0.0254f</c>), so this
/// probe divides every one of the test's IVP-frame numbers by that same 0.0254 to recover the genuine Source-inch values
/// a real map would use — the real engine's public object API does the 0.0254 conversion itself, internally, exactly as
/// production code does, so feeding it raw Source units is the faithful replay, not an approximation. The corners and
/// field distance need no such conversion: <see cref="DisplacementCollisionTree.Build"/> takes them in Source units
/// already, same as the test passes them.
/// </para>
/// </remarks>
public sealed class VphysicsVirtualTerrainDropProbe : IProbe
{
    private const float MetresPerInch = 0.0254f;

    // Same slot constants VphysicsDropProbe already verified against the disassembly.
    private const int CollisionBBoxToCollideSlot = 29;
    private const int CollisionCreateVirtualMeshSlot = 46;
    private const int CollisionSupportsVirtualMeshSlot = 47;
    private const int SurfacePropsParseSurfaceDataSlot = 1;
    private const int SurfacePropsGetSurfaceIndexSlot = 3;
    private const int EnvironmentSetGravitySlot = 3;
    private const int EnvironmentCreatePolyObjectSlot = 7;
    private const int EnvironmentCreatePolyObjectStaticSlot = 8;
    private const int EnvironmentSimulateSlot = 34;
    private const int EnvironmentSetSimulationTimestepSlot = 37;
    private const int ObjectEnableMotionSlot = 15;
    private const int ObjectWakeSlot = 24;
    private const int ObjectGetPositionSlot = 47;
    private const int ObjectGetVelocitySlot = 51;

    // Matches IvpSimulationBroadPhaseTests' own Environment() InverseStep (66), so a tick-by-tick diff against the port
    // compares the same PSI rate rather than the engine's default 100Hz against the port's 66Hz (B369).
    private const int TotalTicks = 198;
    private const int PrintEveryTicks = 13;
    private const float Timestep = 1f / 66f;

    /// <summary>The cap the runtime handler hands its tree walk — <c>0xc00</c>, <c>MAX_VIRTUAL_TRIANGLES·3</c>.</summary>
    private const int TriangleIndexCap = 0xc00;

    /// <summary>The basin's 2000-inch square, matching the test's own corners.</summary>
    private const float BasinSize = 2000f;

    /// <summary>The border ring's raise, matching the test's own field distance.</summary>
    private const float BorderHeight = 100f;

    /// <summary>The test's own <c>Half</c> (4), read back out of its metric IVP scale into Source inches.</summary>
    private const float HalfInches = 4f / MetresPerInch;

    /// <summary>The test's own drop height (10, IVP <c>-Y</c>), read back into Source inches of altitude above the flat middle.</summary>
    private const float DropAltitudeInches = 10f / MetresPerInch;

    /// <summary>The test's own gravity magnitude (10), read back into Source inches/s².</summary>
    private const float GravityInches = 10f / MetresPerInch;

    /// <summary>The test's drop X/Z (25.4 IVP, the basin's exact metric centre), read back into Source inches — 1000 exactly.</summary>
    private const float CentreInches = 25.4f / MetresPerInch;

    private const string SurfaceText = """
        "default"
        {
        "friction"      "0.8"
        "elasticity"    "0.25"
        "density"       "2700"
        "thickness"     "-1"
        "dampening"     "0"
        }
        "frictionless"
        {
        "friction"      "0"
        "elasticity"    "0"
        "density"       "2700"
        "thickness"     "-1"
        "dampening"     "0"
        }
        """;

    /// <summary><c>IPhysicsObject::SetVelocity</c>, counted as the other object slots are.</summary>
    private const int ObjectSetVelocitySlot = 49;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetVelocityDelegate(nint physicsObject, in Vec3 velocity, in Vec3 angularVelocity);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Vec3(float x, float y, float z)
    {
        public readonly float X = x;
        public readonly float Y = y;
        public readonly float Z = z;

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:F2}, {Y:F2}, {Z:F2})");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectParams
    {
        public nint MassCenterOverride;
        public float Mass;
        public float Inertia;
        public float Damping;
        public float RotDamping;
        public float RotInertiaLimit;
        public nint Name;
        public nint GameData;
        public float Volume;
        public float DragCoefficient;

        [MarshalAs(UnmanagedType.I1)]
        public bool EnableCollisions;

        // Damping/RotDamping/DragCoefficient are 0 here, not g_PhysDefaultObjectParams' usual 0.1/0.1/1.0 (physics_shared.cpp:43-56):
        // the C# port's synthetic IvpRigidBody has no velocity damping at all (HANDOFF traced its fall as constant +0.1515/step
        // acceleration, undamped, right up to its one bounce), so any nonzero damping here would compare the real engine's
        // drag against the port's lack of it rather than comparing the collision mechanism under test.
        public static ObjectParams Default(float mass, nint name) => new()
        {
            Mass = mass,
            Inertia = 1.0f,
            RotInertiaLimit = 0.05f,
            Name = name,
            EnableCollisions = true,
        };
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateInterfaceDelegate([MarshalAs(UnmanagedType.LPStr)] string name, out int returnCode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateEnvironmentDelegate(nint physics);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint BBoxToCollideDelegate(nint collision, in Vec3 mins, in Vec3 maxs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateVirtualMeshDelegate(nint collision, nint paramsPtr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool SupportsVirtualMeshDelegate(nint collision);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ParseSurfaceDataDelegate(
        nint surfaceProps, [MarshalAs(UnmanagedType.LPStr)] string filename, [MarshalAs(UnmanagedType.LPStr)] string text);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetSurfaceIndexDelegate(nint surfaceProps, [MarshalAs(UnmanagedType.LPStr)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetGravityDelegate(nint environment, in Vec3 gravity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetSimulationTimestepDelegate(nint environment, float timestep);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreatePolyObjectDelegate(
        nint environment, nint collide, int materialIndex, in Vec3 position, in Vec3 angles, ref ObjectParams objectParams);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SimulateDelegate(nint environment, float deltaTime);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EnableMotionDelegate(nint physicsObject, [MarshalAs(UnmanagedType.I1)] bool enable);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WakeDelegate(nint physicsObject);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetPositionDelegate(nint physicsObject, out Vec3 position, out Vec3 angles);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetVelocityDelegate(nint physicsObject, out Vec3 velocity, out Vec3 angularVelocity);

    // IVirtualMeshEvent's own three slots, public/vphysics/virtualmesh.h — no destructor, so slot 0 is the first method.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetVirtualMeshDelegate(nint self, nint userData, nint pList);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetWorldspaceBoundsDelegate(nint self, nint userData, nint pMins, nint pMaxs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetTrianglesInSphereDelegate(nint self, nint userData, nint center, float radius, nint pList);

    private readonly List<Delegate> _callbacks = [];
    private readonly List<nint> _blocks = [];

    /// <inheritdoc />
    public string Name => "vphysics-virtual-terrain-drop";

    /// <inheritdoc />
    public string Summary =>
        "drives the shipped vphysics.dll's real IPhysicsCollision::CreateVirtualMesh with the broad-phase virtual-terrain " +
        "test's own displacement geometry, drops the same half-cube, and reports whether the real engine rests at the " +
        "test's expected height or falls through; 'boxes' drives the broad-phase test's two cubes together instead: " +
        "vphysics-virtual-terrain-drop [boxes]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        output.WriteLine($"pid={Environment.ProcessId}");

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        // Paused here, not before TryLoad: a breakpoint needs vphysics.dll already mapped into this
        // process before its addresses mean anything to an attaching debugger.
        SpinWaitForDebuggerAttach(output);

        nint createInterfaceExport = NativeLibrary.GetExport(module, "CreateInterface");
        CreateInterfaceDelegate createInterface =
            Marshal.GetDelegateForFunctionPointer<CreateInterfaceDelegate>(createInterfaceExport);

        if (!TryCreate(output, createInterface, "VPhysics031", out nint physics) ||
            !TryCreate(output, createInterface, "VPhysicsCollision007", out nint collision) ||
            !TryCreate(output, createInterface, "VPhysicsSurfaceProps001", out nint surfaceProps))
        {
            return;
        }

        output.WriteLine($"IPhysics={physics:x} IPhysicsCollision={collision:x} IPhysicsSurfaceProps={surfaceProps:x}");

        int parsed = VCall<ParseSurfaceDataDelegate>(surfaceProps, SurfacePropsParseSurfaceDataSlot)(
            surfaceProps, "probe.txt", SurfaceText);
        int materialIndex = VCall<GetSurfaceIndexDelegate>(surfaceProps, SurfacePropsGetSurfaceIndexSlot)(
            surfaceProps, "default");
        output.WriteLine($"ParseSurfaceData -> {parsed} entries; GetSurfaceIndex(\"default\") -> {materialIndex}");

        if (materialIndex < 0)
        {
            output.WriteLine("No 'default' surface parsed; aborting.");
            return;
        }

        if (arguments.Count > 0 && arguments[0] == "boxes")
        {
            RunBoxes(output, module, physics, collision, VCall<GetSurfaceIndexDelegate>(surfaceProps, SurfacePropsGetSurfaceIndexSlot)(
                surfaceProps, "frictionless"));
            return;
        }

        bool supportsVirtualMesh =VCall<SupportsVirtualMeshDelegate>(collision, CollisionSupportsVirtualMeshSlot)(collision);
        output.WriteLine($"control: SupportsVirtualMesh() -> {supportsVirtualMesh}");

        if (!supportsVirtualMesh)
        {
            output.WriteLine("This build's vphysics.dll does not support virtual meshes; the experiment cannot run.");
            return;
        }

        // The test's own corners and field, unconverted — DisplacementCollisionTree takes Source units already.
        Vector3[] corners = [Vector3.Zero, new Vector3(0f, BasinSize, 0f), new Vector3(BasinSize, BasinSize, 0f), new Vector3(BasinSize, 0f, 0f)];
        (Vector3 Direction, float Distance)[] field = new (Vector3, float)[25];

        for (int index = 0; index < 25; index++)
        {
            if (index / 5 is 0 or 4 || index % 5 is 0 or 4)
            {
                field[index] = (Vector3.UnitZ, BorderHeight);
            }
        }

        DisplacementCollisionTree tree = DisplacementCollisionTree.Build(corners, 2, field);
        output.WriteLine($"DisplacementCollisionTree: {tree.Vertices.Count} vertices, {tree.Triangles.Count} triangles");

        nint environment = VCall<CreateEnvironmentDelegate>(physics, 5)(physics);
        output.WriteLine($"IPhysicsEnvironment={environment:x}");

        Vec3 gravity = new(0f, 0f, -GravityInches);
        VCall<SetGravityDelegate>(environment, EnvironmentSetGravitySlot)(environment, gravity);
        VCall<SetSimulationTimestepDelegate>(environment, EnvironmentSetSimulationTimestepSlot)(environment, Timestep);
        output.WriteLine($"gravity={gravity} (test magnitude 10, /{MetresPerInch} inches/s^2), timestep={Timestep:F3}");

        (nint handler, nint meshVerts) = BuildMeshEventHandler(tree);

        try
        {
            nint meshParams = Block(24);
            Marshal.WriteIntPtr(meshParams, 0, handler); // pMeshEventHandler
            Marshal.WriteIntPtr(meshParams, 8, handler); // userData, matching CDispMeshEvent's Assert(userData==this)
            Marshal.WriteByte(meshParams, 16, 1); // buildOuterHull = true; pHull stays NULL, same as the real compiler's own usage

            nint groundCollide = VCall<CreateVirtualMeshDelegate>(collision, CollisionCreateVirtualMeshSlot)(collision, meshParams);
            output.WriteLine($"CreateVirtualMesh -> {groundCollide:x}");

            if (groundCollide == 0)
            {
                output.WriteLine("CreateVirtualMesh returned null; the real engine refused this geometry. Aborting.");
                return;
            }

            nint groundName = Marshal.StringToHGlobalAnsi("ground");
            nint bodyName = Marshal.StringToHGlobalAnsi("body");

            try
            {
                ObjectParams groundParams = ObjectParams.Default(mass: 0f, groundName);
                VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectStaticSlot)(
                    environment, groundCollide, materialIndex, new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f), ref groundParams);

                nint bodyCollide = VCall<BBoxToCollideDelegate>(collision, CollisionBBoxToCollideSlot)(
                    collision, new Vec3(-HalfInches, -HalfInches, -HalfInches), new Vec3(HalfInches, HalfInches, HalfInches));

                Vec3 start = new(CentreInches, CentreInches, DropAltitudeInches);
                ObjectParams bodyParams = ObjectParams.Default(mass: 10f, bodyName);
                nint bodyObject = VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectSlot)(
                    environment, bodyCollide, materialIndex, start, new Vec3(0f, 0f, 0f), ref bodyParams);

                output.WriteLine(
                    $"ground at (0,0,0), half-extent {HalfInches:F3} in, dropped from {start}, expecting rest at Z~{HalfInches:F3}");

                VCall<EnableMotionDelegate>(bodyObject, ObjectEnableMotionSlot)(bodyObject, true);
                VCall<WakeDelegate>(bodyObject, ObjectWakeSlot)(bodyObject);

                SimulateDelegate simulate = VCall<SimulateDelegate>(environment, EnvironmentSimulateSlot);
                GetPositionDelegate getPosition = VCall<GetPositionDelegate>(bodyObject, ObjectGetPositionSlot);
                GetVelocityDelegate getVelocity = VCall<GetVelocityDelegate>(bodyObject, ObjectGetVelocitySlot);

                Vec3 lastPosition = start;
                using ImpactTrace? impacts = ImpactTrace.FromEnvironment(module, output);

                for (int tick = 1; tick <= TotalTicks; tick++)
                {
                    impacts?.AtTick(tick);
                    simulate(environment, Timestep);
                    getPosition(bodyObject, out lastPosition, out _);

                    if (tick % PrintEveryTicks == 0)
                    {
                        getVelocity(bodyObject, out Vec3 velocity, out Vec3 angularVelocity);
                        output.WriteLine(
                            $"tick {tick,4} t={tick * Timestep,5:F2}  pos={lastPosition}  vel={velocity}  spin={angularVelocity}");
                    }
                }

                float restDistance = MathF.Abs(lastPosition.Z - HalfInches);
                output.WriteLine(
                    $"final: pos={lastPosition}, expected rest Z={HalfInches:F3} (+/- half a metre = {0.5f / MetresPerInch:F3} in), " +
                    $"|difference|={restDistance:F3} in");
                output.WriteLine(
                    restDistance <= 0.5f / MetresPerInch
                        ? "REAL ENGINE RESTS at the expected height."
                        : "REAL ENGINE DOES NOT REST at the expected height (fell through or stopped elsewhere).");
            }
            finally
            {
                Marshal.FreeHGlobal(groundName);
                Marshal.FreeHGlobal(bodyName);
            }
        }
        finally
        {
            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }

            Marshal.FreeHGlobal(meshVerts);
        }
    }

    /// <summary>
    /// <c>boxes</c>: <c>IvpSimulationBroadPhaseTests</c>' two cubes driven together — half 4, one at IVP (2, 1, 0) moving at 6 along
    /// IVP +z, one still at (0, 0, 9), no gravity, a frictionless inelastic surface — through the real engine.
    /// </summary>
    /// <remarks>IVP to Source is <c>(x, z, −y)</c> over <see cref="MetresPerInch"/>.</remarks>
    private static void RunBoxes(TextWriter output, nint module, nint physics, nint collision, int materialIndex)
    {
        nint environment = VCall<CreateEnvironmentDelegate>(physics, 5)(physics);
        VCall<SetGravityDelegate>(environment, EnvironmentSetGravitySlot)(environment, new Vec3(0f, 0f, 0f));
        VCall<SetSimulationTimestepDelegate>(environment, EnvironmentSetSimulationTimestepSlot)(environment, Timestep);

        nint box = VCall<BBoxToCollideDelegate>(collision, CollisionBBoxToCollideSlot)(
            collision, new Vec3(-HalfInches, -HalfInches, -HalfInches), new Vec3(HalfInches, HalfInches, HalfInches));
        nint movingName = Marshal.StringToHGlobalAnsi("moving");
        nint stillName = Marshal.StringToHGlobalAnsi("still");

        using ImpactTrace? impacts = ImpactTrace.FromEnvironment(module, output);

        try
        {
            ObjectParams movingParams = ObjectParams.Default(mass: 1f, movingName);
            ObjectParams stillParams = ObjectParams.Default(mass: 1f, stillName);
            nint moving = VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectSlot)(
                environment, box, materialIndex, new Vec3(2f / MetresPerInch, 0f, -1f / MetresPerInch), new Vec3(0f, 0f, 0f), ref movingParams);
            nint still = VCall<CreatePolyObjectDelegate>(environment, EnvironmentCreatePolyObjectSlot)(
                environment, box, materialIndex, new Vec3(0f, 9f / MetresPerInch, 0f), new Vec3(0f, 0f, 0f), ref stillParams);

            foreach (nint body in (ReadOnlySpan<nint>)[moving, still])
            {
                VCall<EnableMotionDelegate>(body, ObjectEnableMotionSlot)(body, true);
                VCall<WakeDelegate>(body, ObjectWakeSlot)(body);
            }

            VCall<SetVelocityDelegate>(moving, ObjectSetVelocitySlot)(moving, new Vec3(0f, 6f / MetresPerInch, 0f), new Vec3(0f, 0f, 0f));

            SimulateDelegate simulate = VCall<SimulateDelegate>(environment, EnvironmentSimulateSlot);

            for (int tick = 1; tick <= 66; tick++)
            {
                impacts?.AtTick(tick);
                simulate(environment, Timestep);
                VCall<GetPositionDelegate>(moving, ObjectGetPositionSlot)(moving, out Vec3 movingAt, out Vec3 movingAngles);
                VCall<GetPositionDelegate>(still, ObjectGetPositionSlot)(still, out Vec3 stillAt, out Vec3 stillAngles);
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"tick {tick,3} moving IVP z={movingAt.Y * MetresPerInch:F4} angles={movingAngles} still IVP z={stillAt.Y * MetresPerInch:F4} " +
                    $"angles={stillAngles} apart={(stillAt.Y - movingAt.Y) * MetresPerInch:F4}"));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(movingName);
            Marshal.FreeHGlobal(stillName);
        }
    }

    /// <summary>
    /// Builds the fabricated <c>IVirtualMeshEvent</c> object <c>CreateVirtualMesh</c> calls back into — a manual vtable of
    /// three delegates, each mirroring <c>CDispMeshEvent</c>'s own implementation (<c>utils/vbsp/disp_ivp.cpp</c>) exactly,
    /// including <c>GetTrianglesInSphere</c> ignoring its own center/radius and returning every triangle every time, same
    /// as the shipped compiler's own handler does.
    /// </summary>
    /// <returns>The handler object's address, and the persistent vertex buffer's address (freed by the caller).</returns>
    private (nint Handler, nint Verts) BuildMeshEventHandler(DisplacementCollisionTree tree)
    {
        int vertexCount = tree.Vertices.Count;
        nint verts = Marshal.AllocHGlobal(vertexCount * 12);

        for (int index = 0; index < vertexCount; index++)
        {
            Vector3 vertex = tree.Vertices[index];
            Marshal.WriteInt32(verts, (index * 12) + 0, BitConverter.SingleToInt32Bits(vertex.X));
            Marshal.WriteInt32(verts, (index * 12) + 4, BitConverter.SingleToInt32Bits(vertex.Y));
            Marshal.WriteInt32(verts, (index * 12) + 8, BitConverter.SingleToInt32Bits(vertex.Z));
        }

        ushort[] indices = new ushort[tree.Triangles.Count * 3];

        for (int index = 0; index < tree.Triangles.Count; index++)
        {
            (int a, int b, int c) = tree.Triangles[index];
            indices[(index * 3) + 0] = (ushort)a;
            indices[(index * 3) + 1] = (ushort)b;
            indices[(index * 3) + 2] = (ushort)c;
        }

        Vector3 mins = new(float.MaxValue);
        Vector3 maxs = new(float.MinValue);

        foreach (Vector3 vertex in tree.Vertices)
        {
            mins = Vector3.Min(mins, vertex);
            maxs = Vector3.Max(maxs, vertex);
        }

        // virtualmeshlist_t (public/vphysics/virtualmesh.h): pVerts(0) indexCount(8) triangleCount(0xC) vertexCount(0x10)
        // surfacePropsIndex(0x14) pHull(0x18) indices[](0x20) — matching CDispMeshEvent::GetVirtualMesh exactly.
        GetVirtualMeshDelegate getVirtualMesh = Keep(new GetVirtualMeshDelegate((_, _, pList) =>
        {
            Marshal.WriteIntPtr(pList, 0x00, verts);
            Marshal.WriteInt32(pList, 0x08, indices.Length);
            Marshal.WriteInt32(pList, 0x0C, indices.Length / 3);
            Marshal.WriteInt32(pList, 0x10, vertexCount);
            Marshal.WriteInt32(pList, 0x14, 0);
            Marshal.WriteIntPtr(pList, 0x18, 0);

            for (int index = 0; index < indices.Length; index++)
            {
                Marshal.WriteInt16(pList, 0x20 + (index * 2), unchecked((short)indices[index]));
            }
        }));

        GetWorldspaceBoundsDelegate getBounds = Keep(new GetWorldspaceBoundsDelegate((_, _, pMins, pMaxs) =>
        {
            Marshal.WriteInt32(pMins, 0, BitConverter.SingleToInt32Bits(mins.X));
            Marshal.WriteInt32(pMins, 4, BitConverter.SingleToInt32Bits(mins.Y));
            Marshal.WriteInt32(pMins, 8, BitConverter.SingleToInt32Bits(mins.Z));
            Marshal.WriteInt32(pMaxs, 0, BitConverter.SingleToInt32Bits(maxs.X));
            Marshal.WriteInt32(pMaxs, 4, BitConverter.SingleToInt32Bits(maxs.Y));
            Marshal.WriteInt32(pMaxs, 8, BitConverter.SingleToInt32Bits(maxs.Z));
        }));

        // virtualmeshtrianglelist_t: triangleCount(0) triangleIndices[](4) — TRIANGLE indices, as vphysics' FUN_180025bc0 reads
        // them. Answered as the game's runtime handler does (engine.dll FUN_18017cb00: AABBTree_BuildTreeTrisInSphere_r, cap
        // 0xc00), not as vbsp's CDispMeshEvent does: that one copies VERTEX indices with a count of indices/3, which vphysics
        // reads as a scrambled, duplicated triangle list that never offers the last triangles at all.
        GetTrianglesInSphereDelegate getTriangles = Keep(new GetTrianglesInSphereDelegate((_, _, center, radius, pList) =>
        {
            Vector3 at = new(
                BitConverter.Int32BitsToSingle(Marshal.ReadInt32(center, 0)),
                BitConverter.Int32BitsToSingle(Marshal.ReadInt32(center, 4)),
                BitConverter.Int32BitsToSingle(Marshal.ReadInt32(center, 8)));
            IReadOnlyList<int> found = tree.TrianglesInSphere(at, radius, TriangleIndexCap);

            Marshal.WriteInt32(pList, 0, found.Count);

            for (int index = 0; index < found.Count; index++)
            {
                Marshal.WriteInt16(pList, 4 + (index * 2), unchecked((short)found[index]));
            }
        }));

        nint vtable = Block(3 * nint.Size);
        Marshal.WriteIntPtr(vtable, 0 * nint.Size, Marshal.GetFunctionPointerForDelegate(getVirtualMesh));
        Marshal.WriteIntPtr(vtable, 1 * nint.Size, Marshal.GetFunctionPointerForDelegate(getBounds));
        Marshal.WriteIntPtr(vtable, 2 * nint.Size, Marshal.GetFunctionPointerForDelegate(getTriangles));

        nint handler = Block(nint.Size);
        Marshal.WriteIntPtr(handler, 0, vtable);

        return (handler, verts);
    }

    /// <summary>
    /// Opt-in debugger-attach window: a live trace of this probe races the run against a manual/scripted
    /// <c>debugger_attach</c>, and the whole 300-tick run otherwise completes faster than an attach can win that race
    /// (measured, `docs/HANDOFF.md`'s virtual-terrain section). Set <c>TF2VPHYSICS_PROBE_PAUSE_MS</c> to spin-wait right
    /// here, before any native call, buying a real attach window — but polling <c>TF2VPHYSICS_PROBE_GO_FILE</c> every
    /// iteration and returning the instant it appears, so the wait ends the moment whatever is attaching says it is
    /// actually ready rather than after a guessed duration. The env var's value is only the upper bound if that file
    /// never shows up (a debugger that fails to attach, or a typo'd path). A plain <c>Stopwatch</c> busy-wait, not
    /// <c>Thread.Sleep</c>/<c>Task.Delay</c> (banned in this repo, `~/.claude/hooks/block-banned-csharp.ps1`). No-op
    /// unless the env var is set; never active in normal runs.
    /// </summary>
    private static void SpinWaitForDebuggerAttach(TextWriter output)
    {
        string? raw = Environment.GetEnvironmentVariable("TF2VPHYSICS_PROBE_PAUSE_MS");

        if (string.IsNullOrEmpty(raw) || !int.TryParse(raw, out int pauseMs) || pauseMs <= 0)
        {
            return;
        }

        string? goFile = Environment.GetEnvironmentVariable("TF2VPHYSICS_PROBE_GO_FILE");

        output.WriteLine(
            string.IsNullOrEmpty(goFile)
                ? $"TF2VPHYSICS_PROBE_PAUSE_MS={pauseMs}: spin-waiting for debugger attach (fixed duration, no go-file set)..."
                : $"TF2VPHYSICS_PROBE_PAUSE_MS={pauseMs} (upper bound), watching for {goFile} to continue early...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < pauseMs)
        {
            if (!string.IsNullOrEmpty(goFile) && File.Exists(goFile))
            {
                output.WriteLine($"{goFile} appeared after {stopwatch.ElapsedMilliseconds}ms, continuing.");
                return;
            }

            // Busy-wait on purpose: Thread.Sleep/Task.Delay are banned in this repo.
        }

        output.WriteLine("Pause elapsed, continuing.");
    }

    private T Keep<T>(T callback)
        where T : Delegate
    {
        _callbacks.Add(callback);
        return callback;
    }

    private nint Block(int size)
    {
        nint block = Marshal.AllocHGlobal(size);

        for (int offset = 0; offset < size; offset++)
        {
            Marshal.WriteByte(block, offset, 0);
        }

        _blocks.Add(block);
        return block;
    }

    private static bool TryCreate(TextWriter output, CreateInterfaceDelegate createInterface, string version, out nint result)
    {
        result = createInterface(version, out int returnCode);

        if (result != 0 && returnCode == 0)
        {
            return true;
        }

        output.WriteLine($"CreateInterface(\"{version}\") failed: returnCode={returnCode}, pointer={result:x}");
        return false;
    }

    private static T VCall<T>(nint instance, int slot)
        where T : Delegate
    {
        nint vtable = Marshal.ReadIntPtr(instance);
        nint function = Marshal.ReadIntPtr(vtable, slot * nint.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(function);
    }

    /// <summary>
    /// Opt-in (<c>TF2VPHYSICS_PROBE_TRACE_IMPACTS=1</c>): every impact the real engine solves, as its impact entry
    /// <c>FUN_18008ed60(record, cores, pushOut, cp)</c> is handed it — the record's normal (<c>+0x20</c>), its two arms
    /// (<c>+0xd0</c>, <c>+0xe0</c>) and which of its cores (<c>+0x98</c>, <c>+0xa0</c>) are movable — so the contact the engine
    /// resolves can be compared point for point with the port's.
    /// </summary>
    /// <remarks>
    /// The entry's first twelve bytes are whole instructions (`MOV RAX,RSP`, three `MOV [RAX+n]`, `PUSH RBP`), so the detour is put
    /// back for each call, the routine called through, and the detour laid again: the simulation is single-threaded.
    /// </remarks>
    private sealed class ImpactTrace : IDisposable
    {
        private const long EntryAddress = 0x18008ed60;

        /// <summary><c>IvpMindist::Collide</c>, <c>this</c> the mindist.</summary>
        private const long CollideAddress = 0x18008ecb0;

        /// <summary>The min-list add, <c>FUN_1800aaed0(list, element, value)</c>; its first twelve bytes end mid-instruction, which
        /// is harmless because the hook is lifted before anything runs them.</summary>
        private const long QueueAddAddress = 0x1800aaed0;

        /// <summary><c>IvpMindist::Attach(mindist, object0, ledge0, object1, ledge1)</c>: a mindist's objects and ledges written.</summary>
        private const long AttachAddress = 0x1800975d0;

        /// <summary><c>IvpMindistHull::HullPassed(mindist, overshoot)</c>: a far pair told its hull passed.</summary>
        private const long HullPassedAddress = 0x180097f00;

        /// <summary><c>IvpMindistMinimize::Minimize(mindist)</c>: the result, 1 settled to 3 backside.</summary>
        private const long MinimizeAddress = 0x180095cb0;

        /// <summary><c>IvpMindistMinimize::BacksideWalk(feature, point)</c>: the edge the walk stops in.</summary>
        private const long BacksideWalkAddress = 0x180094e30;

        private readonly Action<string> _write;
        private readonly Hook<MinimizeDelegate> _minimize;
        private readonly Hook<BacksideWalkDelegate> _backsideWalk;
        private readonly Hook<ImpactEntryDelegate> _entry;
        private readonly Hook<CollideDelegate> _collide;
        private readonly Hook<QueueAddDelegate> _queueAdd;
        private readonly Hook<AttachDelegate> _attach;
        private readonly Hook<HullPassedDelegate> _hullPassed;
        private readonly (int First, int Last) _queueTicks;
        private int _tick;
        private bool _dumped;

        private ImpactTrace(nint module, TextWriter output, (int First, int Last) queueTicks)
        {
            _write = output.WriteLine;
            _queueTicks = queueTicks;
            _entry = new Hook<ImpactEntryDelegate>(module, EntryAddress, Entered);
            _collide = new Hook<CollideDelegate>(module, CollideAddress, Collided);
            _queueAdd = new Hook<QueueAddDelegate>(module, QueueAddAddress, Queued);
            _attach = new Hook<AttachDelegate>(module, AttachAddress, Attached);
            _hullPassed = new Hook<HullPassedDelegate>(module, HullPassedAddress, Passed);
            _minimize = new Hook<MinimizeDelegate>(module, MinimizeAddress, Minimized);
            _backsideWalk = new Hook<BacksideWalkDelegate>(module, BacksideWalkAddress, Walked);
            _weights = new Hook<TriangleWeightsDelegate>(module, TriangleWeightsAddress, Weighed);
            _examine = new Hook<ExamineDelegate>(module, ExamineAddress, Examined);
        }

        /// <summary><c>IvpPairScheduler::Examine(mindist, removeFar, recheck)</c>, <c>FUN_180099380</c>.</summary>
        private const long ExamineAddress = 0x180099380;

        private readonly Hook<ExamineDelegate> _examine;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ExamineDelegate(nint mindist, int removeFar, int recheck);

        private void Examined(nint mindist, int removeFar, int recheck)
        {
            nint environment = Marshal.ReadIntPtr(Marshal.ReadIntPtr(mindist, 0x48), 0x30);
            int before = Marshal.ReadInt32(mindist, 0x20);
            int looksBefore = Marshal.ReadInt32(environment, 0x13c);
            _examine.CallThrough(original => original(mindist, removeFar, recheck));

            if (_tick <= _queueTicks.Last)
            {
                _write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"EXAMINE tick {_tick} mindist={mindist:x} removeFar={removeFar} recheck={recheck} flags 0x{before:x8}->0x{Marshal.ReadInt32(mindist, 0x20):x8} " +
                    $"looks {looksBefore}->{Marshal.ReadInt32(environment, 0x13c)} len={BitConverter.Int32BitsToSingle(Marshal.ReadInt32(mindist, 0xa8))} " +
                    $"state0={Marshal.ReadByte(Marshal.ReadIntPtr(mindist, 0x48), 0x78) & 7} state1={Marshal.ReadByte(Marshal.ReadIntPtr(mindist, 0x80), 0x78) & 7} " +
                    $"past={BitConverter.Int64BitsToDouble(Marshal.ReadInt64(mindist, 0xa0)):R}"));
            }
        }

        /// <summary><c>IvpCompactLedgeSolver::TriangleWeights(ledge, edge, point, out)</c>.</summary>
        private const long TriangleWeightsAddress = 0x18007cdf0;

        private readonly Hook<TriangleWeightsDelegate> _weights;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TriangleWeightsDelegate(nint ledge, nint edge, nint point, nint weights);

        private void Weighed(nint ledge, nint edge, nint point, nint weights)
        {
            _weights.CallThrough(original => original(ledge, edge, point, weights));

            if (_tick >= _queueTicks.First && _tick <= _queueTicks.Last)
            {
                _write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"WEIGHTS tick {_tick} ledge={ledge:x} edge={edge:x} point=({BitConverter.Int64BitsToDouble(Marshal.ReadInt64(point, 0)):R}, " +
                    $"{BitConverter.Int64BitsToDouble(Marshal.ReadInt64(point, 8)):R}, {BitConverter.Int64BitsToDouble(Marshal.ReadInt64(point, 16)):R}) " +
                    $"out=({BitConverter.Int32BitsToSingle(Marshal.ReadInt32(weights, 0)):R}, {BitConverter.Int32BitsToSingle(Marshal.ReadInt32(weights, 4)):R}, " +
                    $"{BitConverter.Int32BitsToSingle(Marshal.ReadInt32(weights, 8)):R}, {BitConverter.Int32BitsToSingle(Marshal.ReadInt32(weights, 12)):R})"));
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ulong MinimizeDelegate(nint mindist);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint BacksideWalkDelegate(nint feature, nint point);

        private ulong Minimized(nint mindist)
        {
            ulong result = 0;
            _minimize.CallThrough(original => result = original(mindist));

            if (_tick <= _queueTicks.Last)
            {
                _write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"MINIMIZE tick {_tick} mindist={mindist:x} result={result} {Kinds(mindist)} len={BitConverter.Int32BitsToSingle(Marshal.ReadInt32(mindist, 0xa8)):R} " +
                    $"feature0={Marshal.ReadIntPtr(mindist, 0x50):x} feature1={Marshal.ReadIntPtr(mindist, 0x88):x}"));
            }

            return result;
        }

        private nint Walked(nint feature, nint point)
        {
            nint stopped = 0;
            _backsideWalk.CallThrough(original => stopped = original(feature, point));
            _write(string.Create(
                CultureInfo.InvariantCulture,
                $"WALK tick {_tick} feature={feature:x} stopped={stopped:x} point=({BitConverter.Int64BitsToDouble(Marshal.ReadInt64(point, 0)):R}, " +
                $"{BitConverter.Int64BitsToDouble(Marshal.ReadInt64(point, 8)):R}, {BitConverter.Int64BitsToDouble(Marshal.ReadInt64(point, 16)):R})"));
            return stopped;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AttachDelegate(nint mindist, nint object0, nint ledge0, nint object1, nint ledge1);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void HullPassedDelegate(nint mindist, float overshoot);

        private void Attached(nint mindist, nint object0, nint ledge0, nint object1, nint ledge1)
        {
            _write(string.Create(
                CultureInfo.InvariantCulture,
                $"ATTACH tick {_tick} mindist={mindist:x} object0={object0:x} ledge0={ledge0:x} object1={object1:x} ledge1={ledge1:x}"));
            if (!_dumped)
            {
                _dumped = true;
                nint ledge = (object1 & ~0xf) - 0x10;
                int triangles = Marshal.ReadInt16(ledge, 0xc);
                int points = 0;
                _write(string.Create(CultureInfo.InvariantCulture, $"LEDGE header={Marshal.ReadInt32(ledge, 0):x8} {Marshal.ReadInt32(ledge, 4):x8} {Marshal.ReadInt32(ledge, 8):x8} {Marshal.ReadInt32(ledge, 12):x8}"));

                for (int t = 0; t < triangles; t++)
                {
                    nint at = ledge + 0x10 + (t * 16);
                    _write(string.Create(CultureInfo.InvariantCulture, $"LEDGE tri {t} {Marshal.ReadInt32(at, 0):x8} {Marshal.ReadInt32(at, 4):x8} {Marshal.ReadInt32(at, 8):x8} {Marshal.ReadInt32(at, 12):x8}"));

                    for (int s = 1; s <= 3; s++)
                    {
                        points = Math.Max(points, (Marshal.ReadInt32(at, s * 4) & 0xffff) + 1);
                    }
                }

                nint pointArray = ledge + Marshal.ReadInt32(ledge, 0);

                for (int p = 0; p < points; p++)
                {
                    _write(string.Create(CultureInfo.InvariantCulture, $"LEDGE point {p} {Read(pointArray, p * 16)}"));
                }
            }

            _attach.CallThrough(original => original(mindist, object0, ledge0, object1, ledge1));
            _write(string.Create(CultureInfo.InvariantCulture, $"ATTACHED mindist={mindist:x} {Kinds(mindist)}"));
        }

        private void Passed(nint mindist, float overshoot)
        {
            if (_tick >= _queueTicks.First && _tick <= _queueTicks.Last)
            {
                _write(string.Create(CultureInfo.InvariantCulture, $"HULLPASSED tick {_tick} mindist={mindist:x} {Kinds(mindist)}"));
            }

            _hullPassed.CallThrough(original => original(mindist, overshoot));
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ImpactEntryDelegate(nint record, nint cores, float pushOut, nint point);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CollideDelegate(nint mindist);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int QueueAddDelegate(nint list, nint element, float value);

        /// <remarks><c>TF2VPHYSICS_PROBE_TRACE_QUEUE=first-last</c> also traces every min-list add in those ticks — every one, the
        /// hull managers' too, so keep the window narrow.</remarks>
        public static ImpactTrace? FromEnvironment(nint module, TextWriter output)
        {
            if (Environment.GetEnvironmentVariable("TF2VPHYSICS_PROBE_TRACE_IMPACTS") != "1")
            {
                return null;
            }

            string[] window = (Environment.GetEnvironmentVariable("TF2VPHYSICS_PROBE_TRACE_QUEUE") ?? "0-0").Split('-');

            return new ImpactTrace(
                module,
                output,
                (int.Parse(window[0], CultureInfo.InvariantCulture), int.Parse(window[1], CultureInfo.InvariantCulture)));
        }

        public void AtTick(int tick) => _tick = tick;

        public void Dispose()
        {
            _entry.Dispose();
            _collide.Dispose();
            _queueAdd.Dispose();
            _attach.Dispose();
            _hullPassed.Dispose();
            _minimize.Dispose();
            _backsideWalk.Dispose();
            _weights.Dispose();
            _examine.Dispose();
        }

        private void Entered(nint record, nint cores, float pushOut, nint point)
        {
            _write(string.Create(
                CultureInfo.InvariantCulture,
                $"IMPACT tick {_tick} record={record:x} point={point:x} normal={Read(record, 0x20)} arm0={Read(record, 0xd0)} " +
                $"arm1={Read(record, 0xe0)} core98={Marshal.ReadIntPtr(record, 0x98):x} coreA0={Marshal.ReadIntPtr(record, 0xa0):x} " +
                $"impacts={Marshal.ReadInt16(record, 0x72)}"));

            _entry.CallThrough(original => original(record, cores, pushOut, point));
        }

        private void Collided(nint mindist)
        {
            _write(string.Create(CultureInfo.InvariantCulture, $"COLLIDE tick {_tick} mindist={mindist:x} {Kinds(mindist)}"));
            _collide.CallThrough(original => original(mindist));
        }

        private int Queued(nint list, nint element, float value)
        {
            int slot = 0;
            _queueAdd.CallThrough(original => slot = original(list, element, value));

            if (_tick >= _queueTicks.First && _tick <= _queueTicks.Last)
            {
                _write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"QUEUE tick {_tick} list={list:x} element={element:x} key=0x{BitConverter.SingleToInt32Bits(value):x8} " +
                    $"slot={slot} head={Marshal.ReadInt32(list, 0x18)} {Kinds(element)}"));
            }

            return slot;
        }

        /// <summary>A mindist's two synapse kinds, <c>+0x5a</c> and <c>+0x92</c> (read as the words the recursive mindist tests).</summary>
        private static string Kinds(nint mindist) => string.Create(
            CultureInfo.InvariantCulture,
            $"kind0={Marshal.ReadInt16(mindist, 0x5a)} kind1={Marshal.ReadInt16(mindist, 0x92)} flags=0x{Marshal.ReadInt32(mindist, 0x20):x8}");

        private static Vec3 Read(nint at, int offset) => new(
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(at, offset)),
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(at, offset + 4)),
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(at, offset + 8)));
    }

    /// <summary>A detour a callback can call through: the routine's bytes put back, the routine called, the detour laid again.</summary>
    /// <remarks>Only sound single-threaded, which the probe's simulation is.</remarks>
    private sealed class Hook<T> : IDisposable
        where T : Delegate
    {
        private readonly nint _module;
        private readonly long _address;
        private readonly T _callback;
        private VphysicsDetour? _detour;

        public Hook(nint module, long address, T callback)
        {
            _module = module;
            _address = address;
            _callback = callback;
            _detour = new VphysicsDetour(module, address, callback);
        }

        public void CallThrough(Action<T> call)
        {
            _detour!.Dispose();
            _detour = null;

            try
            {
                call(Marshal.GetDelegateForFunctionPointer<T>(VphysicsLibrary.Address(_module, _address)));
            }
            finally
            {
                _detour = new VphysicsDetour(_module, _address, _callback);
            }
        }

        public void Dispose()
        {
            _detour?.Dispose();
            _detour = null;
        }
    }
}
