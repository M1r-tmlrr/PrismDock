using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using Screen = System.Windows.Forms.Screen;

namespace PrismDock
{
    /// Monitor geometry in WPF units (DIP). The app is system-DPI aware, so one scale applies to every screen.
    static class Mon
    {
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
        static double scale;

        public static double Scale
        {
            get
            {
                if (scale == 0)
                {
                    IntPtr dc = Native.GetDC(IntPtr.Zero);
                    scale = Math.Max(1, GetDeviceCaps(dc, 88 /*LOGPIXELSX*/)) / 96.0;
                    Native.ReleaseDC(IntPtr.Zero, dc);
                }
                return scale;
            }
        }

        public static Rect Dip(System.Drawing.Rectangle r)
        {
            double k = Scale;
            return new Rect(r.X / k, r.Y / k, r.Width / k, r.Height / k);
        }

        /// bounds of the monitor that contains a point given in DIP
        public static Rect BoundsAt(double x, double y)
        {
            double k = Scale;
            return Dip(Screen.FromPoint(new System.Drawing.Point((int)Math.Round(x * k), (int)Math.Round(y * k))).Bounds);
        }

        public static Screen Find(string device)
        {
            return Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device);
        }
    }
}
