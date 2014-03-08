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

        private void propChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
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
            return FromHwnd(((HwndSource)PresentationSource.FromVisual(visual)).Handle);
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

                    return new AppEntry() { DisplayName = name, Path = exeName };
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
            var ignore = new HashSet<AppEntry>(appsToIgnore, new CompareSelector<AppEntry, string>(a => a.Path));

            return EnumWindows()
                .Where(hwnd => IsWindowVisible(hwnd))
                .Select(hwnd => GetAncestor(hwnd, GetAncestorFlags.GetRootOwner))
                .Select(AppEntry.FromHwnd)
                .Distinct(app => app.Path)
                .Where(app => app != null && !ignore.Contains(app));
        }

        /// <summary>
        /// Load all the applications from the registry entries.
        /// </summary>
        /// <returns>List of the apps.</returns>
        public static IEnumerable<AppEntry> LoadAppsFromRegistry(Visual visual, int iconSize)
        {
            using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\Lenovo\SmartKey\Application\Row"))
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
                                    Path = appReg.GetValue("AppPath") as string
                                };
                                app.LoadIconFromModule(visual, iconSize);
                                yield return app;
                            }
                        }
                    }
                }
            }    
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

