using System;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace GetScreenInfo
{
    class Program
    {
        static void Main(string[] args)
        {
            RegistryKey Software = Registry.CurrentUser.CreateSubKey("Software");

            using (RegistryKey RegKey = Software.CreateSubKey("DesktopPic"))
            {
                // Get screen information
                string screensInfo = string.Join(";", Screen.AllScreens.Select((screen, index) =>
                {
                    var rect = GetActualWorkingArea(screen, index + 1);
                    float scale = GetScalingFactor(index + 1);
                    string color = GetFirstPixelColor(index + 1);
                    return $"{scale},{rect.Left},{rect.Top},{rect.Right},{rect.Bottom},{color}";
                }));

                RegKey.SetValue("Screens", screensInfo);
            }
        }

        private static Rectangle GetActualWorkingArea(Screen screen, int displayNumber)
        {
            // Get the working area of the screen
            int left = screen.WorkingArea.Left;
            int top = screen.WorkingArea.Top;
            int right = screen.WorkingArea.Right;
            int bottom = screen.WorkingArea.Bottom;

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private const int MDT_EFFECTIVE_DPI = 0;

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(
            IntPtr hmonitor,
            int dpiType,
            out uint dpiX,
            out uint dpiY);

        private static float GetScalingFactor(int displayNumber)
        {
            if (displayNumber < 1 || displayNumber > Screen.AllScreens.Length)
                return 1.0f;

            var screen = Screen.AllScreens[displayNumber - 1];
            IntPtr hMonitor = NativeMethods.MonitorFromPoint(
                new NativeMethods.POINT(screen.Bounds.Left + 1, screen.Bounds.Top + 1),
                NativeMethods.MONITOR_DEFAULTTONEAREST);

            if (hMonitor != IntPtr.Zero)
            {
                if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
                {
                    return dpiX / 96.0f;
                }
            }
            return 1.0f;
        }

        // Helper for MonitorFromPoint
        private static class NativeMethods
        {
            public const int MONITOR_DEFAULTTONEAREST = 2;

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X;
                public int Y;
                public POINT(int x, int y) { X = x; Y = y; }
            }

            [DllImport("User32.dll")]
            public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);

        public static string GetFirstPixelColor(int displayNumber)
        {
            if (displayNumber < 1 || displayNumber > Screen.AllScreens.Length)
                return "";

            var screen = Screen.AllScreens[displayNumber - 1];
            int x = screen.Bounds.Left + 1;
            int y = screen.Bounds.Top + 1;

            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return "";

            uint colorRef = GetPixel(hdc, x, y);
            ReleaseDC(IntPtr.Zero, hdc);

            int r = (int)(colorRef & 0xFF);
            int g = (int)((colorRef >> 8) & 0xFF);
            int b = (int)((colorRef >> 16) & 0xFF);

            string hex = $"#{r:X2}{g:X2}{b:X2}";

            return hex;
        }
    }
}
