using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfAnimatedGif;
using System.Windows.Media.Animation;
using System.Windows.Forms;
using System.Text;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace DesktopPicViewer
{
    public partial class MainWindow : Window
    {
        private string[] _imageFiles = Array.Empty<string>(); // Initialize with an empty array
        private DispatcherTimer _slideshowTimer = new DispatcherTimer(); // Initialize with a new instance
        private int _currentImageIndex = 0; // Track the current image index
        private bool _isPaused = false; // Track the pause state
        private TaskCompletionSource<bool> _imageUpdateTcs; // TaskCompletionSource to track image update completion

        static string SystemRoot = Environment.ExpandEnvironmentVariables("%SystemRoot%");
        static string myPath = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
        static string myExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        static string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string myIniFile;

        static void ParseCoordinates(string coordinates, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (!string.IsNullOrWhiteSpace(coordinates))
            {
                var parts = coordinates.Split(',');
                if (parts.Length > 0)
                {
                    int.TryParse(parts[0].Trim(), out x);
                }
                if (parts.Length > 1)
                {
                    int.TryParse(parts[1].Trim(), out y);
                }
            }
        }

        static string GetWritableAppDataPath()
        {
            string portableAppData = Path.GetFullPath(Path.Combine(myPath, "..\\AppData"));
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

        static string picPath;
        static int PicDelay;
        static double MaxScrPct;
        static int MonitorIndex;
        static int LocIdx;
        static bool Stretch;
        static bool ShowFileName;
        static int FadeEffect;
        static int FadeEffectSetting;
        static int PicPadding;
        static string Coordinates;
        static int x = 0;
        static int y = 0;

        static float scale;

        private const int MDT_EFFECTIVE_DPI = 0;

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        public MainWindow()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                string logPath = Path.Combine(Path.GetDirectoryName(myIniFile), "error.log");
                File.AppendAllText(logPath, $"Unhandled: {e.ExceptionObject}\n");
            };

            string NTkey = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion";
            int buildNumber = int.Parse(Registry.GetValue(NTkey, "CurrentBuild", "").ToString());

            if (buildNumber < 10000)
            {
                System.Windows.MessageBox.Show("Windows 10 or higher required", "DesktopPic");
                System.Windows.Application.Current.Shutdown();
                return;
            }

            myIniFile = GetWritableAppDataPath();

            picPath = ReadString(myIniFile, "Options", "PicPath", Path.GetFullPath($@"{myPath}\..\Examples\Transparent"));
            Stretch = ReadString(myIniFile, "Options", "Stretch", "false") == "true";
            ShowFileName = ReadString(myIniFile, "Options", "ShowFileName", "false") == "true";
            FadeEffect = int.Parse(ReadString(myIniFile, "Options", "FadeEffect", "2"));
            PicPadding = int.Parse(ReadString(myIniFile, "Options", "Padding", "0"));
            PicDelay = int.Parse(ReadString(myIniFile, "Options", "PicDelay", "5"));
            MaxScrPct = double.Parse(ReadString(myIniFile, "Options", "MaxScrPct", "25"));
            MonitorIndex = int.Parse(ReadString(myIniFile, "Options", "MonitorIndex", "0"));
            LocIdx = int.Parse(ReadString(myIniFile, "Options", "LocIdx", "0"));
            Coordinates = ReadString(myIniFile, "Options", "Coordinates", "");

            FadeEffectSetting = FadeEffect;

            ParseCoordinates(Coordinates, out x, out y);

            if (MonitorIndex < 0 || MonitorIndex >= Screen.AllScreens.Length) MonitorIndex = 0;

            InitializeComponent();
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

            if (!Directory.Exists(picPath)) picPath = Path.GetFullPath($@"{myPath}\..\Examples\Landscape");
            if (!Directory.Exists(picPath)) picPath = Path.GetFullPath($@"{myPath}\..\Examples\Portrait");
            if (!Directory.Exists(picPath)) picPath = Path.GetFullPath($@"{myPath}\..\Examples\Animated");
            if (!Directory.Exists(picPath)) RunGUIandExit();

            var extensionOrder = new[] { ".bmp", ".gif", ".jpg", ".jpeg", ".heic", ".heif", ".png", ".tif", ".tiff", ".webp" };
            _imageFiles = Directory.GetFiles(picPath, "*.*")
                .Where(f => extensionOrder.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Array.IndexOf(extensionOrder, Path.GetExtension(f).ToLowerInvariant()))
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                int idx;
                if (int.TryParse(args[1], out idx) && idx >= 0 && idx < _imageFiles.Length)
                {
                    _currentImageIndex = idx;
                }
                else
                {
                    _currentImageIndex = 0;
                }
            }
            else
            {
                _currentImageIndex = 0;
            }

            if (_imageFiles.Length == 0) RunGUIandExit();

            StartSlideshow();
        }

        private void StartSlideshow()
        {
            // Implementation of the slideshow logic
            _slideshowTimer.Interval = TimeSpan.FromSeconds(PicDelay);
            _slideshowTimer.Tick += OnSlideshowTick;
            _slideshowTimer.Start();
            UpdateImage(); // Display the first image immediately
        }

        private void OnSlideshowTick(object sender, EventArgs e)
        {
            _currentImageIndex = (_currentImageIndex + 1) % _imageFiles.Length;
            UpdateImage();
        }

        private void SetWindowSizePosition(string imagePath)
        {
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath);
            bitmap.EndInit();

            double iw = bitmap.PixelWidth;
            double ih = bitmap.PixelHeight;

            var screen = Screen.AllScreens[MonitorIndex];
            double sw = screen.WorkingArea.Width;
            double sh = screen.WorkingArea.Height;

            double maxArea = sw * sh * (MaxScrPct / 100);
            double imgArea = iw * ih;
            double w = iw;
            double h = ih;
            double resizeFactor = Math.Sqrt(maxArea / imgArea);

            w = iw * resizeFactor;
            h = ih * resizeFactor;

            if (w + PicPadding > sw)
            {
                h = (sw - PicPadding) / w * h;
                w = sw - PicPadding;
            }

            if (h + PicPadding > sh)
            {
                w = (sh - PicPadding) / h * w;
                h = sh - PicPadding;
            }

            if (!Stretch && w > iw)
            {
                w = iw;
                h = ih;
            }

            double left = screen.WorkingArea.Left;
            double top = screen.WorkingArea.Top;

            switch (LocIdx)
            {
                case 1: //Top-Left
                    left = screen.WorkingArea.Left + PicPadding;
                    top = screen.WorkingArea.Top + PicPadding;
                    break;
                case 2: //Top-Right
                    left = screen.WorkingArea.Right - w - PicPadding;
                    top = screen.WorkingArea.Top + PicPadding;
                    break;
                case 3: //Bottom-Left
                    left = screen.WorkingArea.Left + PicPadding;
                    top = screen.WorkingArea.Bottom - h - PicPadding;
                    break;
                case 4: //Bottom-Right
                    left = screen.WorkingArea.Right - w - PicPadding;
                    top = screen.WorkingArea.Bottom - h - PicPadding;
                    break;
                case 5: //Custom
                    left = x;
                    top = y;
                    break;
                case 0: //Center
                default:
                    left = screen.WorkingArea.Left + (screen.WorkingArea.Width - w) / 2;
                    top = screen.WorkingArea.Top + (screen.WorkingArea.Height - h) / 2;
                    break;
            }

            scale = GetCurrentDisplayScale();

            w /= scale;
            h /= scale;
            left /= scale;
            top /= scale;

            this.Width = w;
            this.Height = h;
            this.Left = left;
            this.Top = top;
        }

        private CancellationTokenSource _fadeCancellationTokenSource;

        private bool _isUpdatingImage = false;

        private async void UpdateImage()
        {
            if (_isUpdatingImage)
                return;

            _isUpdatingImage = true;
            var tcs = new TaskCompletionSource<bool>();
            _imageUpdateTcs = tcs;

            try
            {
                ImageBehavior.SetAnimatedSource(SlideshowImage, null);
                ImageBehavior.SetAnimatedSource(SlideshowImageNext, null);

                string currentImagePath = _imageFiles[_currentImageIndex];
                string fileName = Path.GetFileName(currentImagePath);

                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(currentImagePath);
                bitmap.EndInit();

                // Set the window size based on the current image
                SetWindowSizePosition(currentImagePath);

                // Set filename overlay
                FileNameText.Text = fileName;
                FileNameText.Visibility = ShowFileName ? Visibility.Visible : Visibility.Collapsed;

                bool gif = currentImagePath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

                if (FadeEffect > 0 && !gif)
                {
                    SlideshowImageNext.Source = bitmap;

                    // Start crossfade animation
                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(FadeEffect));
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(FadeEffect));

                    SlideshowImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    SlideshowImageNext.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                    _fadeCancellationTokenSource = new CancellationTokenSource();
                    try
                    {
                        await Task.Delay(FadeEffect * 1000, _fadeCancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        // Fade was interrupted
                    }
                    finally
                    {
                        _fadeCancellationTokenSource = null;
                    }

                    SlideshowImage.Source = bitmap;
                }
                else
                {
                    // No fade, just set image
                    if (gif) ImageBehavior.SetAnimatedSource(SlideshowImage, bitmap); else SlideshowImage.Source = bitmap;
                    if (gif) ImageBehavior.SetAnimatedSource(SlideshowImageNext, bitmap); else SlideshowImageNext.Source = bitmap;
                }

                this.Focus();
                Keyboard.Focus(this);
            }
            finally
            {
                if (!tcs.Task.IsCompleted)
                    tcs.SetResult(true);
                _isUpdatingImage = false;
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

        public float GetCurrentDisplayScale()
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            IntPtr hMonitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST = 2
            if (hMonitor != IntPtr.Zero)
            {
                if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
                {
                    return dpiX / 96.0f;
                }
            }
            return 1.0f;
        }


        private async void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) PauseSlideshow();

            // Cancel fade effect for all keys
            if (_fadeCancellationTokenSource != null) _fadeCancellationTokenSource.Cancel();

            if (_imageUpdateTcs != null)
            {
                await _imageUpdateTcs.Task; // Wait for the image update to complete
            }

            switch (e.Key)
            {
                case Key.Escape:
                    RunGUIandExit();
                    break;
                case Key.Right:
                case Key.Down:
                    FadeEffect = 0;
                    ShowNextImage();
                    break;
                case Key.Left:
                case Key.Up:
                    FadeEffect = 0;
                    ShowPreviousImage();
                    break;
                case Key.Space:
                    PauseSlideshow();
                    break;
                case Key.Enter:
                    FadeEffect = FadeEffectSetting;
                    ShowNextImage();
                    ResumeSlideshow();
                    break;
            }
        }

        private void RunGUIandExit()
        {
            string exe = $"{SystemRoot}\\System32\\mshta.exe";

            try
            {
                Process.Start(exe, $"\"{myPath}\\DesktopPicGUI.hta\" \"{_currentImageIndex}\" \"{picPath}\" \"{myExe}\"");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to launch:\n\n{exe}\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            this.Close();
        }

        private void MainWindow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            RunGUIandExit();
        }

        private void ImageClicked(object sender, MouseButtonEventArgs e)
        {
            // Logic for handling image click

            if (_isPaused)
            {
                ResumeSlideshow();
            }
            else
            {
                PauseSlideshow();
            }
        }

        private void ShowNextImage()
        {
            _currentImageIndex = (_currentImageIndex + 1) % _imageFiles.Length;
            UpdateImage();
        }

        private void ShowPreviousImage()
        {
            _currentImageIndex = (_currentImageIndex - 1 + _imageFiles.Length) % _imageFiles.Length;
            UpdateImage();
        }

        private void PauseSlideshow()
        {
            if (!_isPaused)
            {
                _slideshowTimer.Stop();
                _isPaused = true;
            }
        }

        private void ResumeSlideshow()
        {
            if (_isPaused)
            {
                _slideshowTimer.Start();
                _isPaused = false;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Set the initial window size based on the current image
            SetWindowSizePosition(_imageFiles[_currentImageIndex]);

            this.Focus(); // Ensure the window is focused when loaded
            Keyboard.Focus(this); // Set keyboard focus to the window
        }

        static string ReadString(string iniFile, string section, string key, string defaultValue)
        {
            try
            {
                if (File.Exists(iniFile))
                {
                    var value = IniFileParser.ReadValue(section, key, defaultValue, iniFile);
                    if (string.IsNullOrEmpty(value))
                        return defaultValue;

                    // If defaultValue is an int, only accept int values
                    if (int.TryParse(defaultValue, out _))
                    {
                        if (int.TryParse(value, out _))
                            return value;
                        else
                            return defaultValue;
                    }

                    // If defaultValue is a boolean, only accept "true" or "false" (case-insensitive)
                    if (string.Equals(defaultValue, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(defaultValue, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                            return value.ToLower();
                        else
                            return defaultValue.ToLower();
                    }

                    return value;
                }
            }
            catch { }

            return defaultValue;
        }

        // INI file parser
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
