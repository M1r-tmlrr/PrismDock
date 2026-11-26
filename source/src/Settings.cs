using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PrismDock
{
    class Settings
    {
        public string Theme = "liquid";
        public bool Top = false;
        public bool FullWidth = false;
        public bool HideTaskbar = true;
        public bool Reserve = true;
        public bool Magnify = true;
        public bool Seconds = false;
        public bool AutoStart = false;
        public int IconSize = 40;
        public bool LiveGlass = true;
        public bool SearchHotkey = true;
        public bool WinKeyMenu = true;
        public bool Island = true;
        public bool AllMonitors = true;
        public bool AutoHide = true;
        public int GlassLight = 60;     // % of the original specular light / rim (100 = v2.0 look)
        public int Refraction = 100;    // % strength of lens bending
        public bool PinsInit = false;
        public List<string> Pinned = new List<string>();
        // taskbar state saved so we can always restore it
        public bool TaskbarTouched = false;
        public int TaskbarPrevState = 0;

        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismDock");
                Directory.CreateDirectory(d);
                return d;
            }
        }
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    int i = raw.IndexOf('=');
                    if (i <= 0) continue;
                    string k = raw.Substring(0, i).Trim().TrimStart('\uFEFF'), v = raw.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "theme": s.Theme = v; break;
                        case "top": s.Top = v == "1"; break;
                        case "fullwidth": s.FullWidth = v == "1"; break;
                        case "hidetaskbar": s.HideTaskbar = v == "1"; break;
                        case "reserve": s.Reserve = v == "1"; break;
                        case "magnify": s.Magnify = v == "1"; break;
                        case "seconds": s.Seconds = v == "1"; break;
                        case "autostart": s.AutoStart = v == "1"; break;
                        case "liveglass": s.LiveGlass = v == "1"; break;
                        case "searchhotkey": s.SearchHotkey = v == "1"; break;
                        case "winkey": s.WinKeyMenu = v == "1"; break;
                        case "island": s.Island = v == "1"; break;
                        case "allmonitors": s.AllMonitors = v == "1"; break;
                        case "autohide": s.AutoHide = v == "1"; break;
                        case "glasslight": int.TryParse(v, out s.GlassLight); break;
                        case "refraction": int.TryParse(v, out s.Refraction); break;
                        case "iconsize": int.TryParse(v, out s.IconSize); break;
                        case "pinsinit": s.PinsInit = v == "1"; break;
                        case "pin": if (v.Length > 0) s.Pinned.Add(v); break;
                        case "tbtouched": s.TaskbarTouched = v == "1"; break;
                        case "tbprev": int.TryParse(v, out s.TaskbarPrevState); break;
                    }
                }
                if (s.IconSize < 28 || s.IconSize > 64) s.IconSize = 40;
                s.GlassLight = Math.Max(0, Math.Min(150, s.GlassLight)); s.Refraction = Math.Max(0, Math.Min(200, s.Refraction));
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var l = new List<string>();
                l.Add("theme=" + Theme);
                l.Add("top=" + B(Top));
                l.Add("fullwidth=" + B(FullWidth));
                l.Add("hidetaskbar=" + B(HideTaskbar));
                l.Add("reserve=" + B(Reserve));
                l.Add("magnify=" + B(Magnify));
                l.Add("seconds=" + B(Seconds));
                l.Add("autostart=" + B(AutoStart));
                l.Add("iconsize=" + IconSize);
                l.Add("liveglass=" + B(LiveGlass));
                l.Add("searchhotkey=" + B(SearchHotkey));
                l.Add("winkey=" + B(WinKeyMenu));
                l.Add("island=" + B(Island));
                l.Add("allmonitors=" + B(AllMonitors));
                l.Add("autohide=" + B(AutoHide));
                l.Add("glasslight=" + GlassLight);
                l.Add("refraction=" + Refraction);
                l.Add("pinsinit=" + B(PinsInit));
                l.Add("tbtouched=" + B(TaskbarTouched));
                l.Add("tbprev=" + TaskbarPrevState);
                foreach (var p in Pinned) l.Add("pin=" + p);
                File.WriteAllLines(FilePath, l.ToArray());
            }
            catch { }
        }

        static string B(bool b) { return b ? "1" : "0"; }
    }
}
