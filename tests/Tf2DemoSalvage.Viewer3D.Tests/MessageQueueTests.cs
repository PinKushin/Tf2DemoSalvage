using System;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// <see cref="MessageQueue.Waiting"/> on this thread's own queue, against messages this test posts.
/// </summary>
/// <remarks>
/// **WM_NULL is a message whose id is zero, and zero used to mean "the queue is empty".** The render
/// loop drew while <c>Waiting()</c> said zero, so a WM_NULL at the head of the queue — which COM and
/// the shell post to wake a thread — read as nothing waiting, and since the peek does not remove it,
/// the loop never yielded again: the viewer drew on and handled no input. On CI that was ten UI
/// tests failing after the Export test's file dialog.
/// </remarks>
[TestFixture]
public sealed partial class MessageQueueTests
{
    private const uint WmNull = 0x0000;

    private const uint WmApp = 0x8000;

    [SetUp]
    public void Drain()
    {
        while (PeekMessage(out _, IntPtr.Zero, 0, 0, PeekRemove))
        {
            // Nothing to dispatch: this queue belongs to the test thread and holds only test posts.
        }
    }

    [TearDown]
    public void DrainAfter() => Drain();

    [Test]
    public void Waiting_OnAnEmptyQueue_IsNull() =>
        MessageQueue.Waiting().ShouldBeNull();

    [Test]
    public void Waiting_WithAWmNullPosted_IsZeroRatherThanNull()
    {
        PostThreadMessage(GetCurrentThreadId(), WmNull, IntPtr.Zero, IntPtr.Zero).ShouldBeTrue();

        MessageQueue.Waiting().ShouldBe(WmNull);
    }

    [Test]
    public void Waiting_WithAPostedMessage_IsItsId()
    {
        PostThreadMessage(GetCurrentThreadId(), WmApp, IntPtr.Zero, IntPtr.Zero).ShouldBeTrue();

        MessageQueue.Waiting().ShouldBe(WmApp);
    }

    private const uint PeekRemove = 1;

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

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(
        out NativeMessage message, IntPtr window, uint filterMinimum, uint filterMaximum, uint removal);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
