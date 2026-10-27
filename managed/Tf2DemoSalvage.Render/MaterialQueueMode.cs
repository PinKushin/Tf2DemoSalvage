using System;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Render;

/// <summary>The material system's thread modes, in the engine's own order.</summary>
/// <remarks>`public/materialsystem/imaterialsystem.h:225-230`, `MaterialThreadMode_t`.</remarks>
public enum MaterialThreadMode
{
    /// <summary>`MATERIAL_SINGLE_THREADED`: every stage draws on the calling thread (today's serial path).</summary>
    SingleThreaded = 0,

    /// <summary>`MATERIAL_QUEUED_SINGLE_THREADED`: never used on PC (`EndFrame` maps 1 to 0), kept for the order.</summary>
    QueuedSingleThreaded = 1,

    /// <summary>`MATERIAL_QUEUED_THREADED`: the frame is drawn on the `MatQueue` thread, one frame in flight.</summary>
    QueuedThreaded = 2,
}

/// <summary>`mat_queue_mode`: its name, its help text and how the engine turns its value into a mode (B581, D228).</summary>
/// <remarks>
/// Read from TF2's x64 `materialsystem.dll` (finding 99 section 1). The cvar has no change callback; `EndFrame`
/// (`0x180014bc0`) re-reads it. `-2` → 2, `1` → 0, `0` and `2` pass through, and any other negative value — `-1`, the
/// engine's default string at `0x1800b69b4` — is the default `AllowThreading` (`0x180010f90`) stored: threaded on two or
/// more PHYSICAL processors (`CPUInformation::m_nPhysicalProcessors`), single-threaded below that.
/// **Not read:** what a positive value above 2 does; answered here as 0. The engine's other fall-backs to 0, against this
/// viewer: finding 98 section 10.
/// </remarks>
public static class MaterialQueueMode
{
    /// <summary>The cvar's name.</summary>
    public const string Command = "mat_queue_mode";

    /// <summary>The help text `materialsystem.dll` registers it with.</summary>
    public const string HelpText =
        "The queue/thread mode the material system should use: -1=default, 0=synchronous single thread, 2=queued multithreaded";

    /// <summary>The mode a value asks for, as `EndFrame` maps it.</summary>
    /// <param name="value">The cvar.</param>
    /// <param name="physicalCores">The machine's physical processor count.</param>
    /// <returns>The mode.</returns>
    public static MaterialThreadMode Resolve(int value, int physicalCores) => value switch
    {
        2 or -2 => MaterialThreadMode.QueuedThreaded,
        < 0 => physicalCores >= 2 ? MaterialThreadMode.QueuedThreaded : MaterialThreadMode.SingleThreaded,
        _ => MaterialThreadMode.SingleThreaded,
    };

    /// <summary>Physical processor cores, as `GetCPUInformation().m_nPhysicalProcessors` counts them.</summary>
    /// <returns>The count; the logical count when the system will not say.</returns>
    /// <remarks>`Environment.ProcessorCount` is LOGICAL processors, which is not what the engine tests.</remarks>
    public static int PhysicalCores()
    {
        // **The query is a Windows API**: the CI runners (Linux) construct a presenter too, and an unguarded P/Invoke
        // there is a DllNotFoundException in every test that does.
        if (!OperatingSystem.IsWindows())
        {
            return Environment.ProcessorCount;
        }

        uint length = 0;

        _ = GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);

        if (length == 0)
        {
            return Environment.ProcessorCount;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)length);

        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
            {
                return Environment.ProcessorCount;
            }

            // Each record starts { LOGICAL_PROCESSOR_RELATIONSHIP Relationship; DWORD Size; ... }: one per core.
            int cores = 0;

            for (int offset = 0; offset < length; offset += Marshal.ReadInt32(buffer, offset + 4))
            {
                cores++;
            }

            return cores;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int RelationProcessorCore = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
}
