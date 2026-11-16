using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PrismDock
{
    /// Keyboard layout (input language) of the app the user is typing in, and switching it.
    static class KeyLayout
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadKeyboardLayout(string klid, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int n, [Out] IntPtr[] list);
        [DllImport("user32.dll")] static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref IntPtr v, uint flags);

        const uint KLF_ACTIVATE = 1, KLF_SUBSTITUTE_OK = 2, KLF_SETFORPROCESS = 0x100;
        const uint WM_INPUTLANGCHANGEREQUEST = 0x50, SPI_SETDEFAULTINPUTLANG = 0x5A, SPIF_SENDCHANGE = 2;
        const int LangTr = 0x041F;
        public const string TurkishQ = "0000041F", TurkishF = "0001041F", EnglishUS = "00000409";

        public static IntPtr Current { get; private set; }
        /// last window outside PrismDock that had focus: the one the user wants to type in
        public static IntPtr Target { get; private set; }
        public static event Action Changed;
        static DispatcherTimer timer;
        static readonly uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public static void Start(Dispatcher d)
        {
            if (timer != null) return;
            timer = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (s, e) => Poll();
            timer.Start();
            Poll();
        }

        public static void Poll()
        {
            IntPtr fg = Native.GetForegroundWindow();
            uint pid = 0; if (fg != IntPtr.Zero) Native.GetWindowThreadProcessId(fg, out pid);
            if (fg != IntPtr.Zero && pid != ownPid) Target = fg;
            IntPtr h = IntPtr.Zero;
            if (Target != IntPtr.Zero && Native.IsWindow(Target)) { uint p2; h = GetKeyboardLayout(Native.GetWindowThreadProcessId(Target, out p2)); }
            if (h == IntPtr.Zero) h = GetKeyboardLayout(0);
            if (h != Current) { Current = h; if (Changed != null) Changed(); }
        }

        static int Lang(IntPtr h) { return (int)((long)h & 0xFFFF); }
        static int Device(IntPtr h) { return (int)(((long)h >> 16) & 0xFFFF); }

        /// KLID-like id of a loaded layout: Turkish Q / F are told apart by the device word.
        public static string Id(IntPtr h)
        {
            int l = Lang(h);
            if (l == LangTr) return Device(h) == LangTr ? TurkishQ : TurkishF;
            if (l == 0x0409 && Device(h) == 0x0409) return EnglishUS;
            return ((long)h & 0xFFFFFFFF).ToString("X8");
        }

        public static string Name(string id)
        {
            if (id == TurkishQ) return "Türkçe Q";
            if (id == TurkishF) return "Türkçe F";
            if (id == EnglishUS) return "İngilizce (ABD)";
            int lang; if (!int.TryParse(id.Substring(4), NumberStyles.HexNumber, null, out lang)) return id;
            try { var c = new CultureInfo(lang); return c.Parent != null && c.Parent.Name.Length > 0 ? c.Parent.NativeName : c.NativeName; } catch { return id; }
        }

        /// short badge text: "TR Q", "TR F", "EN", "DE"…
        public static string Short(string id)
        {
            if (id == TurkishQ) return "TR Q";
            if (id == TurkishF) return "TR F";
            int lang; if (!int.TryParse(id.Substring(4), NumberStyles.HexNumber, null, out lang)) return "?";
            try { return new CultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant(); } catch { return "?"; }
        }

        public static bool IsTurkish { get { return Lang(Current) == LangTr; } }

        /// Installed layouts other than Turkish (for the "other" tile).
        public static List<IntPtr> Installed()
        {
            int n = GetKeyboardLayoutList(0, null);
            var a = new IntPtr[Math.Max(n, 0)];
            if (n > 0) GetKeyboardLayoutList(n, a);
            return a.ToList();
        }

        /// Switches the typing app (and the session default) to the given layout id (KLID).
        public static void Switch(string klid)
        {
            IntPtr h = IntPtr.Zero;
            var inst = Installed().FirstOrDefault(x => Id(x) == klid);
            if (inst != IntPtr.Zero) h = inst;
            else
            {
                try { h = LoadKeyboardLayout(klid, KLF_ACTIVATE | KLF_SUBSTITUTE_OK); } catch { }
                if (h == IntPtr.Zero) { EventLog.Write("keyboard: load failed " + klid); return; }
            }
            Apply(h);
        }

        public static void Apply(IntPtr h)
        {
            try
            {
                ActivateKeyboardLayout(h, KLF_SETFORPROCESS);
                if (Target != IntPtr.Zero && Native.IsWindow(Target)) Native.PostMessage(Target, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, h);
                // every other open window follows too (Windows 8+ keeps one layout for the desktop by default)
                Native.PostMessage((IntPtr)0xFFFF, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, h);
                IntPtr v = h; SystemParametersInfo(SPI_SETDEFAULTINPUTLANG, 0, ref v, SPIF_SENDCHANGE);
                EventLog.Write("keyboard: " + Id(h));
            }
            catch (Exception ex) { EventLog.Write("keyboard: " + ex.Message); }
            Current = h;
            if (Changed != null) Changed();
            // the target app reports its real layout a moment later
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            t.Tick += (s, e) => { t.Stop(); Poll(); };
            t.Start();
        }

        // key caps shown in the panel's mini keyboard
        public static string[] Rows(string id)
        {
            if (id == TurkishQ) return new[] { "QWERTYUIOPĞÜ", "ASDFGHJKLŞİ", "ZXCVBNMÖÇ." };
            if (id == TurkishF) return new[] { "FGĞIODRNHPQW", "UİEAÜTKMLYŞ", "JÖVCÇZSB.," };
            return new[] { "QWERTYUIOP[]", "ASDFGHJKL;'", "ZXCVBNM,./" };
        }
    }

    // ================= Keyboard panel =================
    /// Opens out of the dock like the other panels; switching a layout flips the mini keyboard's keys
    /// in a wave and slides the accent highlight to the chosen tile.
    class KeyboardPanel : PanelWindow
    {
        const double Inner = 344, TileW = 108, TileH = 92, TileGap = 10;
        readonly List<TextBlock> caps = new List<TextBlock>();
        readonly List<ScaleTransform> capSc = new List<ScaleTransform>();
        readonly List<int> capCol = new List<int>();
        TextBlock badge, name, sub; ScaleTransform badgeSc;
        Border highlight; TranslateTransform hlT; Spring hlS, badgeS;
        readonly List<FrameworkElement> tileBtns = new List<FrameworkElement>();
        string[] tiles; string shownId; DispatcherTimer closeLater; bool switched;

        public KeyboardPanel() : base(384, 446)
        {
            KeyLayout.Changed += () => { if (IsOpen) Sync(true); };
        }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            caps.Clear(); capSc.Clear(); capCol.Clear(); tileBtns.Clear();
            KeyLayout.Poll();
            string cur = KeyLayout.Id(KeyLayout.Current);
            var sp = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
            sp.Children.Add(Header("Klavye"));

            // ---- current layout
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            badgeSc = new ScaleTransform(1, 1);
            var bg = new Border
            {
                Width = 58, Height = 58, CornerRadius = new CornerRadius(18), Background = Ui.Br(t.Accent),
                RenderTransform = badgeSc, RenderTransformOrigin = new Point(0.5, 0.5), VerticalAlignment = VerticalAlignment.Center
            };
            badge = Ui.Text("", 17, FontWeights.Bold, Ui.Br(t.Accent == Colors.White ? Color.FromRgb(20, 20, 24) : Colors.White));
            badge.HorizontalAlignment = HorizontalAlignment.Center; badge.VerticalAlignment = VerticalAlignment.Center;
            bg.Child = badge;
            head.Children.Add(bg);
            var txt = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            name = Ui.Text("", 17, FontWeights.SemiBold, Ui.Br(t.Fg));
            sub = Ui.Text("", 12, FontWeights.Normal, Ui.Br(t.Sub)); sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.Margin = new Thickness(0, 2, 0, 0);
            txt.Children.Add(name); txt.Children.Add(sub);
            Grid.SetColumn(txt, 1); head.Children.Add(txt);
            var hc = Card(head); hc.Padding = new Thickness(12);
            sp.Children.Add(hc);
            badgeS = new Spring(1, v => { badgeSc.ScaleX = v; badgeSc.ScaleY = v; }, 380, 0.5);

            // ---- mini keyboard
            var kb = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var rows = KeyLayout.Rows(cur);
            double[] indent = { 0, 9, 22 };
            for (int r = 0; r < rows.Length; r++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent[r], 0, 0, 4) };
                for (int c = 0; c < rows[r].Length; c++)
                {
                    var cap = Ui.Text(rows[r][c].ToString(), 12, FontWeights.SemiBold, Ui.Br(t.Fg));
                    cap.HorizontalAlignment = HorizontalAlignment.Center; cap.VerticalAlignment = VerticalAlignment.Center;
                    var sc = new ScaleTransform(1, 1);
                    var key = new Border
                    {
                        Width = 24, Height = 26, Margin = new Thickness(1.5, 0, 1.5, 0), CornerRadius = new CornerRadius(6),
                        Background = Ui.Br(Ui.A(t.Fg, t.Dark ? 0.09 : 0.07)), BorderBrush = Ui.Br(Ui.A(t.Fg, t.Dark ? 0.14 : 0.12)), BorderThickness = new Thickness(1),
                        Child = cap, RenderTransform = sc, RenderTransformOrigin = new Point(0.5, 0.5)
                    };
                    row.Children.Add(key);
                    caps.Add(cap); capSc.Add(sc); capCol.Add(c + r);
                }
                kb.Children.Add(row);
            }
            var space = new Border { Width = 150, Height = 18, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 1, 0, 0), Background = Ui.Br(Ui.A(t.Fg, t.Dark ? 0.09 : 0.07)), HorizontalAlignment = HorizontalAlignment.Center };
            kb.Children.Add(space);
            var kc = Card(kb); kc.Padding = new Thickness(8, 12, 8, 10); kc.Margin = new Thickness(0, 4, 0, 14);
            sp.Children.Add(kc);

            // ---- choices: sliding accent highlight behind three tiles
            string other = KeyLayout.Installed().Select(KeyLayout.Id).FirstOrDefault(i => i != KeyLayout.TurkishQ && i != KeyLayout.TurkishF) ?? KeyLayout.EnglishUS;
            tiles = new[] { KeyLayout.TurkishQ, KeyLayout.TurkishF, other };
            var area = new Grid { Width = Inner, Height = TileH, HorizontalAlignment = HorizontalAlignment.Center };
            var cv = new Canvas();
            hlT = new TranslateTransform();
            highlight = new Border { Width = TileW, Height = TileH, CornerRadius = new CornerRadius(18), Background = Ui.Br(t.Accent), RenderTransform = hlT };
            cv.Children.Add(highlight);
            area.Children.Add(cv);
            var tg = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 0; i < tiles.Length; i++) { var tb = Tile(tiles[i], i); tileBtns.Add(tb); tg.Children.Add(tb); }
            area.Children.Add(tg);
            hlS = new Spring(0, v => hlT.X = v, 300, 0.7);
            sp.Children.Add(area);

            var hint = Ui.Text("Windows'ta  Win + Boşluk  ile de geçebilirsin", 11, FontWeights.Normal, Ui.Br(t.Sub));
            hint.HorizontalAlignment = HorizontalAlignment.Center; hint.Margin = new Thickness(0, 14, 0, 0);
            sp.Children.Add(hint);
            shownId = null;
            Sync(false);
            return sp;
        }

        FrameworkElement Tile(string id, int i)
        {
            var t = Theme.Cur;
            var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string big = id == KeyLayout.TurkishQ ? "Q" : id == KeyLayout.TurkishF ? "F" : KeyLayout.Short(id);
            var b = Ui.Text(big, 28, FontWeights.Light, Ui.Br(t.Fg)); b.HorizontalAlignment = HorizontalAlignment.Center;
            var l = Ui.Text(KeyLayout.Name(id), 11.5, FontWeights.SemiBold, Ui.Br(t.Sub)); l.HorizontalAlignment = HorizontalAlignment.Center;
            l.TextTrimming = TextTrimming.CharacterEllipsis; l.MaxWidth = TileW - 10; l.Margin = new Thickness(0, 2, 0, 0);
            st.Children.Add(b); st.Children.Add(l);
            var btn = Ui.Button(st, () => Choose(id), null, TileW, TileH, 18);
            btn.Margin = new Thickness(0, 0, i < 2 ? TileGap + (Inner - 3 * TileW - 2 * TileGap) / 2 : 0, 0);
            btn.Tag = new[] { b, l };
            return btn;
        }

        void Choose(string id)
        {
            if (id == KeyLayout.Id(KeyLayout.Current)) { CloseAnimated(); return; }
            KeyLayout.Switch(id);
            switched = true;
            // let the flip play, then hand focus back to the app the user was typing in
            if (closeLater == null)
            {
                closeLater = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                closeLater.Tick += (s, e) => { closeLater.Stop(); if (IsOpen) CloseAnimated(); };
            }
            closeLater.Stop(); closeLater.Start();
        }

        /// updates texts; when the layout really changed, keys flip in a wave and the highlight slides
        void Sync(bool animate)
        {
            if (badge == null) return;
            var t = Theme.Cur;
            string id = KeyLayout.Id(KeyLayout.Current);
            if (id == shownId) return;
            bool first = shownId == null;
            shownId = id;
            badge.Text = KeyLayout.Short(id);
            name.Text = KeyLayout.Name(id);
            string title = KeyLayout.Target != IntPtr.Zero ? Native.GetText(KeyLayout.Target) : "";
            sub.Text = title.Length > 0 ? "Yazılan pencere: " + title : "Etkin klavye düzeni";

            int sel = Array.IndexOf(tiles, id);
            double step = (Inner - TileW) / 2;
            highlight.Opacity = sel >= 0 ? 1 : 0;
            if (sel >= 0) { if (first || !animate) hlS.Snap(sel * step); else hlS.To(sel * step); }
            for (int i = 0; i < tileBtns.Count; i++)
                {
                    var tb = (TextBlock[])tileBtns[i].Tag;
                    var on = i == sel; var fg = on ? (t.Accent == Colors.White ? Color.FromRgb(20, 20, 24) : Colors.White) : t.Fg;
                    tb[0].Foreground = Ui.Br(fg); tb[1].Foreground = Ui.Br(on ? Ui.A(fg, 0.85) : t.Sub);
                }

            if (first || !animate) { SetCaps(KeyLayout.Rows(id)); return; }
            badgeS.Snap(0.6); badgeS.To(1);
            Flip(KeyLayout.Rows(id));
        }

        void SetCaps(string[] rows)
        {
            int k = 0;
            foreach (var r in rows) foreach (var ch in r) if (k < caps.Count) caps[k++].Text = ch.ToString();
        }

        void Flip(string[] rows)
        {
            var letters = string.Concat(rows);
            for (int i = 0; i < caps.Count; i++)
            {
                int k = i; string next = k < letters.Length ? letters[k].ToString() : "";
                var delay = TimeSpan.FromMilliseconds(capCol[k] * 22);
                var down = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110)) { BeginTime = delay, EasingFunction = Ui.InOut };
                down.Completed += (s, e) =>
                {
                    caps[k].Text = next;
                    capSc[k].BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ui.Spring });
                };
                capSc[k].BeginAnimation(ScaleTransform.ScaleYProperty, down);
            }
        }

        protected override void Opened() { switched = false; KeyLayout.Poll(); Sync(false); }

        protected override void Closed2()
        {
            if (closeLater != null) closeLater.Stop();
            // give focus back to the app we switched the layout for (not when the user clicked elsewhere)
            var target = KeyLayout.Target;
            if (switched && IsActive && target != IntPtr.Zero && Native.IsWindow(target)) Native.SetForegroundWindow(target);
            switched = false;
        }
    }
}
