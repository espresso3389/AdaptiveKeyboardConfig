using LambdaComparer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AdaptiveKeyboardConfig
{

    /// <summary>
    /// Application Entry
    /// </summary>
    public class AppEntry : INotifyPropertyChanged
    {
        private string displayName;
        private string path;
        private Mode mode;
        private ImageSource icon;
        private string regPath;

        [Flags]
        enum Changes
        {
            Value = 1,
            Path = 2,
        }

        /// <summary>
        /// Application Name (i.e. Window Caption)
        /// </summary>
        public string DisplayName
        {
            get { return displayName; }
            private set
            {
                if (displayName == value)
                    return;
                displayName = value;
                propChanged("IsBeingRemoved", Changes.Path);
            }
        }

        /// <summary>
        /// Application Executable Path
        /// </summary>
        public string Path
        {
            get { return path; }
            private set
            {
                if (path == value)
                    return;
                path = value;
                propChanged("Path", Changes.Value);
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
                propChanged("Mode", Changes.Path);
            }
        }

        /// <summary>
        /// The icon of the executable. If the icon is not loaded, the value is <c>null</c>.
        /// To load the icon explicitly, call <see cref="LoadIconFromModule"/> method.
        /// </summary>
        public ImageSource Icon
        {
            get { return icon; }
            private set
            {
                if (icon == value)
                    return;
                icon = value;
                propChanged("Icon", 0);
            }
        }

        /// <summary>
        /// The registry path, where the <see cref="AppEntry"/> is last loaded from.
        /// </summary>
        public string RegistryPath
        {
            get { return regPath; }
            private set
            {
                if (regPath == value)
                    return;
                regPath = value;
                propChanged("RegistryPath", 0);
            }
        }

        public bool IsBeingRemoved { get; private set; }

        public bool SetupFinished { get; private set; }

        public void MarkForDeletion()
        {
            IsBeingRemoved = true;
            propChanged("IsBeingRemoved", 0);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void propChanged(string propName, Changes changes)
        {
            if (!SetupFinished)
                return;

            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));

            if (changes.HasFlag(Changes.Path))
            {
                UpdateRegistryEntry();
            }
            else if (changes.HasFlag(Changes.Value))
            {
                writeAppPath();
            }
        }

        public void LoadIconFromModule(Visual visual, int size)
        {
            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(Path))
                Icon = toImageSource(visual, icon, size);
        }


        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// Convert an icon to an <see cref="ImageSource"/>.
        /// </summary>
        /// <param name="visual"><see cref="Visual"/> to obtain visual metrics.</param>
        /// <param name="icon">An icon to convert.</param>
        /// <param name="size">The icon size in virtualized pixels (at 96 dpi).</param>
        /// <returns><see cref="ImageSource"/> of the created icon image.</returns>
        private ImageSource toImageSource(Visual visual, Icon icon, int size)
        {
            var ps = PresentationSource.FromVisual(visual);
            var w = (int)(size * ps.CompositionTarget.TransformToDevice.M11);
            var h = (int)(size * ps.CompositionTarget.TransformToDevice.M22);

            using (var scaled = new Icon(icon, w, h))
            {
                using (var bitmap = scaled.ToBitmap())
                {
                    var hBitmap = bitmap.GetHbitmap();
                    try
                    {
                        return Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    }
                    finally
                    {
                        if (!DeleteObject(hBitmap))
                        {
                            throw new Win32Exception();
                        }
                    }
                }

            }
        }

        /// <summary>
        /// Create <see cref="AppEntry"/> from a <see cref="Visual"/>.
        /// </summary>
        /// <param name="visual">Visual of a app.</param>
        /// <returns>The <see cref="AppEntry"/> of the specified <see cref="Visual"/>.</returns>
        public static AppEntry FromVisual(Visual visual)
        {
            var hwndSource = PresentationSource.FromVisual(visual) as HwndSource;
            if (hwndSource == null)
                return null;
            return FromHwnd(hwndSource.Handle);
        }

        /// <summary>
        /// Create <see cref="AppEntry"/> from Window handle.
        /// </summary>
        /// <param name="hwnd">Handle of a window.</param>
        /// <returns>The <see cref="AppEntry"/> of the specified window handle.</returns>
        public static AppEntry FromHwnd(IntPtr hwnd)
        {
            try
            {
                using (var proc = GetProcessByWindowHandle(hwnd))
                {
                    var exeName = proc.MainModule.FileName;

                    var fvi = FileVersionInfo.GetVersionInfo(exeName);
                    var name = fvi.FileDescription;
                    if (String.IsNullOrEmpty(name))
                    {
                        name = proc.MainWindowTitle;
                        if (String.IsNullOrEmpty(name))
                            name = System.IO.Path.GetFileNameWithoutExtension(exeName);
                    }

                    var app = new AppEntry() { DisplayName = name, Path = exeName };
                    app.SetupFinished = true;
                    return app;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
        }

        /// <summary>
        /// Enumerate applications on the desktop except the ones listed on the argument.
        /// </summary>
        /// <param name="appsToIgnore">Apps to ignore.</param>
        /// <returns>List of the apps.</returns>
        public static IEnumerable<AppEntry> EnumAppsExcluding(IEnumerable<AppEntry> appsToIgnore)
        {
            var ignore = new HashSet<AppEntry>(appsToIgnore.Where(app => app != null), new CompareSelector<AppEntry, string>(app => app.Path));

            return EnumWindows()
                .Where(hwnd => IsWindowVisible(hwnd))
                .Select(hwnd => GetAncestor(hwnd, GetAncestorFlags.GetRootOwner))
                .Where(hwnd =>
                {
                    // The code is based on the following article:
                    //   Why does EnumWindows return more windows than I expected?
                    //   http://stackoverflow.com/questions/7277366/why-does-enumwindows-return-more-windows-than-i-expected
                    var ti = new TITLEBARINFO();
                    ti.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(ti);
                    if (!GetTitleBarInfo(hwnd, ref ti))
                        return false;
                    if (ti.rgstate[0].HasFlag(STATE_SYSTEM.INVISIBLE))
                        return false;

                    if (((WindowStylesEx)GetWindowLong(hwnd, GWL_EXSTYLE)).HasFlag(WindowStylesEx.WS_EX_TOOLWINDOW))
                        return false;

                    return true;
                })
                .Select(AppEntry.FromHwnd)
                .Where(app => app != null)
                .Distinct(app => app.Path)
                .Where(app => app != null && !ignore.Contains(app));
        }

        /// <summary>
        /// Registry root path for Adaptive Keyboard Entry
        /// </summary>
        private static readonly string RegistryRootPath = @"Software\Lenovo\SmartKey\Application\Row";

        /// <summary>
        /// Load all the applications from the registry entries.
        /// </summary>
        /// <returns>List of the apps.</returns>
        public static IEnumerable<AppEntry> LoadAppsFromRegistry(Visual visual, int iconSize)
        {
            using (var reg = Registry.CurrentUser.CreateSubKey(RegistryRootPath))
            {
                foreach (Mode mode in Enum.GetValues(typeof (Mode)))
                {
                    using (var modeReg = reg.CreateSubKey(mode.ToString()))
                    {
                        foreach (var appName in modeReg.GetSubKeyNames())
                        {
                            using (var appReg = modeReg.CreateSubKey(appName))
                            {
                                var app = new AppEntry()
                                {
                                    DisplayName = appName,
                                    Mode = mode,
                                    Path = appReg.GetValue("AppPath") as string,
                                    RegistryPath = string.Format(@"{0}\{1}\{2}", RegistryRootPath, mode, appName)
                                };
                                app.LoadIconFromModule(visual, iconSize);
                                app.SetupFinished = true;
                                yield return app;
                            }
                        }
                    }
                }
            }    
        }

        public void RemoveRegistryEntry()
        {
            if (!string.IsNullOrEmpty(RegistryPath))
            {
                // Remove the current entry anyway
                var regPath = System.IO.Path.GetDirectoryName(RegistryPath);
                var key = System.IO.Path.GetFileName(RegistryPath);

                using (var reg = Registry.CurrentUser.CreateSubKey(regPath))
                    reg.DeleteSubKeyTree(key);

                RegistryPath = null;
            }
        }

        private void writeAppPath()
        {
            var newRegPath = string.Format(@"{0}\{1}\{2}", RegistryRootPath, Mode, DisplayName);
            using (var reg = Registry.CurrentUser.CreateSubKey(newRegPath))
                reg.SetValue("AppPath", Path);

            RegistryPath = newRegPath;
        }

        public void UpdateRegistryEntry()
        {
            RemoveRegistryEntry();
            writeAppPath();
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        static IEnumerable<IntPtr> EnumWindows()
        {
            var list = new List<IntPtr>();
            EnumWindows((hwnd, lParam) =>
            {
                list.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        /// <summary>
        /// Retrieves the handle to the ancestor of the specified window. 
        /// </summary>
        /// <param name="hwnd">A handle to the window whose ancestor is to be retrieved. 
        /// If this parameter is the desktop window, the function returns NULL. </param>
        /// <param name="flags">The ancestor to be retrieved.</param>
        /// <returns>The return value is the handle to the ancestor window.</returns>
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr GetAncestor(IntPtr hwnd, GetAncestorFlags flags);

        private enum GetAncestorFlags
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

        [DllImport("user32.dll")]
        static extern bool GetTitleBarInfo(IntPtr hwnd, ref TITLEBARINFO pti);

        [StructLayout(LayoutKind.Sequential)]
        struct TITLEBARINFO
        {
            public const int CCHILDREN_TITLEBAR = 5;
            public uint cbSize;
            public MahApps.Metro.Native.RECT rcTitleBar;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = CCHILDREN_TITLEBAR + 1)]
            public STATE_SYSTEM[] rgstate;
        }

        [Flags]
        enum STATE_SYSTEM : uint
        {
            FOCUSABLE = 0x00100000,
            INVISIBLE = 0x00008000,
            OFFSCREEN = 0x00010000,
            UNAVAILABLE = 0x00000001,
            PRESSED = 0x00000008
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        static readonly int GWL_EXSTYLE = (-20);

        [Flags]
        enum WindowStylesEx : uint
        {
            /// <summary>
            /// Specifies that a window created with this style accepts drag-drop files.
            /// </summary>
            WS_EX_ACCEPTFILES = 0x00000010,
            /// <summary>
            /// Forces a top-level window onto the taskbar when the window is visible.
            /// </summary>
            WS_EX_APPWINDOW = 0x00040000,
            /// <summary>
            /// Specifies that a window has a border with a sunken edge.
            /// </summary>
            WS_EX_CLIENTEDGE = 0x00000200,
            /// <summary>
            /// Windows XP: Paints all descendants of a window in bottom-to-top painting order using double-buffering. For more information, see Remarks. This cannot be used if the window has a class style of either CS_OWNDC or CS_CLASSDC. 
            /// </summary>
            WS_EX_COMPOSITED = 0x02000000,
            /// <summary>
            /// Includes a question mark in the title bar of the window. When the user clicks the question mark, the cursor changes to a question mark with a pointer. If the user then clicks a child window, the child receives a WM_HELP message. The child window should pass the message to the parent window procedure, which should call the WinHelp function using the HELP_WM_HELP command. The Help application displays a pop-up window that typically contains help for the child window.
            /// WS_EX_CONTEXTHELP cannot be used with the WS_MAXIMIZEBOX or WS_MINIMIZEBOX styles.
            /// </summary>
            WS_EX_CONTEXTHELP = 0x00000400,
            /// <summary>
            /// The window itself contains child windows that should take part in dialog box navigation. If this style is specified, the dialog manager recurses into children of this window when performing navigation operations such as handling the TAB key, an arrow key, or a keyboard mnemonic.
            /// </summary>
            WS_EX_CONTROLPARENT = 0x00010000,
            /// <summary>
            /// Creates a window that has a double border; the window can, optionally, be created with a title bar by specifying the WS_CAPTION style in the dwStyle parameter.
            /// </summary>
            WS_EX_DLGMODALFRAME = 0x00000001,
            /// <summary>
            /// Windows 2000/XP: Creates a layered window. Note that this cannot be used for child windows. Also, this cannot be used if the window has a class style of either CS_OWNDC or CS_CLASSDC. 
            /// </summary>
            WS_EX_LAYERED = 0x00080000,
            /// <summary>
            /// Arabic and Hebrew versions of Windows 98/Me, Windows 2000/XP: Creates a window whose horizontal origin is on the right edge. Increasing horizontal values advance to the left. 
            /// </summary>
            WS_EX_LAYOUTRTL = 0x00400000,
            /// <summary>
            /// Creates a window that has generic left-aligned properties. This is the default.
            /// </summary>
            WS_EX_LEFT = 0x00000000,
            /// <summary>
            /// If the shell language is Hebrew, Arabic, or another language that supports reading order alignment, the vertical scroll bar (if present) is to the left of the client area. For other languages, the style is ignored.
            /// </summary>
            WS_EX_LEFTSCROLLBAR = 0x00004000,
            /// <summary>
            /// The window text is displayed using left-to-right reading-order properties. This is the default.
            /// </summary>
            WS_EX_LTRREADING = 0x00000000,
            /// <summary>
            /// Creates a multiple-document interface (MDI) child window.
            /// </summary>
            WS_EX_MDICHILD = 0x00000040,
            /// <summary>
            /// Windows 2000/XP: A top-level window created with this style does not become the foreground window when the user clicks it. The system does not bring this window to the foreground when the user minimizes or closes the foreground window. 
            /// To activate the window, use the SetActiveWindow or SetForegroundWindow function.
            /// The window does not appear on the taskbar by default. To force the window to appear on the taskbar, use the WS_EX_APPWINDOW style.
            /// </summary>
            WS_EX_NOACTIVATE = 0x08000000,
            /// <summary>
            /// Windows 2000/XP: A window created with this style does not pass its window layout to its child windows.
            /// </summary>
            WS_EX_NOINHERITLAYOUT = 0x00100000,
            /// <summary>
            /// Specifies that a child window created with this style does not send the WM_PARENTNOTIFY message to its parent window when it is created or destroyed.
            /// </summary>
            WS_EX_NOPARENTNOTIFY = 0x00000004,
            /// <summary>
            /// Combines the WS_EX_CLIENTEDGE and WS_EX_WINDOWEDGE styles.
            /// </summary>
            WS_EX_OVERLAPPEDWINDOW = WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE,
            /// <summary>
            /// Combines the WS_EX_WINDOWEDGE, WS_EX_TOOLWINDOW, and WS_EX_TOPMOST styles.
            /// </summary>
            WS_EX_PALETTEWINDOW = WS_EX_WINDOWEDGE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
            /// <summary>
            /// The window has generic "right-aligned" properties. This depends on the window class. This style has an effect only if the shell language is Hebrew, Arabic, or another language that supports reading-order alignment; otherwise, the style is ignored.
            /// Using the WS_EX_RIGHT style for static or edit controls has the same effect as using the SS_RIGHT or ES_RIGHT style, respectively. Using this style with button controls has the same effect as using BS_RIGHT and BS_RIGHTBUTTON styles.
            /// </summary>
            WS_EX_RIGHT = 0x00001000,
            /// <summary>
            /// Vertical scroll bar (if present) is to the right of the client area. This is the default.
            /// </summary>
            WS_EX_RIGHTSCROLLBAR = 0x00000000,
            /// <summary>
            /// If the shell language is Hebrew, Arabic, or another language that supports reading-order alignment, the window text is displayed using right-to-left reading-order properties. For other languages, the style is ignored.
            /// </summary>
            WS_EX_RTLREADING = 0x00002000,
            /// <summary>
            /// Creates a window with a three-dimensional border style intended to be used for items that do not accept user input.
            /// </summary>
            WS_EX_STATICEDGE = 0x00020000,
            /// <summary>
            /// Creates a tool window; that is, a window intended to be used as a floating toolbar. A tool window has a title bar that is shorter than a normal title bar, and the window title is drawn using a smaller font. A tool window does not appear in the taskbar or in the dialog that appears when the user presses ALT+TAB. If a tool window has a system menu, its icon is not displayed on the title bar. However, you can display the system menu by right-clicking or by typing ALT+SPACE. 
            /// </summary>
            WS_EX_TOOLWINDOW = 0x00000080,
            /// <summary>
            /// Specifies that a window created with this style should be placed above all non-topmost windows and should stay above them, even when the window is deactivated. To add or remove this style, use the SetWindowPos function.
            /// </summary>
            WS_EX_TOPMOST = 0x00000008,
            /// <summary>
            /// Specifies that a window created with this style should not be painted until siblings beneath the window (that were created by the same thread) have been painted. The window appears transparent because the bits of underlying sibling windows have already been painted.
            /// To achieve transparency without these restrictions, use the SetWindowRgn function.
            /// </summary>
            WS_EX_TRANSPARENT = 0x00000020,
            /// <summary>
            /// Specifies that a window has a border with a raised edge.
            /// </summary>
            WS_EX_WINDOWEDGE = 0x00000100
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

