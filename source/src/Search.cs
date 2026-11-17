using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PrismDock
{
    class AppEntry
    {
        public string Name, Launch, Target, Key, Alias = "", Icon;   // Icon: optional different path for the icon
        public bool Admin;   // offer "run as administrator"
    }

    /// Installed applications: Start Menu shortcuts (desktop apps, e.g. YASB) + Store apps from shell:AppsFolder.
    static class AppCatalog
    {
        static List<AppEntry> list;
        static bool loading;
        public static event Action Loaded;
        public static List<AppEntry> Items { get { return list ?? new List<AppEntry>(); } }
        public static bool Ready { get { return list != null; } }

        static readonly string[] Junk = { "uninstall", "kaldır", "remove", "readme", "release notes", "help", "documentation", "website", "web site", "license", "changelog", "manual", "support", "debug", "safe mode" };

        public static void Load(Dispatcher ui)
        {
            if (loading || list != null) return;
            loading = true;
            var th = new Thread(() =>
            {
                var res = new List<AppEntry>();
                try { Collect(res); } catch { }
                ui.BeginInvoke(new Action(() => { list = res; loading = false; if (Loaded != null) Loaded(); }));
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Priority = ThreadPriority.BelowNormal;
            th.Start();
        }

        public static void Reload(Dispatcher ui) { if (!loading) { list = null; Load(ui); } }

        static void Collect(List<AppEntry> res)
        {
            var seen = new HashSet<string>();
            object wsh = null;
            try { wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); } catch { }
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); } catch { continue; }
                foreach (var f in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    string name = Icons.DisplayName(f) ?? fileName;   // e.g. "Task Manager.lnk" is shown as "Görev Yöneticisi"
                    if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    string low = name.ToLowerInvariant();
                    if (Junk.Any(j => low.Contains(j))) continue;
                    if (f.IndexOf(@"\Startup\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string key = Search.Fold(name);
                    if (seen.Contains(key)) continue;
                    string target = null;
                    try { if (wsh != null) { dynamic sc = ((dynamic)wsh).CreateShortcut(f); target = sc.TargetPath; } } catch { }
                    if (!string.IsNullOrEmpty(target))
                    {
                        string ext = Path.GetExtension(target).ToLowerInvariant();
                        if (ext == ".txt" || ext == ".html" || ext == ".htm" || ext == ".url" || ext == ".pdf" || ext == ".chm") continue;
                    }
                    seen.Add(key);
                    var e = new AppEntry { Name = name, Launch = f, Target = string.IsNullOrEmpty(target) ? null : target, Key = key };
                    bool cpl = e.Target != null && Path.GetFileName(e.Target).Equals("control.exe", StringComparison.OrdinalIgnoreCase) && key != Search.Fold("Denetim Masası");
                    e.Alias = Search.Fold(fileName + " " + (cpl || e.Target == null ? "" : Path.GetFileNameWithoutExtension(e.Target) + " " + AliasFor(e.Target)));
                    e.Admin = IsConsole(e.Target);
                    res.Add(e);
                }
            }
            // Store / packaged apps
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic folder = shell.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}");
                if (folder != null)
                    foreach (dynamic it in folder.Items())
                    {
                        string name = it.Name, path = it.Path;
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) continue;
                        string key = Search.Fold(name);
                        if (seen.Contains(key)) continue;
                        string lowName = name.ToLowerInvariant();
                        if (Junk.Any(j => lowName.Contains(j))) continue;
                        seen.Add(key);
                        string exeName = path.IndexOf('!') < 0 && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(path) : null;
                        res.Add(new AppEntry { Name = name, Launch = @"shell:AppsFolder\" + path, Key = key, Alias = Search.Fold(path.Replace('.', ' ').Replace('\\', ' ') + " " + AliasFor(exeName)) });
                    }
            }
            catch { }
            AddBuiltIns(res, seen);
            res.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        public const string SettingsIcon = @"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";

        static string Sys(string exe) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe); }

        /// essential system tools, guaranteed to be findable even if their Start Menu shortcut is missing or renamed
        static void AddBuiltIns(List<AppEntry> res, HashSet<string> seen)
        {
            string wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
            var list = new List<string[]>
            {
                new[] { "Görev Yöneticisi", Sys("taskmgr.exe") },
                new[] { "Komut İstemi", Sys("cmd.exe") },
                new[] { "Windows PowerShell", Sys(@"WindowsPowerShell\v1.0\powershell.exe") },
                new[] { "Denetim Masası", Sys("control.exe") },
                new[] { "Kayıt Defteri Düzenleyicisi", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "regedit.exe") },
                new[] { "Aygıt Yöneticisi", Sys("devmgmt.msc") },
                new[] { "Disk Yönetimi", Sys("diskmgmt.msc") },
                new[] { "Hizmetler", Sys("services.msc") },
                new[] { "Olay Görüntüleyicisi", Sys("eventvwr.msc") },
                new[] { "Kaynak İzleyicisi", Sys("resmon.exe") },
                new[] { "Sistem Yapılandırması", Sys("msconfig.exe") },
                new[] { "Sistem Bilgileri", Sys("msinfo32.exe") },
                new[] { "Dosya Gezgini", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") },
                // the Start Menu "Settings" shortcut points at Control.exe (Windows opens it as Settings through its app id):
                // launch the Settings app itself
                new[] { "Ayarlar", "ms-settings:", "ayarlar settings windows ayarlari ms-settings" },
            };
            if (File.Exists(wt)) list.Insert(0, new[] { "Terminal", wt });
            foreach (var b in list)
            {
                string key = Search.Fold(b[0]);
                bool shellItem = DockApp.IsShellPath(b[1]) || b[1].EndsWith(":");   // shell:AppsFolder\..., ms-settings:
                if (!shellItem && !File.Exists(b[1])) continue;
                string alias = b.Length > 2 ? Search.Fold(b[2]) : Search.Fold(Path.GetFileNameWithoutExtension(b[1]) + " " + AliasFor(b[1]));
                // same name in the Start Menu: the built-in definition wins (its shortcut may point somewhere else)
                var byName = res.FirstOrDefault(x => x.Key == key);
                if (byName != null)
                {
                    byName.Launch = b[1]; byName.Target = shellItem ? null : b[1]; byName.Alias = alias;
                    if (b[1] == "ms-settings:") byName.Icon = SettingsIcon;
                    byName.Admin = IsConsole(b[1]) || b[1].EndsWith(".msc") || b[1].EndsWith("regedit.exe");
                    continue;
                }
                // same exe under another name: only add search words (never for Control.exe, several shortcuts use it)
                var byTarget = shellItem || Path.GetFileName(b[1]).Equals("control.exe", StringComparison.OrdinalIgnoreCase) ? null
                    : res.FirstOrDefault(x => x.Target != null && string.Equals(x.Target, b[1], StringComparison.OrdinalIgnoreCase));
                if (byTarget != null) { byTarget.Alias += " " + alias; byTarget.Admin |= IsConsole(b[1]) || b[1].EndsWith("regedit.exe"); continue; }
                seen.Add(key);
                res.Add(new AppEntry { Name = b[0], Launch = b[1], Target = shellItem ? null : b[1], Key = key, Alias = alias, Icon = b[1] == "ms-settings:" ? SettingsIcon : null, Admin = IsConsole(b[1]) || b[1].EndsWith(".msc") || b[1].EndsWith("regedit.exe") });
            }
        }

        static bool IsConsole(string target)
        {
            if (target == null) return false;
            string n = Path.GetFileName(target).ToLowerInvariant();
            return n == "cmd.exe" || n == "powershell.exe" || n == "pwsh.exe" || n == "wt.exe" || n == "taskmgr.exe";
        }

        /// English names / common words people type for Windows tools
        static string AliasFor(string target)
        {
            if (string.IsNullOrEmpty(target)) return "";
            switch (Path.GetFileName(target).ToLowerInvariant())
            {
                case "cmd.exe": return "cmd command prompt komut terminal konsol console";
                case "powershell.exe": case "pwsh.exe": return "powershell ps terminal konsol console shell";
                case "wt.exe": return "terminal windows terminal wt konsol console cmd powershell";
                case "taskmgr.exe": return "task manager taskmgr gorev yoneticisi islem process";
                case "control.exe": return "control panel denetim masasi";
                case "regedit.exe": return "regedit registry kayit defteri";
                case "devmgmt.msc": return "device manager aygit surucu driver";
                case "diskmgmt.msc": return "disk management bolum partition";
                case "services.msc": return "services hizmet servis";
                case "eventvwr.msc": return "event viewer olay log gunluk";
                case "resmon.exe": return "resource monitor kaynak izleyici";
                case "msconfig.exe": return "msconfig system configuration baslangic";
                case "msinfo32.exe": return "system information sistem bilgi";
                case "explorer.exe": return "explorer file explorer dosya gezgini klasor";
                case "notepad.exe": return "notepad not defteri";
                case "calc.exe": case "calculatorapp.exe": return "calculator hesap makinesi";
                case "mspaint.exe": return "paint resim";
                case "snippingtool.exe": return "snipping ekran alintisi screenshot";
                default: return "";
            }
        }

        /// best matches for a query (name prefix > word prefix > initials > substring)
        public static List<AppEntry> Match(string q, int max)
        {
            string f = Search.Fold(q.Trim());
            if (f.Length == 0) return new List<AppEntry>();
            var scored = new List<KeyValuePair<int, AppEntry>>();
            foreach (var a in Items)
            {
                int s = Score(a.Key, f);
                if (s == 0 && a.Alias.Length > 0)
                {
                    var words = a.Alias.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Any(w => w == f)) s = 90;                    // exact command: "cmd", "regedit"
                    else if (words.Any(w => w.StartsWith(f))) s = 70;
                    else if (f.Contains(" ") && a.Alias.Contains(f)) s = 60;  // "task manager"
                }
                if (s > 0) scored.Add(new KeyValuePair<int, AppEntry>(s - Math.Min(a.Key.Length, 40) / 4, a));
            }
            return scored.OrderByDescending(x => x.Key).Take(max).Select(x => x.Value).ToList();
        }

        static int Score(string name, string q)
        {
            if (name.StartsWith(q)) return 100;
            var words = name.Split(new[] { ' ', '-', '_', '.', '(' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => w.StartsWith(q))) return 80;
            if (q.Length >= 2) { var ini = new string(words.Select(w => w[0]).ToArray()); if (ini.StartsWith(q)) return 65; }
            if (name.Contains(q)) return 50;
            return 0;
        }
    }

    /// Files and folders through the Windows Search index (falls back to a bounded folder walk).
    static class FileIndex
    {
        public class Hit { public string Path, Name; public bool Folder; }
        static System.Data.OleDb.OleDbConnection cn;
        static System.Threading.Timer idleClose;
        static bool indexBroken;
        static readonly object gate = new object();

        public static List<Hit> Query(string text, int max, CancellationToken ct)
        {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 0).Take(5).ToArray();
            if (words.Length == 0) return new List<Hit>();
            if (!indexBroken)
            {
                try { return Indexed(words, max); }
                catch { indexBroken = true; try { if (cn != null) cn.Dispose(); } catch { } cn = null; }
            }
            return Walk(words, max, ct);
        }

        static string Esc(string w)
        {
            return w.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        }

        static List<Hit> Indexed(string[] words, int max)
        {
            lock (gate)
            {
                if (cn == null)
                {
                    cn = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
                    cn.Open();
                }
                if (idleClose == null) idleClose = new System.Threading.Timer(_ => { lock (gate) { if (cn != null) { try { cn.Dispose(); } catch { } cn = null; } } });
                idleClose.Change(30000, System.Threading.Timeout.Infinite);
                var sb = new StringBuilder("SELECT TOP " + max + " System.ItemUrl, System.FileName, System.ItemType FROM SYSTEMINDEX WHERE SCOPE='file:'");
                foreach (var w in words) sb.Append(" AND System.FileName LIKE '%" + Esc(w) + "%'");
                sb.Append(" ORDER BY System.DateModified DESC");
                var res = new List<Hit>();
                using (var cmd = new System.Data.OleDb.OleDbCommand(sb.ToString(), cn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string url = r[0] as string;
                        if (url == null || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                        string p = url.Substring(5).Replace('/', '\\');
                        if (p.IndexOf(@"\AppData\", StringComparison.OrdinalIgnoreCase) >= 0 && !p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
                        string type = r[2] as string;
                        res.Add(new Hit { Path = p, Name = r[1] as string ?? Path.GetFileName(p), Folder = type != null && type.Equals("Directory", StringComparison.OrdinalIgnoreCase) });
                    }
                }
                return res;
            }
        }

        static List<Hit> Walk(string[] words, int max, CancellationToken ct)
        {
            var res = new List<Hit>();
            var low = words.Select(w => w.ToLowerInvariant()).ToArray();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var q = new Queue<KeyValuePair<string, int>>();
            foreach (var d in new[] { "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "OneDrive" })
            {
                string p = Path.Combine(home, d);
                if (Directory.Exists(p)) q.Enqueue(new KeyValuePair<string, int>(p, 0));
            }
            var sw = Stopwatch.StartNew();
            while (q.Count > 0 && res.Count < max && sw.ElapsedMilliseconds < 1500 && !ct.IsCancellationRequested)
            {
                var cur = q.Dequeue();
                try
                {
                    foreach (var e in Directory.EnumerateFileSystemEntries(cur.Key))
                    {
                        string n = Path.GetFileName(e), nl = n.ToLowerInvariant();
                        bool dir = Directory.Exists(e);
                        if (low.All(w => nl.Contains(w))) { res.Add(new Hit { Path = e, Name = n, Folder = dir }); if (res.Count >= max) break; }
                        if (dir && cur.Value < 5 && !n.StartsWith(".") && n != "node_modules") q.Enqueue(new KeyValuePair<string, int>(e, cur.Value + 1));
                    }
                }
                catch { }
            }
            return res;
        }
    }

    /// Spotlight-like search: apps, files, folders, a calculator and web search.
    class SearchWindow : PanelWindow
    {
        class Result
        {
            public string Name, Sub, IconPath, Kind;
            public ImageSource Icon;
            public Action Open, OpenFolder, OpenAdmin;
            public Border Row;
        }

        TextBox box; TextBlock placeholder; StackPanel list; ScrollViewer scroll;
        readonly List<Result> results = new List<Result>();
        int sel = -1;
        bool catalogHooked;
        int version;
        CancellationTokenSource cts;
        DispatcherTimer debounce;

        public SearchWindow() : base(640, 470) { CenterOrigin = true; }

        public void OpenCentered()
        {
            AppCatalog.Load(Dispatcher);
            double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
            Place(Math.Round((sw - PW) / 2), Math.Round(sh * 0.17), false);
        }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            var g = new Grid { Margin = new Thickness(14, 12, 14, 12) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());

            var top = new Grid { Height = 48 };
            var glyph = Ui.Glyph("", 20, Ui.Br(t.Sub)); glyph.HorizontalAlignment = HorizontalAlignment.Left; glyph.Margin = new Thickness(10, 0, 0, 0);
            top.Children.Add(glyph);
            placeholder = Ui.Text("Uygulama, dosya veya klasör ara…", 20, FontWeights.Normal, Ui.Br(Ui.A(t.Fg, 0.45)));
            placeholder.Margin = new Thickness(44, 0, 0, 0); placeholder.IsHitTestVisible = false;
            top.Children.Add(placeholder);
            box = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Br(t.Fg), CaretBrush = Ui.Br(t.Fg),
                FontSize = 20, FontFamily = Ui.TextFont, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 8, 0),
                SelectionBrush = Ui.Br(t.Accent == Colors.White ? Color.FromRgb(120, 160, 255) : t.Accent)
            };
            box.TextChanged += (s, e) => OnText();
            box.PreviewKeyDown += OnKey;
            top.Children.Add(box);
            g.Children.Add(top);

            var line = new Border { Height = 1, Background = Ui.Br(Ui.A(t.Fg, 0.12)), Margin = new Thickness(4, 8, 4, 6) };
            Grid.SetRow(line, 1); g.Children.Add(line);

            list = new StackPanel();
            scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
            Grid.SetRow(scroll, 2); g.Children.Add(scroll);

            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
            debounce.Tick += (s, e) => { debounce.Stop(); QueryFiles(); };
            if (!catalogHooked) { catalogHooked = true; Action h = () => { if (IsOpen) OnText(); }; AppCatalog.Loaded += h; OnDestroy(() => AppCatalog.Loaded -= h); }
            OnText();
            return g;
        }

        public string Pending;   // text typed before the window existed (from the start panel)
        public void Append(string s)
        {
            if (box == null) { Pending = (Pending ?? "") + s; return; }
            box.Text += s; box.CaretIndex = box.Text.Length;
            if (!box.IsKeyboardFocused) { Activate(); box.Focus(); Keyboard.Focus(box); }
        }

        protected override void Opened()
        {
            box.Text = Pending ?? "";
            Pending = null;
            box.CaretIndex = box.Text.Length;
            box.Focus(); Keyboard.Focus(box);
            OnText();
            Dispatcher.BeginInvoke(new Action(() => { Activate(); box.Focus(); Keyboard.Focus(box); }), DispatcherPriority.Input);
        }

        protected override void Closed2() { if (cts != null) cts.Cancel(); if (debounce != null) debounce.Stop(); }

        public void DebugQuery(string q) { Dispatcher.BeginInvoke(new Action(() => { box.Text = q; box.CaretIndex = q.Length; }), DispatcherPriority.Input); }

        // ------------------------------------------------------------ query
        void OnText()
        {
            if (box == null) return;
            string q = box.Text;
            placeholder.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            version++;
            if (cts != null) cts.Cancel();
            results.Clear();
            if (q.Trim().Length == 0)
            {
                foreach (var p in App.S.Pinned.Take(8))
                {
                    var a = DockApp.FromPath(p);
                    results.Add(AppResult(a.Name, p, "Sabitlenmiş"));
                }
                Render("Sabitlenmiş uygulamalar", 0);
                var tip = Ui.Text("Alt + Boşluk ile her yerden aç  ·  ↑ ↓ seç  ·  Enter aç  ·  Ctrl + Enter konumu göster", 11.5, FontWeights.Normal, Ui.Br(Theme.Cur.Sub));
                tip.Margin = new Thickness(12, 18, 0, 0); tip.Opacity = 0.8;
                list.Children.Add(tip);
                return;
            }
            var calc = Calc(q);
            if (calc != null) results.Add(calc);
            foreach (var a in AppCatalog.Match(q, 6))
            {
                var r = AppResult(a.Name, a.Launch, a.Launch.StartsWith("shell:") || a.Launch.EndsWith(":") ? "Uygulama" : (a.Admin ? "Sistem aracı · Ctrl+Shift+Enter: yönetici olarak" : "Uygulama"), a.Icon);
                if (a.Admin) { string l = a.Launch; r.OpenAdmin = () => { try { Process.Start(new ProcessStartInfo(l) { UseShellExecute = true, Verb = "runas" }); } catch { } }; }
                results.Add(r);
            }
            appCount = results.Count;
            results.Add(WebResult(q));
            Render(null, 0);
            debounce.Stop(); debounce.Start();
        }
        int appCount;

        void QueryFiles()
        {
            string q = box.Text.Trim();
            if (q.Length < 2) return;
            int my = version;
            cts = new CancellationTokenSource();
            var tok = cts.Token;
            Task.Factory.StartNew(() =>
            {
                List<FileIndex.Hit> hits;
                try { hits = FileIndex.Query(q, 24, tok); } catch { hits = new List<FileIndex.Hit>(); }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (my != version || !IsOpen) return;
                    var files = hits.Select(FileResult).ToList();
                    results.InsertRange(appCount, files);
                    Render(null, sel < 0 ? 0 : sel);
                }));
            }, tok, TaskCreationOptions.None, TaskScheduler.Default);
        }

        Result AppResult(string name, string launch, string sub, string icon = null)
        {
            return new Result
            {
                Name = name, Sub = sub, IconPath = icon ?? launch, Kind = "Uygulamalar",
                Open = () => { try { Process.Start(new ProcessStartInfo(launch) { UseShellExecute = true }); } catch { } },
                OpenFolder = launch.StartsWith("shell:") || launch.EndsWith(":") ? (Action)null : () => Reveal(launch)
            };
        }

        Result FileResult(FileIndex.Hit h)
        {
            string dir = Path.GetDirectoryName(h.Path) ?? "";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (dir.StartsWith(home, StringComparison.OrdinalIgnoreCase)) dir = "~" + dir.Substring(home.Length);
            return new Result
            {
                Name = h.Name, Sub = dir, IconPath = h.Path, Kind = h.Folder ? "Klasörler ve dosyalar" : "Klasörler ve dosyalar",
                Open = () => { try { Process.Start(new ProcessStartInfo(h.Path) { UseShellExecute = true }); } catch { } },
                OpenFolder = () => Reveal(h.Path)
            };
        }

        Result WebResult(string q)
        {
            return new Result
            {
                Name = "“" + q.Trim() + "” için web'de ara", Sub = "Varsayılan tarayıcıda açılır", Kind = "Web", Icon = GlyphImage(""),
                Open = () => { try { Process.Start(new ProcessStartInfo("https://www.google.com/search?q=" + Uri.EscapeDataString(q.Trim())) { UseShellExecute = true }); } catch { } }
            };
        }

        static readonly Regex mathRx = new Regex(@"^[\d\s\.\,\+\-\*/\(\)%\^]+$");
        Result Calc(string q)
        {
            string e = q.Trim();
            if (!mathRx.IsMatch(e) || !Regex.IsMatch(e, @"\d\s*[\+\-\*/%\^]\s*[\d\(]")) return null;
            try
            {
                object v = new System.Data.DataTable().Compute(e.Replace(",", ".").Replace("^", "*"), null);
                double d = Convert.ToDouble(v);
                if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                string s = d.ToString("0.##########", System.Globalization.CultureInfo.CurrentCulture);
                return new Result { Name = "= " + s, Sub = "Hesap makinesi · Enter: kopyala", Kind = "Hesap", Icon = GlyphImage(""), Open = () => { try { Clipboard.SetText(s); } catch { } } };
            }
            catch { return null; }
        }

        static void Reveal(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        // ------------------------------------------------------------ render
        void Render(string header, int select)
        {
            var t = Theme.Cur;
            list.Children.Clear();
            string lastKind = null;
            if (header != null) list.Children.Add(Section(header));
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (header == null && r.Kind != lastKind) { list.Children.Add(Section(r.Kind)); lastKind = r.Kind; }
                list.Children.Add(r.Row = Row(r, i));
            }
            if (results.Count == 0 && header == null)
                list.Children.Add(Section("Sonuç yok"));
            sel = -1;
            Select(Math.Min(select, results.Count - 1), false);
        }

        UIElement Section(string s)
        {
            var tb = Ui.Text(s.ToUpper(Tr), 10.5, FontWeights.SemiBold, Ui.Br(Theme.Cur.Sub));
            tb.Margin = new Thickness(12, 8, 0, 4);
            return tb;
        }

        Border Row(Result r, int index)
        {
            var t = Theme.Cur;
            var g = new Grid { Height = 46 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var img = new Image { Width = 28, Height = 28, Source = r.Icon };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            g.Children.Add(img);
            if (r.Icon == null && r.IconPath != null)
            {
                string p = r.IconPath;
                Dispatcher.BeginInvoke(new Action(() => { r.Icon = Icons.ForPath(p, (int)(32 * scale)) ?? Icons.Letter(r.Name); img.Source = r.Icon; }), DispatcherPriority.Background);
            }
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var n = Ui.Text(r.Name, 14, FontWeights.SemiBold, Ui.Br(t.Fg)); n.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(n);
            if (!string.IsNullOrEmpty(r.Sub))
            {
                var s = Ui.Text(r.Sub, 11.5, FontWeights.Normal, Ui.Br(t.Sub)); s.TextTrimming = TextTrimming.CharacterEllipsis;
                texts.Children.Add(s);
            }
            Grid.SetColumn(texts, 1); g.Children.Add(texts);
            var hint = Ui.Text(r.OpenFolder != null ? "↵ aç   ⌃↵ konum" : "↵", 11, FontWeights.Normal, Ui.Br(t.Sub));
            hint.Margin = new Thickness(8, 0, 12, 0); hint.Opacity = 0; hint.Tag = "hint";
            Grid.SetColumn(hint, 2); g.Children.Add(hint);
            var b = new Border { Child = g, CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = hint };
            b.MouseEnter += (s, e) => Select(index, false);
            b.MouseLeftButtonUp += (s, e) => Run(index, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            return b;
        }

        void Select(int i, bool scrollTo)
        {
            if (i == sel) return;
            var t = Theme.Cur;
            if (sel >= 0 && sel < results.Count && results[sel].Row != null)
            {
                var old = results[sel].Row;
                old.Background = Brushes.Transparent;
                ((UIElement)old.Tag).Opacity = 0;
            }
            sel = i;
            if (sel < 0 || sel >= results.Count || results[sel].Row == null) return;
            var row = results[sel].Row;
            var hb = new SolidColorBrush(t.Dark ? Color.FromArgb(46, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0)) { Opacity = 0 };
            row.Background = hb;
            hb.BeginAnimation(Brush.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            ((UIElement)row.Tag).Opacity = 1;
            if (scrollTo) row.BringIntoView();
        }

        void Run(int i, bool folder)
        {
            if (i < 0 || i >= results.Count) return;
            var r = results[i];
            CloseAnimated();
            bool admin = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (admin && r.OpenAdmin != null) r.OpenAdmin();
            else if (folder && r.OpenFolder != null) r.OpenFolder(); else if (r.Open != null) r.Open();
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Down) { Select(Math.Min(results.Count - 1, sel + 1), true); e.Handled = true; }
            else if (e.Key == Key.Up) { Select(Math.Max(0, sel - 1), true); e.Handled = true; }
            else if (e.Key == Key.Enter) { Run(sel < 0 ? 0 : sel, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); e.Handled = true; }
        }

        static ImageSource GlyphImage(string glyph)
        {
            var t = Theme.Cur;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Ui.A(t.Fg, 0.14)), null, new Rect(0, 0, 56, 56), 14, 14);
                var ft = new FormattedText(glyph, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(Ui.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 28, new SolidColorBrush(t.Fg), 1.0);
                dc.DrawText(ft, new Point(28 - ft.Width / 2, 28 - ft.Height / 2));
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(56, 56, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            return rtb;
        }
    }

    /// Lets the user pin any installed application (desktop or Store) to the dock.
    class AppPickerPanel : PanelWindow
    {
        TextBox box; StackPanel list; TextBlock status;
        readonly Dictionary<AppEntry, Border> rows = new Dictionary<AppEntry, Border>();

        bool catalogHooked;
        public AppPickerPanel() : base(400, 560) { }

        protected override UIElement Build()
        {
            var t = Theme.Cur;
            var g = new Grid { Margin = new Thickness(18, 16, 18, 14) };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(Header("Dock'a uygulama ekle"));

            var sb = new Grid { Height = 36, Margin = new Thickness(0, 0, 0, 8) };
            sb.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Ui.Br(Ui.A(t.Fg, 0.08)), BorderBrush = Ui.Br(Ui.A(t.Fg, 0.14)), BorderThickness = new Thickness(1) });
            var gl = Ui.Glyph("", 14, Ui.Br(t.Sub)); gl.HorizontalAlignment = HorizontalAlignment.Left; gl.Margin = new Thickness(12, 0, 0, 0);
            sb.Children.Add(gl);
            box = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Ui.Br(t.Fg), CaretBrush = Ui.Br(t.Fg), FontSize = 14, FontFamily = Ui.TextFont, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(34, 0, 10, 0) };
            box.TextChanged += (s, e) => Filter();
            sb.Children.Add(box);
            Grid.SetRow(sb, 1); g.Children.Add(sb);

            list = new StackPanel();
            var sv = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
            Grid.SetRow(sv, 2); g.Children.Add(sv);

            var foot = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            status = Ui.Text("", 11.5, FontWeights.Normal, Ui.Br(t.Sub));
            foot.Children.Add(status);
            var bt = Ui.Text("Gözat…", 13, FontWeights.SemiBold, Ui.Br(t.Fg)); bt.HorizontalAlignment = HorizontalAlignment.Center;
            var browse = Ui.Button(bt, Browse, "Herhangi bir .exe veya kısayol seç", 96, 34, 17);
            browse.HorizontalAlignment = HorizontalAlignment.Right;
            browse.Background = Ui.Br(Ui.A(t.Fg, 0.08)); browse.BorderBrush = Ui.Br(Ui.A(t.Fg, 0.16)); browse.BorderThickness = new Thickness(1);
            foot.Children.Add(browse);
            Grid.SetRow(foot, 3); g.Children.Add(foot);

            rows.Clear();
            if (!catalogHooked) { catalogHooked = true; Action h = () => { if (list != null) Fill(); }; AppCatalog.Loaded += h; OnDestroy(() => AppCatalog.Loaded -= h); }
            Fill();
            return g;
        }

        void Fill()
        {
            list.Children.Clear(); rows.Clear();
            pinnedTargets.Clear();
            foreach (var p in App.S.Pinned) { pinnedTargets.Add(p); var tg = Shortcut.Target(p); if (tg != null) pinnedTargets.Add(tg); }
            if (!AppCatalog.Ready) { status.Text = "Uygulamalar taranıyor…"; AppCatalog.Load(Dispatcher); return; }
            foreach (var a in AppCatalog.Items)
            {
                var r = Row(a);
                rows[a] = r;
                list.Children.Add(r);
            }
            status.Text = AppCatalog.Items.Count + " uygulama";
            Filter();
        }

        Border Row(AppEntry a)
        {
            var t = Theme.Cur;
            var g = new Grid { Height = 42 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            var img = new Image { Width = 26, Height = 26 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            g.Children.Add(img);
            var n = Ui.Text(a.Name, 13.5, FontWeights.Normal, Ui.Br(t.Fg)); n.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(n, 1); g.Children.Add(n);
            bool pinned = IsPinned(a);
            var mark = Ui.Glyph(pinned ? "" : "", 13, Ui.Br(pinned ? t.Accent : t.Sub));
            Grid.SetColumn(mark, 2); g.Children.Add(mark);
            var b = Ui.Button(g, () =>
            {
                if (IsPinned(a)) return;
                App.Dock.PinPath(a.Launch);
                pinnedTargets.Add(a.Launch);
                if (a.Target != null) pinnedTargets.Add(a.Target.ToLowerInvariant());
                mark.Text = ""; mark.Foreground = Ui.Br(t.Accent);
                status.Text = a.Name + " dock'a eklendi";
            }, null, 0, 0, 12);
            b.Margin = new Thickness(0, 1, 6, 1);
            bool loaded = false;
            b.Loaded += (s, e) =>
            {
                if (loaded) return; loaded = true;
                Dispatcher.BeginInvoke(new Action(() => img.Source = Icons.ForPath(a.Launch, (int)(32 * scale)) ?? Icons.Letter(a.Name)), DispatcherPriority.Background);
            };
            return b;
        }

        readonly HashSet<string> pinnedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool IsPinned(AppEntry a)
        {
            return pinnedTargets.Contains(a.Launch) || (a.Target != null && pinnedTargets.Contains(a.Target));
        }

        void Filter()
        {
            string f = Search.Fold(box == null ? "" : box.Text.Trim());
            foreach (var kv in rows)
                kv.Value.Visibility = f.Length == 0 || kv.Key.Key.Contains(f) ? Visibility.Visible : Visibility.Collapsed;
        }

        void Browse()
        {
            CloseAnimated();
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Dock'a eklenecek uygulamayı seç", Filter = "Uygulamalar ve kısayollar|*.exe;*.lnk;*.bat;*.cmd;*.url|Tüm dosyalar|*.*" };
            if (d.ShowDialog() == true) App.Dock.PinPath(d.FileName);
        }

        protected override void Opened()
        {
            if (box != null) { box.Text = ""; Dispatcher.BeginInvoke(new Action(() => box.Focus()), DispatcherPriority.Input); }
        }
    }

    static class Search
    {
        /// lowercase + Turkish letters folded, for forgiving matching ("ışık" ~ "isik", "WINDOWS" ~ "windows")
        public static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c0 in s)
            {
                char c = c0;
                switch (c)
                {
                    case 'İ': case 'I': case 'ı': c = 'i'; break;
                    case 'Ş': case 'ş': c = 's'; break;
                    case 'Ğ': case 'ğ': c = 'g'; break;
                    case 'Ü': case 'ü': c = 'u'; break;
                    case 'Ö': case 'ö': c = 'o'; break;
                    case 'Ç': case 'ç': c = 'c'; break;
                    default: c = char.ToLowerInvariant(c); break;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
