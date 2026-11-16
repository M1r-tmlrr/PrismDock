using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PrismDock
{
    /// Mica-style backdrop: the desktop wallpaper, decoded small and blurred once.
    /// Costs ~1 MB RAM and zero CPU while idle.
    static class Wallpaper
    {
        public static BitmapSource Blurred;

        public static void Load()
        {
            Blurred = null;
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (!File.Exists(path))
                {
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                        path = k != null ? k.GetValue("WallPaper") as string : null;
                }
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                string style = "10";
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    if (k != null && k.GetValue("WallpaperStyle") != null) style = k.GetValue("WallpaperStyle").ToString();

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = 640;
                using (var fs = File.OpenRead(path)) { bi.StreamSource = fs; bi.EndInit(); }
                bi.Freeze();

                double sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
                int W = 480, H = (int)Math.Round(480 * sh / sw);
                double iw = bi.PixelWidth, ih = bi.PixelHeight;
                Rect dest;
                if (style == "2") dest = new Rect(0, 0, W, H); // stretch
                else
                {
                    // fill (default): cover & center-crop
                    double s = Math.Max(W / iw, H / ih);
                    dest = new Rect((W - iw * s) / 2, (H - ih * s) / 2, iw * s, ih * s);
                }
                const double pad = 24;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // draw oversized so the blur has real pixels at the borders
                    var big = new Rect(dest.X - pad, dest.Y - pad, dest.Width + pad * 2, dest.Height + pad * 2);
                    dc.DrawImage(bi, big);
                }
                dv.Effect = new BlurEffect { Radius = 14, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
                var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                Blurred = rtb;
            }
            catch { Blurred = null; }
        }
    }
}
