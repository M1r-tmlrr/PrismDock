using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;

namespace PrismDock
{
    /// PrismDock's own start panel: greeting, search, pinned apps, recent files, places and power actions.
    class StartPanel : PanelWindow
    {
        TextBox box;
        public StartPanel() : base(480, 610) { }

        static string Greeting()
        {
            int h = DateTime.Now.Hour;
            return h < 5 ? "İyi geceler" : h < 12 ? "Günaydın" : h < 18 ? "İyi günler" : h < 22 ? "İyi akşamlar" : "İyi geceler";
        }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            var sp = new StackPanel { Margin = new Thickness(22, 20, 22, 16) };

            // ---- header: avatar + greeting
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            string user = Environment.UserName;
            var av = new Grid { Width = 46, Height = 46 };
            var cols = t.Tint.Length > 1 ? t.Tint : new[] { Color.FromRgb(94, 242, 208), Color.FromRgb(140, 90, 255) };
            var ag = new LinearGradientBrush(Color.FromRgb(cols[0].R, cols[0].G, cols[0].B), Color.FromRgb(cols[cols.Length - 1].R, cols[cols.Length - 1].G, cols[cols.Length - 1].B), 45);
            av.Children.Add(new Ellipse { Fill = ag });
            av.Children.Add(new Ellipse { Fill = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Colors.Transparent, 90), Margin = new Thickness(5, 3, 5, 20) });
            var ini = Ui.Text(user.Substring(0, 1).ToUpper(Tr), 20, FontWeights.SemiBold, Brushes.White); ini.HorizontalAlignment = HorizontalAlignment.Center;
            av.Children.Add(ini);
            head.Children.Add(av);
            var hs = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            hs.Children.Add(Ui.Text(Greeting() + ", " + user, 18, FontWeights.SemiBold, Ui.Br(t.Fg)));
            var up = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            string sub = "Açık kalma süresi " + (up.TotalDays >= 1 ? (int)up.TotalDays + " g " : "") + up.Hours + " sa " + up.Minutes + " dk";
            bool ch; int bat = SysStats.Battery(out ch);
            if (bat >= 0) sub += "  ·  Pil %" + bat + (ch ? " ⚡" : "");
            hs.Children.Add(Ui.Text(sub, 12, FontWeights.Normal, Ui.Br(t.Sub)));
            Grid.SetColumn(hs, 1); head.Children.Add(hs);
            sp.Children.Add(head);

            // ---- search field: typing hands over to the search window
            var sg = new Grid { Height = 40, Margin = new Thickness(0, 16, 0, 4) };
            sg.Children.Add(new Border { CornerRadius = new CornerRadius(20), Background = Ui.Br(Ui.A(t.Fg, 0.08)), BorderBrush = Ui.Br(Ui.A(t.Fg, 0.16)), BorderThickness = new Thickness(1) });
            var sgl = Ui.Glyph("", 15, Ui.Br(t.Sub)); sgl.HorizontalAlignment = HorizontalAlignment.Left; sgl.Margin = new Thickness(15, 0, 0, 0);
            sg.Children.Add(sgl);
            var ph = Ui.Text("Uygulama, dosya veya ayar ara…", 14, FontWeights.Normal, Ui.Br(Ui.A(t.Fg, 0.45))); ph.Margin = new Thickness(42, 0, 0, 0); ph.IsHitTestVisible = false;
            sg.Children.Add(ph);
            box = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Br(t.Fg), CaretBrush = Ui.Br(t.Fg), FontSize = 14, FontFamily = Ui.TextFont, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 14, 0) };
            box.TextChanged += (s, e) =>
            {
                ph.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (box.Text.Length == 0) return;
                string q = box.Text; box.Text = "";
                // first key opens the search window; anything typed before it has focus is appended there, never lost
                if (!handedOff) { handedOff = true; App.Dock.OpenSearch(q); CloseAnimated(); }
                else App.Dock.AppendSearch(q);
            };
            sg.Children.Add(box);
            sp.Children.Add(sg);

            // ---- pinned apps
            sp.Children.Add(Label("SABİTLENENLER"));
            pinnedGrid = new UniformGrid { Columns = 6, MinHeight = 70 };
            sp.Children.Add(pinnedGrid);

            // ---- recent files
            sp.Children.Add(Label("SON KULLANILANLAR"));
            recentPanel = new StackPanel { MinHeight = 148 };
            sp.Children.Add(recentPanel);

            // ---- places
            var places = new UniformGrid { Columns = 5, Margin = new Thickness(0, 10, 0, 0) };
            places.Children.Add(Place("", "Masaüstü", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
            places.Children.Add(Place("", "Belgeler", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
            places.Children.Add(Place("", "İndirilenler", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));
            places.Children.Add(Place("", "Resimler", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)));
            places.Children.Add(Place("", "Ayarlar", "ms-settings:"));
            sp.Children.Add(places);

            // ---- power
            var line = new Border { Height = 1, Background = Ui.Br(Ui.A(t.Fg, 0.12)), Margin = new Thickness(0, 12, 0, 10) };
            sp.Children.Add(line);
            var power = new UniformGrid { Columns = 6 };
            power.Children.Add(Power("", "Kilitle", false, () => Native.LockWorkStation()));
            power.Children.Add(Power("", "Uyku", false, () => Native.SetSuspendState(false, false, false)));
            power.Children.Add(Power("", "Görev Yön.", false, () => Run("taskmgr.exe")));
            power.Children.Add(Power("", "Oturumu kapat", true, () => Run("shutdown.exe", "/l")));
            power.Children.Add(Power("", "Yeniden başlat", true, () => Run("shutdown.exe", "/r /t 0")));
            power.Children.Add(Power("", "Kapat", true, () => Run("shutdown.exe", "/s /t 0")));
            sp.Children.Add(power);

            var foot = Ui.Text("Windows Başlat menüsü", 11.5, FontWeights.Normal, Ui.Br(t.Sub));
            var fb = Ui.Button(foot, () => { CloseAnimated(); WinKeyHook.OpenWindowsStart(); }, null, 0, 26, 13);
            fb.Padding = new Thickness(10, 0, 10, 0); fb.HorizontalAlignment = HorizontalAlignment.Center; fb.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(fb);
            return sp;
        }

        bool handedOff;
        UniformGrid pinnedGrid; StackPanel recentPanel;
        string pinSig, recentSig;

        void RefreshLists()
        {
            if (pinnedGrid == null) return;
            var t = Theme.Cur;
            string ps = string.Join("|", App.S.Pinned.Take(12));
            if (ps != pinSig)
            {
                pinSig = ps;
                pinnedGrid.Children.Clear();
                foreach (var p in App.S.Pinned.Take(12)) pinnedGrid.Children.Add(AppTile(DockApp.FromPath(p)));
                if (pinnedGrid.Children.Count < 12) pinnedGrid.Children.Add(Tile("\uE71D", "Tümü", () => { CloseAnimated(); App.Dock.OpenAppPicker(); }));
            }
            var rec = Recent(4);
            string rs = string.Join("|", rec.Select(f => f.FullName + f.LastWriteTime.Ticks));
            if (rs != recentSig)
            {
                recentSig = rs;
                recentPanel.Children.Clear();
                foreach (var f in rec) recentPanel.Children.Add(RecentRow(f));
                if (rec.Count == 0) recentPanel.Children.Add(Ui.Text("Henüz yok", 12, FontWeights.Normal, Ui.Br(t.Sub)));
            }
        }
        protected override void Opened()
        {
            handedOff = false;
            if (box != null) { box.Text = ""; box.Focus(); Keyboard.Focus(box); }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (box != null) { box.Focus(); Keyboard.Focus(box); }
                WinKeyHook.Release();   // keys typed since the Win tap now arrive here
            }), DispatcherPriority.Input);
            // lists that may have changed are refreshed after the panel is already on screen
            Dispatcher.BeginInvoke(new Action(RefreshLists), DispatcherPriority.Background);
        }

        protected override void Closed2() { WinKeyHook.Release(); }

        static void Run(string f, string args = null)
        {
            try { Process.Start(new ProcessStartInfo(f, args ?? "") { UseShellExecute = true }); } catch { }
        }

        FrameworkElement AppTile(DockApp a)
        {
            var t = Theme.Cur;
            var st = new StackPanel();
            var img = new Image { Width = 30, Height = 30, Source = a.Icon(), Margin = new Thickness(0, 4, 0, 0) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            st.Children.Add(img);
            var n = Ui.Text(a.Name, 10.5, FontWeights.Normal, Ui.Br(t.Fg)); n.HorizontalAlignment = HorizontalAlignment.Center; n.TextTrimming = TextTrimming.CharacterEllipsis; n.MaxWidth = 64; n.Margin = new Thickness(0, 5, 0, 0);
            st.Children.Add(n);
            var b = Ui.Button(st, () => { CloseAnimated(); a.Start(); }, a.Name, 70, 70, 16);
            b.Margin = new Thickness(1);
            return b;
        }

        FrameworkElement Tile(string glyph, string name, Action click)
        {
            var t = Theme.Cur;
            var st = new StackPanel();
            var c = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Ui.Br(Ui.A(t.Fg, 0.12)), Margin = new Thickness(0, 4, 0, 0), Child = Ui.Glyph(glyph, 13, Ui.Br(t.Fg)) };
            st.Children.Add(c);
            var n = Ui.Text(name, 10.5, FontWeights.Normal, Ui.Br(t.Sub)); n.HorizontalAlignment = HorizontalAlignment.Center; n.Margin = new Thickness(0, 5, 0, 0);
            st.Children.Add(n);
            var b = Ui.Button(st, click, null, 70, 70, 16);
            b.Margin = new Thickness(1);
            return b;
        }

        static List<FileInfo> Recent(int n)
        {
            try
            {
                var dir = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.Recent));
                return dir.GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(n * 3)
                    .Where(f => { var tg = Shortcut.Target(f.FullName); return tg != null && File.Exists(tg); }).Take(n).ToList();
            }
            catch { return new List<FileInfo>(); }
        }

        FrameworkElement RecentRow(FileInfo f)
        {
            var t = Theme.Cur;
            var g = new Grid { Height = 36 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string target = Shortcut.Target(f.FullName);
            var img = new Image { Width = 22, Height = 22, Source = Icons.ForPath(target, (int)(32 * scale)) };
            g.Children.Add(img);
            var n = Ui.Text(Path.GetFileName(target), 13, FontWeights.Normal, Ui.Br(t.Fg)); n.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(n, 1); g.Children.Add(n);
            var when = Ui.Text(Ago(f.LastWriteTime), 11, FontWeights.Normal, Ui.Br(t.Sub)); when.Margin = new Thickness(8, 0, 10, 0);
            Grid.SetColumn(when, 2); g.Children.Add(when);
            var b = Ui.Button(g, () => { CloseAnimated(); Run(target); }, target, 0, 0, 10);
            b.Margin = new Thickness(0, 1, 0, 1);
            return b;
        }

        static string Ago(DateTime d)
        {
            var s = DateTime.Now - d;
            if (s.TotalMinutes < 1) return "şimdi";
            if (s.TotalHours < 1) return (int)s.TotalMinutes + " dk önce";
            if (s.TotalDays < 1) return (int)s.TotalHours + " sa önce";
            if (s.TotalDays < 2) return "dün";
            return (int)s.TotalDays + " gün önce";
        }

        FrameworkElement Place(string glyph, string name, string path)
        {
            var t = Theme.Cur;
            var st = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            st.Children.Add(Ui.Glyph(glyph, 13, Ui.Br(t.Fg)));
            var n = Ui.Text(name, 11.5, FontWeights.Normal, Ui.Br(t.Fg)); n.Margin = new Thickness(6, 0, 0, 0);
            st.Children.Add(n);
            var b = Ui.Button(st, () => { CloseAnimated(); Run(path); }, null, 0, 32, 16);
            b.Background = Ui.Br(Ui.A(t.Fg, 0.07)); b.Margin = new Thickness(2, 0, 2, 0);
            return b;
        }

        /// power button; dangerous ones need a second click within 3 s ("Emin misin?")
        FrameworkElement Power(string glyph, string name, bool confirm, Action act)
        {
            var t = Theme.Cur;
            var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var gl = Ui.Glyph(glyph, 17, Ui.Br(t.Fg));
            st.Children.Add(gl);
            var l = Ui.Text(name, 10, FontWeights.Normal, Ui.Br(t.Sub)); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 6, 0, 0); l.TextAlignment = TextAlignment.Center;
            st.Children.Add(l);
            Border b = null;
            bool armed = false;
            DispatcherTimer disarm = null;
            b = Ui.Button(st, () =>
            {
                if (!confirm || armed) { CloseAnimated(); act(); return; }
                armed = true;
                l.Text = "Emin misin?"; l.Foreground = Ui.Br(Color.FromRgb(255, 110, 100)); gl.Foreground = l.Foreground;
                b.BorderBrush = Ui.Br(Color.FromArgb(150, 255, 90, 80)); b.BorderThickness = new Thickness(1);
                if (disarm == null) { disarm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; disarm.Tick += (s, e) => { disarm.Stop(); armed = false; l.Text = name; l.Foreground = Ui.Br(t.Sub); gl.Foreground = Ui.Br(t.Fg); b.BorderThickness = new Thickness(0); }; }
                disarm.Stop(); disarm.Start();
            }, name, 72, 70, 16);
            b.Margin = new Thickness(1);
            return b;
        }
    }
}
