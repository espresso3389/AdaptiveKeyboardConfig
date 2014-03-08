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

