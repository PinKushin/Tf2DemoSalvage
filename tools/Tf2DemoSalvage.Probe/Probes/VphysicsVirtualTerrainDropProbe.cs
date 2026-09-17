using System;
using System.Collections.Generic;
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

    private const int TotalTicks = 300;
    private const int PrintEveryTicks = 20;
    private const float Timestep = 0.01f;

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
        """;

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
        "test's expected height or falls through: vphysics-virtual-terrain-drop";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

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

        bool supportsVirtualMesh = VCall<SupportsVirtualMeshDelegate>(collision, CollisionSupportsVirtualMeshSlot)(collision);
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

                for (int tick = 1; tick <= TotalTicks; tick++)
                {
                    simulate(environment, Timestep);
                    getPosition(bodyObject, out lastPosition, out _);

                    if (tick % PrintEveryTicks == 0)
                    {
                        getVelocity(bodyObject, out Vec3 velocity, out _);
                        output.WriteLine($"tick {tick,4} t={tick * Timestep,5:F2}  pos={lastPosition}  vel={velocity}");
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

        // virtualmeshtrianglelist_t: triangleCount(0) triangleIndices[](4).
        GetTrianglesInSphereDelegate getTriangles = Keep(new GetTrianglesInSphereDelegate((_, _, _, _, pList) =>
        {
            Marshal.WriteInt32(pList, 0, indices.Length / 3);

            for (int index = 0; index < indices.Length; index++)
            {
                Marshal.WriteInt16(pList, 4 + (index * 2), unchecked((short)indices[index]));
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
}
