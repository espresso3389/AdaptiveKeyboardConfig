using MahApps.Metro.Controls;
using Microsoft.Practices.Prism.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AdaptiveKeyboardConfig
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            Apps = new ObservableCollection<AppEntry>();
            InitializeComponent();
            this.DataContext = this;

            winGrabber = new WinGrabber();
            winGrabber.Show();

            RemoveApp = new DelegateCommand<string>(
                async p =>
                {
                    // FIXME: Dirty code to realize removal animation

                    var idx = appList.SelectedIndex;
                    var appsToRemove = appList.SelectedItems.Cast<AppEntry>().ToArray();
                    foreach (var item in appsToRemove)
                        item.MarkForDeletion();

                    await Task.Delay(200);

                    foreach (var item in appsToRemove)
                        Apps.Remove(item);

                    if (idx >= appList.Items.Count)
                        idx = appList.Items.Count - 1;
                    if (idx >= 0)
                    {
                        appList.SelectedIndex = idx;
                        var item = (ListBoxItem)(appList.ItemContainerGenerator.ContainerFromItem(appList.SelectedItem));
                        if (item != null)
                            item.Focus();
                    }
                },
                p => appList.SelectedIndex >= 0);

            appList.SelectionChanged += (s, e) => RemoveApp.RaiseCanExecuteChanged();
            findTargetWindowButton.PreviewMouseLeftButtonDown += findTargetWindowButton_MouseLeftButtonDown;
            findTargetWindowButton.PreviewMouseMove += findTargetWindowButton_MouseMove;
            findTargetWindowButton.PreviewMouseLeftButtonUp += findTargetWindowButton_MouseLeftButtonUp;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            winGrabber.Close();
        }

        void addApp(AppEntry app)
        {
            if (app == null)
                return;

            if (Apps.FirstOrDefault(a => a.Path == app.Path) != null)
                return;

            var idx = 0; // appList.SelectedIndex;
            if (idx < 0) idx = 0;
            Apps.Insert(idx, app);
            appList.ScrollIntoView(Apps[idx + 1 < Apps.Count ? idx + 1 : idx]);
        }

        AppEntry getAppFromCursorPos(out RECT bounds)
        {
            bounds = new RECT();

            POINT pt;
            GetPhysicalCursorPos(out pt);

            var hwndGrabber = (HwndSource)HwndSource.FromVisual(winGrabber);
            var hwndSrc = (HwndSource)HwndSource.FromVisual(this);

            IntPtr hwndFound = IntPtr.Zero;
            EnumWindows((hwnd, lParam) =>
            {
                RECT rect;
                if (!GetWindowRect(hwnd, out rect) ||
                    !rect.Contains(pt) ||
                    !IsWindowVisible(hwnd) ||
                    hwnd == hwndGrabber.Handle || hwnd == hwndSrc.Handle)
                    return true; // continue search

                hwndFound = hwnd;
                return false; // we found the window!
            }, IntPtr.Zero);
            if (hwndFound == IntPtr.Zero)
                return null;

            var hwndRoot = GetAncestor(hwndFound, GetAncestorFlags.GetRootOwner);

            GetWindowRect(hwndRoot, out bounds);

            try
            {
                using (var proc = GetProcessByWindowHandle(hwndRoot))
                {
                    var exeName = proc.MainModule.FileName;

                    var fvi = FileVersionInfo.GetVersionInfo(exeName);
                    var name = fvi.FileDescription;
                    if (string.IsNullOrEmpty(name))
                    {
                        name = proc.MainWindowTitle;
                        if (string.IsNullOrEmpty(name))
                            name = System.IO.Path.GetFileNameWithoutExtension(exeName);
                    }

                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exeName))
                    {
                        return new AppEntry()
                        {
                            DisplayName = name,
                            Path = exeName,
                            Icon = toImageSource(icon, 32)
                        };
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
        }

        void findTargetWindowButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;
            var elem = sender as UIElement;
            if (elem != null)
                elem.CaptureMouse();
            winGrabber.Visibility = System.Windows.Visibility.Hidden; // Without this, the first attempt to visualize the window failed...
            findWindow();
        }

        void findTargetWindowButton_MouseMove(object sender, MouseEventArgs e)
        {
            e.Handled = false;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (lastAppFound != null)
                {
                    lastAppFound = null;
                    winGrabber.Visibility = System.Windows.Visibility.Hidden;
                }
                return;
            }

            findWindow();
        }

        bool findWindow()
        {
            RECT bounds;
            lastAppFound = getAppFromCursorPos(out bounds);
            if (lastAppFound == null)
            {
                winGrabber.Visibility = System.Windows.Visibility.Hidden;
                return false;
            }

            winGrabber.Left = bounds.Left;
            winGrabber.Top = bounds.Top;
            winGrabber.Width = bounds.Width;
            winGrabber.Height = bounds.Height;
            winGrabber.winTitle.Text = lastAppFound.DisplayName;
            winGrabber.Visibility = System.Windows.Visibility.Visible;
            return true;
        }

        void findTargetWindowButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = false;
            var elem = sender as UIElement;
            if (elem != null)
                elem.ReleaseMouseCapture();
            winGrabber.Visibility = System.Windows.Visibility.Hidden;
            addApp(lastAppFound);
        }

        private void modeSwitchButtonClick(object sender, RoutedEventArgs e)
        {
            var app = ((Button)sender).DataContext as AppEntry;
            if (app == null)
                return;

            app.Mode = (Mode)(((int)app.Mode + 1) % Enum.GetNames(typeof(Mode)).Length);
        }

        public ObservableCollection<AppEntry> Apps { get; set; }
        public DelegateCommand<string> RemoveApp { get; set; }

        private AppEntry lastAppFound;

        private WinGrabber winGrabber;

        [DllImport("User32.dll")]
        static extern bool GetPhysicalCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        delegate bool EnumWindowsProc(IntPtr hwnd, int lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        /// <summary>
        /// Retrieves the handle to the ancestor of the specified window. 
        /// </summary>
        /// <param name="hwnd">A handle to the window whose ancestor is to be retrieved. 
        /// If this parameter is the desktop window, the function returns NULL. </param>
        /// <param name="flags">The ancestor to be retrieved.</param>
        /// <returns>The return value is the handle to the ancestor window.</returns>
        [DllImport("user32.dll", ExactSpelling = true)]
        static extern IntPtr GetAncestor(IntPtr hwnd, GetAncestorFlags flags);

        enum GetAncestorFlags
        {
            /// <summary>
            /// Retrieves the parent window. This does not include the owner, as it does with the GetParent function. 
            /// </summary>
            GetParent = 1,
            /// <summary>
            /// Retrieves the root window by walking the chain of parent windows.
            /// </summary>
            GetRoot = 2,
            /// <summary>
            /// Retrieves the owned root window by walking the chain of parent and owner windows returned by GetParent. 
            /// </summary>
            GetRootOwner = 3
        }

        struct POINT
        {
            public int x;
            public int y;

            public override string ToString() { return string.Format("({0},{1})", x, y); }
        }

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left; 
            public int Top;
            public int Right;
            public int Bottom;

            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }

            public bool Contains(POINT pt)
            {
                return pt.x >= Left && pt.x < Right && pt.y >= Top && pt.y < Bottom;
            }

            public override string ToString()
            {
                return string.Format("({0},{1})-({2},{3})", Left, Top, Right, Bottom);
            }
        }

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static int GetProcessIdByWindowHandle(IntPtr hWnd)
        {
            uint pid = 0;
            GetWindowThreadProcessId(hWnd, out pid);
            return unchecked((int)pid);
        }

        public static Process GetProcessByWindowHandle(IntPtr hWnd)
        {
            return Process.GetProcessById(GetProcessIdByWindowHandle(hWnd));
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        static extern bool DeleteObject(IntPtr hObject);

        ImageSource toImageSource(System.Drawing.Icon icon, int size)
        {
            var ps = PresentationSource.FromVisual(this);
            var w = (int)(size * ps.CompositionTarget.TransformToDevice.M11);
            var h = (int)(size * ps.CompositionTarget.TransformToDevice.M22);

            using (var scaled = new System.Drawing.Icon(icon, w, h))
            {
                var bitmap = scaled.ToBitmap();
                var hBitmap = bitmap.GetHbitmap();

                var wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                if (!DeleteObject(hBitmap))
                {
                    throw new Win32Exception();
                }

                return wpfBitmap;
            }
        }
    }

    /// <summary>
    /// Application Entry
    /// </summary>
    public class AppEntry : INotifyPropertyChanged
    {
        string displayName;
        string path;
        Mode mode;
        ImageSource icon;

        /// <summary>
        /// Application Name (i.e. Window Caption)
        /// </summary>
        public string DisplayName
        {
            get { return displayName; }
            set
            {
                if (displayName == value)
                    return;
                displayName = value;
                propChanged("IsBeingRemoved");
            }
        }

        /// <summary>
        /// Application Executable Path
        /// </summary>
        public string Path
        {
            get { return path; }
            set
            {
                if (path == value)
                    return;
                path = value;
                propChanged("Path");
            }
        }

        /// <summary>
        /// Adaptive Keyboard Mode
        /// </summary>
        public Mode Mode
        {
            get { return mode; }
            set
            {
                if (mode == value)
                    return;
                mode = value;
                propChanged("Mode");
            }
        }

        public ImageSource Icon
        {
            get { return icon; }
            set
            {
                if (icon == value)
                    return;
                icon = value;
                propChanged("Icon");
            }
        }

        public bool IsBeingRemoved { get; private set; }

        public void MarkForDeletion()
        {
            IsBeingRemoved = true;
            propChanged("IsBeingRemoved");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void propChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
    }

    /// <summary>
    /// Adaptive Keyboard Mode
    /// </summary>
    public enum Mode
    {
        Function = 0,
        Home,
        WebBrowser,
        WebConference,
    }
}
