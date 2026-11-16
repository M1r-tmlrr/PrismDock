using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace PrismDock
{
    /// "Now playing" via Windows' system media transport controls (Spotify, browsers, media players ...).
    /// All WinRT types live behind NoInlining methods so a missing API can't break startup.
    static class Media
    {
        public static string Title = "", Artist = "", AppId = "";
        public static ImageSource Thumb;
        public static bool Playing, HasSession;
        public static event Action Changed;
        static Dispatcher ui;
        static object manager, session;   // kept alive (WinRT event sources)
        static int gen;

        public static void Init(Dispatcher d)
        {
            ui = d;
            try { InitCore(); } catch { }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void InitCore()
        {
            var op = GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            op.Completed = (a, st) =>
            {
                if (st != AsyncStatus.Completed) return;
                try
                {
                    var m = a.GetResults();
                    manager = m;
                    m.CurrentSessionChanged += (s, e) => Hook();
                    Hook();
                }
                catch { }
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Hook()
        {
            var m = manager as GlobalSystemMediaTransportControlsSessionManager;
            if (m == null) return;
            GlobalSystemMediaTransportControlsSession s = null;
            try { s = m.GetCurrentSession(); } catch { }
            if (!ReferenceEquals(s, session))
            {
                var old = session as GlobalSystemMediaTransportControlsSession;
                if (old != null) { try { old.MediaPropertiesChanged -= OnProps; old.PlaybackInfoChanged -= OnPlayback; } catch { } }
                session = s;
                if (s != null)
                {
                    s.MediaPropertiesChanged += OnProps;
                    s.PlaybackInfoChanged += OnPlayback;
                }
            }
            if (s == null)
            {
                HasSession = false; Title = Artist = AppId = ""; Thumb = null; Playing = false;
                Fire();
                return;
            }
            HasSession = true;
            try { AppId = s.SourceAppUserModelId ?? ""; } catch { }
            ReadPlayback(s);
            ReadProps(s);
        }

        static void OnProps(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) { ReadProps(s); }
        static void OnPlayback(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) { ReadPlayback(s); Fire(); }

        static void ReadPlayback(GlobalSystemMediaTransportControlsSession s)
        {
            try { Playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { }
        }

        static void ReadProps(GlobalSystemMediaTransportControlsSession s)
        {
            int my = Interlocked.Increment(ref gen);
            try
            {
                var op = s.TryGetMediaPropertiesAsync();
                op.Completed = (a, st) =>
                {
                    if (st != AsyncStatus.Completed || my != gen) return;
                    try
                    {
                        var p = a.GetResults();
                        string title = p.Title ?? "", artist = p.Artist ?? "";
                        bool same = title == Title && artist == Artist;
                        Title = title; Artist = artist;
                        if (!same) Thumb = null;
                        Fire();
                        if (p.Thumbnail != null && (!same || Thumb == null)) LoadThumb(p.Thumbnail, my);
                    }
                    catch { }
                };
            }
            catch { }
        }

        static void LoadThumb(IRandomAccessStreamReference r, int my)
        {
            var op = r.OpenReadAsync();
            op.Completed = (a, st) =>
            {
                if (st != AsyncStatus.Completed) return;
                try
                {
                    var stream = a.GetResults();
                    uint size = (uint)Math.Min(stream.Size, 8 * 1024 * 1024);
                    var reader = new DataReader(stream);
                    var load = reader.LoadAsync(size);
                    load.Completed = (b, st2) =>
                    {
                        try
                        {
                            if (st2 != AsyncStatus.Completed || my != gen) return;
                            var bytes = new byte[b.GetResults()];
                            reader.ReadBytes(bytes);
                            reader.Dispose(); stream.Dispose();
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.DecodePixelWidth = 96;
                            bi.StreamSource = new System.IO.MemoryStream(bytes);
                            bi.EndInit();
                            bi.Freeze();
                            Thumb = bi;
                            Fire();
                        }
                        catch { }
                    };
                }
                catch { }
            };
        }

        static void Fire()
        {
            if (ui == null) return;
            ui.BeginInvoke(new Action(() => { if (Changed != null) Changed(); }));
        }

        public static void PlayPause()
        {
            if (!TryCall(0)) Native.Keys(0xB3);
        }
        public static void Next() { if (!TryCall(1)) Native.Keys(0xB0); }
        public static void Prev() { if (!TryCall(2)) Native.Keys(0xB1); }

        static bool TryCall(int what)
        {
            try { return CallCore(what); } catch { return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool CallCore(int what)
        {
            var s = session as GlobalSystemMediaTransportControlsSession;
            if (s == null) return false;
            if (what == 0) { var x = s.TryTogglePlayPauseAsync(); }
            else if (what == 1) { var x = s.TrySkipNextAsync(); }
            else { var x = s.TrySkipPreviousAsync(); }
            return true;
        }
    }
}
