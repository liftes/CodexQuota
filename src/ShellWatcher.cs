using System;
using System.Runtime.InteropServices;

namespace CodexQuota
{
    // Install on the WPF dispatcher thread. WinEvent callbacks only enqueue UI work;
    // they never call UI Automation, move windows, or contact Codex.
    public sealed class ShellWatcher : IDisposable
    {
        private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint eventTime);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventCallback callback, uint processId, uint threadId, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        private readonly WinEventCallback callback;
        private IntPtr foregroundHook;
        private IntPtr taskbarHook;
        private IntPtr taskbar;
        private uint explorerProcessId;
        private bool isDisposed;
        private bool isStarted;
        public event Action Changed;
        public ShellWatcher() { callback = OnWinEvent; }
        public void Start()
        {
            if (isDisposed || isStarted) return;
            isStarted = true;
            foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0x0002);
            RefreshTaskbar();
        }
        public void RefreshTaskbar()
        {
            if (isDisposed) return;
            IntPtr found_ = NativeMethods.FindWindow("Shell_TrayWnd", null);
            uint processId_ = 0;
            if (found_ != IntPtr.Zero) NativeMethods.GetWindowThreadProcessId(found_, out processId_);
            if (found_ == taskbar && processId_ == explorerProcessId && (taskbarHook != IntPtr.Zero || processId_ == 0)) return;
            if (taskbarHook != IntPtr.Zero) { UnhookWinEvent(taskbarHook); taskbarHook = IntPtr.Zero; }
            taskbar = found_;
            explorerProcessId = processId_;
            if (isStarted && processId_ != 0)
                taskbarHook = SetWinEventHook(0x8002, 0x800B, IntPtr.Zero, callback, processId_, 0, 0x0002);
            TaskbarAnchor.InvalidateLayout();
        }
        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint eventTime)
        {
            if (isDisposed || window == IntPtr.Zero) return;
            // Foreground changes include opening Start and switching applications.
            // The handler restores z order in place instead of a Hide/Show cycle.
            if (eventType != 3)
            {
                if (eventType != 0x8002 && eventType != 0x8003 && eventType != 0x8004 && eventType != 0x800B) return;
                if (objectId != 0 && objectId != -4) return;
                IntPtr root_ = NativeMethods.GetAncestor(window, 2);
                if (window != taskbar && root_ != taskbar) return;
                TaskbarAnchor.InvalidateLayout();
            }
            Action changed_ = Changed;
            if (changed_ != null)
            {
                // No managed exception may cross the native callback boundary.
                try { changed_(); } catch (Exception) { }
            }
        }
        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
            if (taskbarHook != IntPtr.Zero) UnhookWinEvent(taskbarHook);
            foregroundHook = taskbarHook = IntPtr.Zero;
            GC.KeepAlive(callback);
        }
    }
}
