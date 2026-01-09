using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GetMonitor
{
    class Program
    {
        [STAThread]
        static void Main()
        {
            // Global cursor position
            Point global = Cursor.Position;

            // Find the monitor that contains the cursor
            Screen screen = Screen.FromPoint(global);

            // Monitor index (0-based)
            int index = Array.IndexOf(Screen.AllScreens, screen);

            RegistryKey Software = Registry.CurrentUser.CreateSubKey("Software");

            using (RegistryKey RegKey = Software.CreateSubKey("DesktopPic"))
            {
                RegKey.SetValue("Stamp", $"{index},{screen.Bounds.Width},{screen.Bounds.Height}");
            }
        }
    }
}
