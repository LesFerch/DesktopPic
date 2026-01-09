using Microsoft.Win32;
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopPic
{
    class Program
    {
        static string SystemRoot = Environment.ExpandEnvironmentVariables("%SystemRoot%");

        static string myPath = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
        static string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string myIniFile;

        static string GetWritableAppDataPath()
        {
            string portableAppData = Path.Combine(myPath, "AppData");
            string testFile = Path.Combine(myPath, Guid.NewGuid().ToString("N") + ".tmp");
            bool isWritable = false;

            try
            {
                // Try to create and delete a temp file in the exe folder
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
                isWritable = true;
            }
            catch
            {
                isWritable = false;
            }

            string chosenAppData = isWritable ? portableAppData : Path.Combine(LocalAppData, "DesktopPic");

            // Ensure the folder exists
            try
            {
                if (!Directory.Exists(chosenAppData))
                    Directory.CreateDirectory(chosenAppData);
            }
            catch
            {
                // Fallback to LocalAppData if creation fails
                chosenAppData = Path.Combine(LocalAppData, "DesktopPic");
                if (!Directory.Exists(chosenAppData))
                    Directory.CreateDirectory(chosenAppData);
            }

            return Path.Combine(chosenAppData, "DesktopPic.ini");
        }

        [STAThread]
        static void Main(string[] args)
        {
            string NTkey = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion";
            int buildNumber = int.Parse(Registry.GetValue(NTkey, "CurrentBuild", "").ToString());

            if (buildNumber < 10000)
            {
                MessageBox.Show("Windows 10 or higher required", "DesktopPic");
                return;
            }

            UnblockPath(myPath);

            myIniFile = GetWritableAppDataPath();

            string viewer = ReadString(myIniFile, "Options", "Viewer", "EXE");
            string exe = $"\"{myPath}\\AppParts\\DesktopPicViewer.exe\"";
            string arg = "";
            if (viewer == "hta")
            {
                exe = $"{SystemRoot}\\System32\\mshta.exe";
                arg = $"\"{myPath}\\AppParts\\DesktopPicViewer.hta\"";
            }

            try
            {
                Process.Start(exe, arg);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to launch:\n\n{exe}\n\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Environment.Exit(1);
            }

        }
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteFile(string name);

        public static void UnblockPath(string path)
        {
            string[] files = System.IO.Directory.GetFiles(path);
            string[] dirs = System.IO.Directory.GetDirectories(path);
            foreach (string file in files)
            {
                UnblockFile(file);
            }
            foreach (string dir in dirs)
            {
                UnblockPath(dir);
            }
        }
        public static bool UnblockFile(string fileName)
        {
            return DeleteFile(fileName + ":Zone.Identifier");
        }

        static string ReadString(string iniFile, string section, string key, string defaultValue)
        {
            try
            {
                if (File.Exists(iniFile))
                {
                    return IniFileParser.ReadValue(section, key, defaultValue, iniFile);
                }
            }
            catch { }

            return defaultValue;
        }

        public static class IniFileParser
        {
            public static string ReadValue(string section, string key, string defaultValue, string filePath)
            {
                try
                {
                    var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                    string currentSection = null;

                    foreach (var line in lines)
                    {
                        string trimmedLine = line.Trim();

                        if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                        {
                            currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                        }
                        else if (currentSection == section)
                        {
                            var parts = trimmedLine.Split(new char[] { '=' }, 2);
                            if (parts.Length == 2 && parts[0].Trim() == key)
                            {
                                return parts[1].Trim().ToLower();
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
                return defaultValue;
            }
        }

    }
}
