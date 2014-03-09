using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PerMonitorDpi
{
    public class PerMonitorDpiHelper : DependencyObject
    {
        public PerMonitorDpiHelper(Window window)
        {
            Debug.WriteLine(
                string.Format("Process DPI Awareness: {0}", NativeMethods.GetProcessDpiAwareness(IntPtr.Zero)));

            this.window = window;
            this.systemDpi = window.GetSystemDpi();
            this.hwndSource = PresentationSource.FromVisual(window) as HwndSource;
            if (this.hwndSource != null)
            {
                this.currentDpi = this.hwndSource.GetDpi();
                this.ChangeDpi(this.currentDpi);
                this.hwndSource.AddHook(this.WndProc);
                this.window.Closed += (sender, args) => this.hwndSource.RemoveHook(this.WndProc);
            }
        }

        /// <summary>
        /// It can be used for <see cref="FrameworkElement.LayoutTransform"/>.
        /// </summary>
        public Transform DpiScaleTransform
        {
            get { return (Transform)this.GetValue(DpiScaleTransformProperty); }
            set { this.SetValue(DpiScaleTransformProperty, value); }
        }

        public static readonly DependencyProperty DpiScaleTransformProperty =
            DependencyProperty.Register("DpiScaleTransform", typeof(Transform), typeof(PerMonitorDpiHelper), new UIPropertyMetadata(Transform.Identity));

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)NativeMethods.WindowMessage.WM_DPICHANGED)
            {
                var dpiX = wParam.ToHiWord();
                var dpiY = wParam.ToLoWord();
                this.ChangeDpi(new Dpi(dpiX, dpiY));
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void ChangeDpi(Dpi dpi)
        {
            if (!PerMonitorDpiMethods.IsSupported) return;

            this.DpiScaleTransform = (dpi == this.systemDpi)
                ? Transform.Identity
                : new ScaleTransform((double)dpi.X / this.systemDpi.X, (double)dpi.Y / this.systemDpi.Y);

            this.window.Width = this.window.Width * dpi.X / this.currentDpi.X;
            this.window.Height = this.window.Height * dpi.Y / this.currentDpi.Y;

            Debug.WriteLine(string.Format("DPI Change: {0} -> {1} (System: {2})",
                this.currentDpi, dpi, this.systemDpi));

            this.currentDpi = dpi;
        }

        private Dpi systemDpi;
        private Dpi currentDpi;
        private readonly Window window;
        private readonly HwndSource hwndSource;


    }

    internal static class PerMonitorDpiMethods
    {
        
        public static bool IsSupported
        {
            get
            {
                var version = Environment.OSVersion.Version;
                return version.Major * 1000 + version.Minor >= 6003; // Windows 8.1: 6.3
            }
        }

        public static Dpi GetSystemDpi(this Visual visual)
        {
            var source = PresentationSource.FromVisual(visual);
            if (source != null && source.CompositionTarget != null)
            {
                return new Dpi(
                    (uint)(Dpi.Default.X * source.CompositionTarget.TransformToDevice.M11),
                    (uint)(Dpi.Default.Y * source.CompositionTarget.TransformToDevice.M22));
            }

            return Dpi.Default;
        }

        public static Dpi GetDpi(this HwndSource hwndSource, MonitorDpiType dpiType = MonitorDpiType.Default)
        {
            if (!IsSupported) return Dpi.Default;

            var hMonitor = NativeMethods.MonitorFromWindow(
                hwndSource.Handle,
                NativeMethods.MonitorDefaultTo.Nearest);

            uint dpiX = 1, dpiY = 1;
            NativeMethods.GetDpiForMonitor(hMonitor, dpiType, ref dpiX, ref dpiY);

            return new Dpi(dpiX, dpiY);
        }
    }

    public struct Dpi
    {
        public static readonly Dpi Default = new Dpi(96, 96);

        public uint X { get; set; }
        public uint Y { get; set; }

        public Dpi(uint x, uint y)
            : this()
        {
            this.X = x;
            this.Y = y;
        }

        public static bool operator ==(Dpi dpi1, Dpi dpi2)
        {
            return dpi1.X == dpi2.X && dpi1.Y == dpi2.Y;
        }

        public static bool operator !=(Dpi dpi1, Dpi dpi2)
        {
            return !(dpi1 == dpi2);
        }

        public bool Equals(Dpi other)
        {
            return this.X == other.X && this.Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is Dpi && Equals((Dpi)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)this.X * 397) ^ (int)this.Y;
            }
        }

        public override string ToString()
        {
            return string.Format("[X={0},Y={1}]", X, Y);
        }
    }

    /// <summary>
    /// Identifies dots per inch (dpi) type.
    /// </summary>
    public enum MonitorDpiType
    {
        /// <summary>
        /// MDT_Effective_DPI
        /// <para>Effective DPI that incorporates accessibility overrides and matches what Desktop Window Manage (DWM) uses to scale desktop applications.</para>
        /// </summary>
        EffectiveDpi = 0,

        /// <summary>
        /// MDT_Angular_DPI
        /// <para>DPI that ensures rendering at a compliant angular resolution on the screen, without incorporating accessibility overrides.</para>
        /// </summary>
        AngularDpi = 1,

        /// <summary>
        /// MDT_Raw_DPI
        /// <para>Linear DPI of the screen as measures on the screen itself.</para>
        /// </summary>
        RawDpi = 2,

        /// <summary>
        /// MDT_Default
        /// </summary>
        Default = EffectiveDpi,
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, MonitorDefaultTo dwFlags);

        [DllImport("shcore.dll")]
        public static extern void GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, ref uint dpiX, ref uint dpiY);

        [DllImport("shcore.dll")]
        private static extern int GetProcessDpiAwareness(IntPtr handle, ref ProcessDpiAwareness awareness);

        public static ProcessDpiAwareness GetProcessDpiAwareness(IntPtr handle)
        {
            ProcessDpiAwareness pda = ProcessDpiAwareness.Unaware;
            if (GetProcessDpiAwareness(handle, ref pda) == 0)
                return pda;
            return ProcessDpiAwareness.Unaware;
        }

        public enum MonitorDefaultTo
        {
            Null = 0,
            Primary = 1,
            Nearest = 2,
        }

        public enum WindowMessage
        {
            WM_DPICHANGED = 0x02E0,
        }

        public enum ProcessDpiAwareness
        {
          Unaware = 0,
          SystemDpiAware = 1,
          PerMonitorDpiAware = 2
        }
    }

    internal static class IntPtrExtensions
    {
        public static ushort ToLoWord(this IntPtr dword)
        {
            return (ushort)((uint)dword & 0xffff);
        }

        public static ushort ToHiWord(this IntPtr dword)
        {
            return (ushort)((uint)dword >> 16);
        }
    }
}