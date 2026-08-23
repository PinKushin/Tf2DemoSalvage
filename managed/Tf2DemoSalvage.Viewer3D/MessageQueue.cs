using System;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Whether Windows has anything waiting for this thread.
/// </summary>
/// <remarks>
/// **The missing half of an idle-driven render loop.** <c>Application.Idle</c> fires once when the
/// message queue empties and not again until a message arrives and is dispatched — so a handler
/// that draws one frame and returns draws one frame and stops. That is exactly what happened here:
/// playback ran "underneath" and the picture only moved when the mouse did.
///
/// The documented shape is to stay in the handler while the queue is empty and leave the moment
/// anything arrives. That renders continuously while nothing else needs doing, and yields
/// immediately to a click, a keystroke or a resize — which is what keeps the UI responsive without
/// flooding it with paint messages, the mistake that came before this one.
/// </remarks>
internal static partial class MessageQueue
{
    /// <summary>Whether a message is waiting, without removing it.</summary>
    /// <returns><c>true</c> when something is queued for this thread.</returns>
    public static bool HasWork() => PeekMessage(out NativeMessage _, IntPtr.Zero, 0, 0, PeekNoRemove);

    /// <summary>Which message is waiting, or zero when the queue is empty.</summary>
    /// <returns>The Windows message id, or 0.</returns>
    /// <remarks>
    /// **The same peek as <see cref="HasWork"/>, keeping the answer it already had.** The id was
    /// being read out of the queue and discarded, and it is the one fact that identifies why the
    /// render loop is yielding.
    ///
    /// **B148 is why this exists.** After a demo switch the viewer drops from about 290 frames a
    /// second to 20 and never recovers — with the demo paused, and with sampling, posing and
    /// lighting all reported at zero, so the cost is not in any of the work the loop already times.
    /// The loop renders only while this queue is empty, so 20 frames a second is not a slow frame:
    /// it is a queue that is never empty, and the id says who is filling it.
    ///
    /// `WM_NULL` is 0 and would be indistinguishable from "nothing waiting". Nothing posts it here,
    /// and the loop's own emptiness check remains <see cref="HasWork"/>, so the ambiguity costs a
    /// diagnostic line rather than a frame.
    /// </remarks>
    public static uint Waiting() =>
        PeekMessage(out NativeMessage message, IntPtr.Zero, 0, 0, PeekNoRemove) ? message.Message : 0;

    /// <summary>Look at the message without taking it off the queue.</summary>
    private const uint PeekNoRemove = 0;

    // Pinned to System32 so the loader cannot be pointed at a user32.dll dropped beside the
    // executable or anywhere else on the search path (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(
        out NativeMessage message,
        IntPtr window,
        uint filterMinimum,
        uint filterMaximum,
        uint removal);

    /// <summary>The shape Windows fills in; none of the fields are read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }
}
