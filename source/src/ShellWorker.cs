using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PrismDock
{
    /// One background STA thread for shell work (icons, shortcut targets). If a call hangs, the thread is abandoned
    /// and a fresh one takes over the queue, so a stuck shell extension can never freeze the dock.
    static class ShellWorker
    {
        static readonly object gate = new object();
        static BlockingCollection<Action> queue;
        static Thread thread;
        static long busySince;   // ticks; 0 = idle
        public static volatile string Current = "";   // what the worker is doing (for the log)
        static Timer monitor;

        public static void Run(Action a)
        {
            if (monitor == null) monitor = new Timer(_ => Check(), null, 1000, 1000);
            lock (gate)
            {
                long b = Interlocked.Read(ref busySince);
                bool stuck = b != 0 && (DateTime.UtcNow.Ticks - b) > TimeSpan.FromSeconds(4).Ticks;
                if (thread == null || stuck)
                {
                    var old = queue;
                    queue = new BlockingCollection<Action>();
                    if (old != null) { Action x; while (old.TryTake(out x)) queue.Add(x); }
                    if (stuck) EventLog.Write("shell worker stuck > 4 s on '" + Current + "', replaced");
                    var q = queue;
                    thread = new Thread(() => Loop(q)) { IsBackground = true, Name = "PrismDock shell", Priority = ThreadPriority.BelowNormal };
                    thread.SetApartmentState(ApartmentState.STA);
                    Interlocked.Exchange(ref busySince, 0);
                    thread.Start();
                }
                queue.Add(a);
            }
        }

        /// a stuck worker is replaced even if no new request comes in (queued icons must still arrive)
        static void Check()
        {
            long b = Interlocked.Read(ref busySince);
            if (b == 0 || (DateTime.UtcNow.Ticks - b) <= TimeSpan.FromSeconds(4).Ticks) return;
            lock (gate) { if (queue == null || queue.Count == 0) return; }
            Run(() => { });
        }

        static void Loop(BlockingCollection<Action> q)
        {
            foreach (var a in q.GetConsumingEnumerable())
            {
                if (!ReferenceEquals(q, queue)) { lock (gate) { if (!ReferenceEquals(q, queue)) { queue.Add(a); return; } } }
                Interlocked.Exchange(ref busySince, DateTime.UtcNow.Ticks);
                try { a(); } catch { }
                Interlocked.Exchange(ref busySince, 0);
                if (!ReferenceEquals(q, queue)) return;   // we were replaced while busy
            }
        }
    }
}
