using System;

namespace PrismDock
{
    /// Hides / restores the Windows taskbar and manages our AppBar (reserved screen space).
    static class Taskbar
    {
        const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_SETSTATE = 10;
        const int ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
        static readonly System.Collections.Generic.HashSet<IntPtr> appbars = new System.Collections.Generic.HashSet<IntPtr>();
        static readonly System.Collections.Generic.Dictionary<IntPtr, string> lastPos = new System.Collections.Generic.Dictionary<IntPtr, string>();
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
            graceUntil = Environment.TickCount + 8000;   // explorer re-shows it a few times right after: that's normal
            ForEachTray(h => Native.ShowWindow(h, Native.SW_HIDE));
        }

        /// Re-hides if explorer brought it back. Cheap check.
        public static void Enforce()
        {
            ForEachTray(h => { if (Native.IsWindowVisible(h) && MayHide()) { Native.ShowWindow(h, Native.SW_HIDE); EventLog.Write("taskbar re-hidden"); } });
        }

        static readonly System.Collections.Generic.Queue<long> reshows = new System.Collections.Generic.Queue<long>();
        static long backoffUntil, lastCounted;
        static volatile int graceUntil;
        /// Explorer (Windows 11 above all) can re-show its taskbar again and again. Fighting every time makes explorer
        /// re-layout the taskbar in a loop, which slows the whole desktop down. More than 8 separate re-shows in 10 s (outside the first seconds after hiding):
        /// stop hiding for a minute (the taskbar stays in auto-hide, so it only slides in at the screen edge).
        public static bool MayHide()
        {
            lock (reshows)
            {
                long now = Environment.TickCount;
                if (now < graceUntil) return true;
                if (now < backoffUntil) return false;
                if (now - lastCounted < 1000) return true;   // one burst = one re-show
                lastCounted = now;
                reshows.Enqueue(now);
                while (reshows.Count > 0 && now - reshows.Peek() > 10000) reshows.Dequeue();
                if (reshows.Count <= 8) return true;
                reshows.Clear();
                backoffUntil = now + 60000;
                EventLog.Write("taskbar: explorer keeps re-showing it -> not fighting for 60 s");
                return false;
            }
        }

        /// Windows 11's explorer re-shows the taskbar shortly after its auto-hide state changes:
        /// hide, then keep re-hiding for a few seconds. Runs on a worker thread (explorer can be slow at login).
        public static void HideHard(Settings s)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < 40 && Tray == IntPtr.Zero; i++) System.Threading.Thread.Sleep(250); // explorer still starting
                    Hide(s);
                    EventLog.Write("taskbar hidden (" + sw.ElapsedMilliseconds + " ms)");
                    for (int i = 0; i < 60; i++) { System.Threading.Thread.Sleep(100); if (s.HideTaskbar) Enforce(); }
                }
                catch { }
            });
        }

        public static bool IsTray(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            string c = Native.GetClass(h);
            return c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
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
        public static void Reserve(IntPtr hwnd, bool top, int heightPx) { Reserve(hwnd, top, heightPx, null); }

        /// area: the monitor (physical px) the strip belongs to; null = primary
        public static void Reserve(IntPtr hwnd, bool top, int heightPx, System.Drawing.Rectangle? area)
        {
            var d = new Native.APPBARDATA(); d.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(d); d.hWnd = hwnd;
            if (!appbars.Contains(hwnd))
            {
                d.uCallbackMessage = CallbackMsg;
                Native.SHAppBarMessage(ABM_NEW, ref d);
                appbars.Add(hwnd);
            }
            var m = area ?? new System.Drawing.Rectangle(0, 0, Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
            // same strip as last time: nothing to negotiate (avoids appbar ping-pong between our bars)
            string key = top + ":" + heightPx + ":" + m;
            string prev;
            if (lastPos.TryGetValue(hwnd, out prev) && prev == key) return;
            lastPos[hwnd] = key;
            d.uEdge = top ? 1u : 3u;
            d.rc.Left = m.Left; d.rc.Right = m.Right;
            if (top) { d.rc.Top = m.Top; d.rc.Bottom = m.Top + heightPx; } else { d.rc.Top = m.Bottom - heightPx; d.rc.Bottom = m.Bottom; }
            Native.SHAppBarMessage(ABM_QUERYPOS, ref d);
            if (top) d.rc.Bottom = d.rc.Top + heightPx; else d.rc.Top = d.rc.Bottom - heightPx;
            Native.SHAppBarMessage(ABM_SETPOS, ref d);
        }

        /// remove every strip we reserved (safe from any thread; used when the UI thread is stuck)
        public static void UnreserveAll()
        {
            IntPtr[] all;
            lock (appbars) { all = new IntPtr[appbars.Count]; appbars.CopyTo(all); }
            foreach (var h in all)
            {
                var d = new Native.APPBARDATA(); d.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(d); d.hWnd = h;
                Native.SHAppBarMessage(ABM_REMOVE, ref d);
            }
        }

        public static void Unreserve(IntPtr hwnd)
        {
            if (!appbars.Contains(hwnd)) return;
            var d = new Native.APPBARDATA(); d.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(d); d.hWnd = hwnd;
            Native.SHAppBarMessage(ABM_REMOVE, ref d);
            appbars.Remove(hwnd);
            lastPos.Remove(hwnd);
        }
    }
}
