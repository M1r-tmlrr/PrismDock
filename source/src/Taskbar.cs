using System;

namespace PrismDock
{
    /// Hides / restores the Windows taskbar and manages our AppBar (reserved screen space).
    static class Taskbar
    {
        const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
        static readonly System.Collections.Generic.HashSet<IntPtr> appbars = new System.Collections.Generic.HashSet<IntPtr>();
        public static uint CallbackMsg = Native.RegisterWindowMessage("PrismDockAppBar");

        static IntPtr Tray { get { return Native.FindWindow("Shell_TrayWnd", null); } }

        static void ForEachTray(Action<IntPtr> a)
        {
            IntPtr t = Tray; if (t != IntPtr.Zero) a(t);
            IntPtr s = IntPtr.Zero;
            while ((s = Native.FindWindowEx(IntPtr.Zero, s, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) a(s);
        }

        static int GetState()
        {
            var d = new Native.APPBARDATA(); d.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(d); d.hWnd = Tray;
            return (int)Native.SHAppBarMessage(ABM_GETSTATE, ref d);
        }
        static void SetState(int st)
        {
            var d = new Native.APPBARDATA(); d.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(d); d.hWnd = Tray; d.lParam = (IntPtr)st;
            Native.SHAppBarMessage(ABM_SETSTATE, ref d);
        }

        public static void Hide(Settings s)
        {
            if (Tray == IntPtr.Zero) return;
            if (!s.TaskbarTouched)
            {
                s.TaskbarPrevState = GetState();
                s.TaskbarTouched = true;
                s.Save();
            }
            SetState(ABS_AUTOHIDE | ABS_ALWAYSONTOP);
            ForEachTray(h => Native.ShowWindow(h, Native.SW_HIDE));
        }

        /// Re-hides if explorer brought it back. Cheap check.
        public static void Enforce()
        {
            ForEachTray(h => { if (Native.IsWindowVisible(h)) Native.ShowWindow(h, Native.SW_HIDE); });
        }

        public static void Restore(Settings s)
        {
            if (!s.TaskbarTouched) return;
            SetState(s.TaskbarPrevState == 0 ? ABS_ALWAYSONTOP : s.TaskbarPrevState);
            ForEachTray(h => Native.ShowWindow(h, Native.SW_SHOWNA));
            s.TaskbarTouched = false;
            s.Save();
        }

        /// Reserve a strip at screen edge (physical pixels).
        public static void Reserve(IntPtr hwnd, bool top, int heightPx)
        {
            var d = new Native.APPBARDATA(); d.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(d); d.hWnd = hwnd;
            if (!appbars.Contains(hwnd))
            {
                d.uCallbackMessage = CallbackMsg;
                Native.SHAppBarMessage(ABM_NEW, ref d);
                appbars.Add(hwnd);
            }
            int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
            d.uEdge = top ? 1u : 3u;
            d.rc.Left = 0; d.rc.Right = sw;
            if (top) { d.rc.Top = 0; d.rc.Bottom = heightPx; } else { d.rc.Top = sh - heightPx; d.rc.Bottom = sh; }
            Native.SHAppBarMessage(ABM_QUERYPOS, ref d);
            if (top) d.rc.Bottom = d.rc.Top + heightPx; else d.rc.Top = d.rc.Bottom - heightPx;
            Native.SHAppBarMessage(ABM_SETPOS, ref d);
        }

        public static void Unreserve(IntPtr hwnd)
        {
            if (!appbars.Contains(hwnd)) return;
            var d = new Native.APPBARDATA(); d.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(d); d.hWnd = hwnd;
            Native.SHAppBarMessage(ABM_REMOVE, ref d);
            appbars.Remove(hwnd);
        }
    }
}
