using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace PrismDock
{
    /// Dock side of the Windows-key handling. The keyboard hook itself runs in a separate tiny process
    /// ("PrismDock.exe --keyhook"): Windows drops a low-level hook that answers too slowly (and lets the key through,
    /// so its Start menu opens). In the dock process a garbage collection or a page-in after a memory trim can cause
    /// exactly that; the helper never allocates after start-up, so it never pauses.
    static class WinKeyHook
    {
        public static readonly uint TapMsg = Native.RegisterWindowMessage("PrismDock_WinTap");
        static Process helper;
        static bool wanted;
        static IntPtr dock;
        static EventWaitHandle releaseEvt;
        static Dispatcher ui;

        public static void Enable(bool on, IntPtr dockHwnd)
        {
            if (ui == null) ui = Dispatcher.CurrentDispatcher;
            wanted = on; dock = dockHwnd;
            if (releaseEvt == null) releaseEvt = new EventWaitHandle(false, EventResetMode.AutoReset, ReleaseName(Process.GetCurrentProcess().Id));
            if (rehookEvt == null) rehookEvt = new EventWaitHandle(false, EventResetMode.AutoReset, RehookName(Process.GetCurrentProcess().Id));
            if (on && (helper == null || helper.HasExited)) StartHelper();
            else if (!on) StopHelper();
        }

        static string ReleaseName(int pid) { return "PrismDock_KeyRelease_" + pid; }
        static string RehookName(int pid) { return "PrismDock_KeyRehook_" + pid; }
        static EventWaitHandle rehookEvt;
        static DateTime lastRehook;
        public static DateTime LastTap = DateTime.MinValue;
        public static DateTime OwnStartUntil = DateTime.MinValue;

        /// Windows' own Start appeared although the user didn't ask for it: our hook was bypassed -> re-hook now
        public static void Bypassed()
        {
            if ((DateTime.Now - lastRehook).TotalSeconds < 5 || DateTime.Now < OwnStartUntil) return;
            lastRehook = DateTime.Now;
            EventLog.Write("Windows Start opened without our hook -> re-hooking");
            try { if (rehookEvt != null) rehookEvt.Set(); } catch { }
        }

        static void StartHelper()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var psi = new ProcessStartInfo(exe, "--keyhook " + Process.GetCurrentProcess().Id + " " + dock.ToInt64()) { UseShellExecute = false, CreateNoWindow = true };
                helper = Process.Start(psi);
                helper.EnableRaisingEvents = true;
                helper.Exited += (s, e) =>
                {
                    if (!wanted || ui == null) return;
                    EventLog.Write("key hook helper exited (" + SafeExit(helper) + "), restarting");
                    ui.BeginInvoke(new Action(() =>
                    {
                        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                        t.Tick += (o, e2) => { t.Stop(); if (wanted && (helper == null || helper.HasExited)) StartHelper(); };
                        t.Start();
                    }));
                };
            }
            catch (Exception ex) { EventLog.Write("key hook helper failed: " + ex.Message); }
        }

        static string SafeExit(Process p) { try { return p.ExitCode.ToString(); } catch { return "?"; } }

        static void StopHelper()
        {
            try { if (helper != null && !helper.HasExited) helper.Kill(); } catch { }
            helper = null;
        }

        /// the panel has keyboard focus: the helper hands over what was typed since the Win tap
        public static void Release() { try { if (releaseEvt != null) releaseEvt.Set(); } catch { } }

        /// opens the real Windows Start menu (injected, so the hook ignores it)
        public static void OpenWindowsStart() { OwnStartUntil = DateTime.Now.AddSeconds(3); Native.Keys(0x5B); }

        // =====================================================================================================
        // helper process
        // =====================================================================================================
        [StructLayout(LayoutKind.Sequential)] struct KBD { public uint vk, scan, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; public long pad; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [DllImport("user32.dll")] static extern uint SendInput(uint n, [In] INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr proc);

        static IntPtr hHook;
        static Native.LowLevelProc procRef;
        static IntPtr target;
        static bool winDown, combo;
        // everything the hook touches is allocated up front: no allocation on the hot path = no GC pause, ever
        static readonly object gate = new object();
        static readonly INPUT[] held = new INPUT[512];
        static readonly INPUT[] tap = new INPUT[3];
        static int heldCount;
        static bool holding;
        static int holdStart;
        static int inputSize;

        public static void RunHelper(string[] args)
        {
            int parent = int.Parse(args[1]);
            target = (IntPtr)long.Parse(args[2]);
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; } catch { }
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
            inputSize = Marshal.SizeOf(typeof(INPUT));
            procRef = Proc;
            Proc(-1, IntPtr.Zero, IntPtr.Zero);   // JIT everything before the hook goes live
            uint me = Native.GetCurrentThreadId();

            // dock exits -> we exit
            var pw = new Thread(() =>
            {
                try { using (var p = Process.GetProcessById(parent)) p.WaitForExit(); } catch { }
                PostThreadMessage(me, 0x12 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
            }) { IsBackground = true };
            pw.Start();
            // dock says "panel has focus" -> hand over held keys
            var rw = new Thread(() =>
            {
                EventWaitHandle ev;
                if (!EventWaitHandle.TryOpenExisting(ReleaseName(parent), out ev)) return;
                while (true) { ev.WaitOne(); Flush(); }
            }) { IsBackground = true };
            rw.Start();

            Hook();
            SetTimer(IntPtr.Zero, UIntPtr.Zero, 10000, IntPtr.Zero);   // periodic re-hook: Windows may drop a hook silently
            // the dock asks for an immediate re-hook when Windows' own Start menu appeared (= our hook was bypassed)
            var hw = new Thread(() =>
            {
                EventWaitHandle ev;
                if (!EventWaitHandle.TryOpenExisting(RehookName(parent), out ev)) return;
                while (true) { ev.WaitOne(); PostThreadMessage(me, 0x8001 /*WM_APP+1*/, IntPtr.Zero, IntPtr.Zero); }
            }) { IsBackground = true };
            hw.Start();
            MSG m;
            while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0)
            {
                if (m.message == 0x8001) { Hook(); continue; }
                if (m.message == 0x113 /*WM_TIMER*/)
                {
                    if (!winDown) Hook();
                    lock (gate) { if (holding && Environment.TickCount - holdStart > 1200) FlushLocked(); }
                }
            }
            if (hHook != IntPtr.Zero) Native.UnhookWindowsHookEx(hHook);
        }

        static void Hook()
        {
            IntPtr old = hHook;
            hHook = Native.SetWindowsHookEx(13, procRef, Native.GetModuleHandle(null), 0); // WH_KEYBOARD_LL
            if (old != IntPtr.Zero) Native.UnhookWindowsHookEx(old);
        }

        static void Set(ref INPUT i, uint vk, uint scan, uint flags) { i.type = 1; i.ki.vk = (ushort)vk; i.ki.scan = (ushort)scan; i.ki.flags = flags; i.ki.time = 0; i.ki.extra = IntPtr.Zero; }

        static unsafe IntPtr Proc(int code, IntPtr w, IntPtr l)
        {
            if (code < 0 || l == IntPtr.Zero) return code == -1 && l == IntPtr.Zero ? IntPtr.Zero : Native.CallNextHookEx(hHook, code, w, l);
            KBD* k = (KBD*)l;
            if ((k->flags & 0x10) != 0) return Native.CallNextHookEx(hHook, code, w, l);   // injected (ours or anyone's)
            int msg = w.ToInt32();
            bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
            bool isWin = k->vk == 0x5B || k->vk == 0x5C;

            lock (gate)
            {
                if (holding && Environment.TickCount - holdStart > 1200) FlushLocked();   // panel never got focus: give the keys back
                if (holding && isWin) FlushLocked();
                if (holding && (down || up) && heldCount < held.Length)
                {
                    Set(ref held[heldCount++], k->vk, k->scan, (up ? 2u : 0u) | ((k->flags & 1) != 0 ? 1u : 0u));
                    return (IntPtr)1;
                }
            }
            if (isWin)
            {
                if (down && !winDown) { winDown = true; combo = false; }
                else if (up && winDown)
                {
                    winDown = false;
                    if (!combo)
                    {
                        // swallow the real key-up and replay "mask down, mask up, Win up": Windows always sees the
                        // unassigned vkE8 between Win down and Win up, so its Start menu never opens
                        Set(ref tap[0], 0xE8, 0, 0); Set(ref tap[1], 0xE8, 0, 2); Set(ref tap[2], k->vk, k->scan, 2 | 1);
                        SendInput(3, tap, inputSize);
                        lock (gate) { heldCount = 0; holding = true; holdStart = Environment.TickCount; }
                        PostMessage(target, TapMsg, IntPtr.Zero, IntPtr.Zero);
                        return (IntPtr)1;
                    }
                }
            }
            else if (down && winDown) combo = true;
            return Native.CallNextHookEx(hHook, code, w, l);
        }

        static void Flush() { lock (gate) FlushLocked(); }

        static void FlushLocked()
        {
            holding = false;
            if (heldCount == 0) return;
            uint n = (uint)heldCount; heldCount = 0;
            SendInput(n, held, inputSize);   // injected: passes the hook, goes to the focused window (the panel)
        }
    }
}
