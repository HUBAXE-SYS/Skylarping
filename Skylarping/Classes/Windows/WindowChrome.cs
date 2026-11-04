/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// .NET 4.0 replacement for System.Windows.Shell.WindowChrome (.NET 4.5+), covering what the
// skinned window frames use: no native frame, draggable caption area, resize borders, title-bar
// elements that stay clickable, and Aero glass where DWM composition is available (Vista/7).
// Behaviour follows WPF's WindowChrome so the existing frame code works unchanged.
/*==========================================================*/

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Skylarping.Native.Windows
{
    public sealed class WindowChrome
    {
        public static readonly DependencyProperty IsHitTestVisibleInChromeProperty =
            DependencyProperty.RegisterAttached("IsHitTestVisibleInChrome", typeof(bool), typeof(WindowChrome),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        private Window _window;
        private IntPtr _hwnd;
        private HwndSource _source;
        private Thickness _glassFrameThickness;

        public WindowChrome()
        {
            CaptionHeight = SystemParameters.CaptionHeight;
            ResizeBorderThickness = new Thickness(
                SystemParameters.ResizeFrameVerticalBorderWidth, SystemParameters.ResizeFrameHorizontalBorderHeight,
                SystemParameters.ResizeFrameVerticalBorderWidth, SystemParameters.ResizeFrameHorizontalBorderHeight);
            UseAeroCaptionButtons = true;
        }

        /// <summary>Height of the draggable caption strip at the top of the window (DIPs).</summary>
        public double CaptionHeight { get; set; }

        /// <summary>Width of the invisible resize borders (DIPs).</summary>
        public Thickness ResizeBorderThickness { get; set; }

        /// <summary>When glass is on, let Windows' own min/max/close buttons work (as WPF's default).</summary>
        public bool UseAeroCaptionButtons { get; set; }

        /// <summary>How far the Aero glass frame extends into the window (DIPs). Ignored without DWM.</summary>
        public Thickness GlassFrameThickness
        {
            get { return _glassFrameThickness; }
            set
            {
                _glassFrameThickness = value;
                ApplyGlass();
            }
        }

        /// <summary>True when Aero glass can be drawn (Vista or later with desktop composition on).</summary>
        public static bool IsGlassAvailable
        {
            get
            {
                if (Environment.OSVersion.Version.Major < 6)
                    return false;
                try
                {
                    bool enabled;
                    return DwmIsCompositionEnabled(out enabled) == 0 && enabled;
                }
                catch (DllNotFoundException) { return false; }
                catch (EntryPointNotFoundException) { return false; }
            }
        }

        public static void SetWindowChrome(Window window, WindowChrome chrome)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (chrome == null) throw new ArgumentNullException("chrome");
            chrome.Attach(window);
        }

        /// <summary>
        /// How far (DIPs) Windows would push a resizable window's frame off-screen when maximizing it
        /// normally. Maximize is clamped to the work area instead, so layouts designed around that
        /// overhang can subtract this to keep the same look.
        /// </summary>
        public static Thickness GetMaximizedFrameOverhang(Window window)
        {
            int padded = 0;
            if (Environment.OSVersion.Version.Major >= 6)
                padded = GetSystemMetrics(SM_CXPADDEDBORDER);
            double x = GetSystemMetrics(SM_CXFRAME) + padded;
            double y = GetSystemMetrics(SM_CYFRAME) + padded;

            var source = PresentationSource.FromVisual(window);
            if (source != null && source.CompositionTarget != null)
            {
                Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
                x *= fromDevice.M11;
                y *= fromDevice.M22;
            }
            return new Thickness(x, y, x, y);
        }

        public static void SetIsHitTestVisibleInChrome(IInputElement element, bool value)
        {
            var obj = element as DependencyObject;
            if (obj != null) obj.SetValue(IsHitTestVisibleInChromeProperty, value);
        }

        public static bool GetIsHitTestVisibleInChrome(IInputElement element)
        {
            var obj = element as DependencyObject;
            return obj != null && (bool)obj.GetValue(IsHitTestVisibleInChromeProperty);
        }

        // ---------------------------------------------------------------------------------------

        private void Attach(Window window)
        {
            _window = window;
            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
                Hook();
            else
                window.SourceInitialized += delegate { Hook(); };
        }

        private void Hook()
        {
            _hwnd = new WindowInteropHelper(_window).Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            if (_source == null) return;
            _source.AddHook(WndProc);
            ApplyGlass();
            // Make Windows re-run WM_NCCALCSIZE so the native frame disappears immediately.
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
        }

        private bool GlassActive
        {
            get
            {
                Thickness g = _glassFrameThickness;
                return (g.Left > 0 || g.Top > 0 || g.Right > 0 || g.Bottom > 0) && IsGlassAvailable;
            }
        }

        private void ApplyGlass()
        {
            if (_source == null || _source.CompositionTarget == null) return;
            if (!IsGlassAvailable) return;

            Matrix toDevice = _source.CompositionTarget.TransformToDevice;
            var margins = new MARGINS
            {
                cxLeftWidth = (int)Math.Ceiling(_glassFrameThickness.Left * toDevice.M11),
                cxRightWidth = (int)Math.Ceiling(_glassFrameThickness.Right * toDevice.M11),
                cyTopHeight = (int)Math.Ceiling(_glassFrameThickness.Top * toDevice.M22),
                cyBottomHeight = (int)Math.Ceiling(_glassFrameThickness.Bottom * toDevice.M22),
            };
            // Glass shows through only where WPF renders transparent pixels.
            _source.CompositionTarget.BackgroundColor = GlassActive ? Colors.Transparent : SystemColors.WindowColor;
            try { DwmExtendFrameIntoClientArea(_hwnd, ref margins); } catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_NCCALCSIZE:
                    // Client area = whole window: this is what removes the native frame.
                    handled = true;
                    return new IntPtr(WVR_REDRAW);

                case WM_NCHITTEST:
                    if (GlassActive && UseAeroCaptionButtons)
                    {
                        IntPtr dwmResult;
                        if (DwmDefWindowProc(hwnd, msg, wParam, lParam, out dwmResult))
                        {
                            handled = true;
                            return dwmResult;
                        }
                    }
                    handled = true;
                    return new IntPtr(HitTest(lParam));

                case WM_NCACTIVATE:
                    // Without DWM, the default handler repaints the old frame over the window on
                    // (de)activation; lParam -1 tells it not to.
                    if (!IsGlassAvailable)
                    {
                        handled = true;
                        return DefWindowProc(hwnd, msg, wParam, new IntPtr(-1));
                    }
                    break;

                case WM_NCUAHDRAWCAPTION:
                case WM_NCUAHDRAWFRAME:
                    // XP's visual styles would otherwise draw the themed caption/frame (e.g. on title changes).
                    if (!IsGlassAvailable)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                    break;

                case WM_DWMCOMPOSITIONCHANGED:
                    ApplyGlass();
                    break;

                case WM_GETMINMAXINFO:
                    // A captionless window maximizes over the whole monitor, taskbar included.
                    // Clamp it to the work area instead. Not marked handled, so WPF still applies
                    // MinWidth/MaxWidth etc. to the track sizes afterwards.
                    ApplyWorkAreaMaximize(hwnd, lParam);
                    break;
            }
            return IntPtr.Zero;
        }

        private static void ApplyWorkAreaMaximize(IntPtr hwnd, IntPtr lParam)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref info)) return;

            RECT work = info.rcWork, mon = info.rcMonitor;

            // An auto-hidden taskbar leaves the work area equal to the monitor; keep a 1px strip
            // free on its edge so the taskbar can still be revealed by the mouse.
            if (work.Left == mon.Left && work.Top == mon.Top && work.Right == mon.Right && work.Bottom == mon.Bottom)
            {
                switch (GetAutoHideTaskbarEdge(monitor))
                {
                    case ABE_LEFT: work.Left += 1; break;
                    case ABE_TOP: work.Top += 1; break;
                    case ABE_RIGHT: work.Right -= 1; break;
                    case ABE_BOTTOM: work.Bottom -= 1; break;
                }
            }

            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            mmi.ptMaxPosition.X = work.Left - mon.Left;
            mmi.ptMaxPosition.Y = work.Top - mon.Top;
            mmi.ptMaxSize.X = work.Right - work.Left;
            mmi.ptMaxSize.Y = work.Bottom - work.Top;
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private static int GetAutoHideTaskbarEdge(IntPtr monitor)
        {
            try
            {
                var abd = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                if (((int)SHAppBarMessage(ABM_GETSTATE, ref abd) & ABS_AUTOHIDE) == 0)
                    return -1;
                if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) == IntPtr.Zero)
                    return -1;
                // Only matters if the taskbar lives on this window's monitor.
                if (MonitorFromRect(ref abd.rc, MONITOR_DEFAULTTONEAREST) != monitor)
                    return -1;
                return abd.uEdge;
            }
            catch { return -1; }
        }

        private int HitTest(IntPtr lParam)
        {
            int raw = lParam.ToInt32();
            var screen = new Point((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF));
            Point p;
            try { p = _window.PointFromScreen(screen); }
            catch (InvalidOperationException) { return HTCLIENT; }

            // Title-bar buttons etc. marked IsHitTestVisibleInChrome get normal mouse input.
            var hit = _window.InputHitTest(p) as DependencyObject;
            if (hit != null && (bool)hit.GetValue(IsHitTestVisibleInChromeProperty))
                return HTCLIENT;

            double width = _window.ActualWidth, height = _window.ActualHeight;
            Thickness border = ResizeBorderThickness;
            bool canResize = _window.WindowState == WindowState.Normal
                && (_window.ResizeMode == ResizeMode.CanResize || _window.ResizeMode == ResizeMode.CanResizeWithGrip);

            if (canResize)
            {
                bool left = p.X < border.Left, right = p.X >= width - border.Right;
                bool top = p.Y < border.Top, bottom = p.Y >= height - border.Bottom;
                if (top && left) return HTTOPLEFT;
                if (top && right) return HTTOPRIGHT;
                if (bottom && left) return HTBOTTOMLEFT;
                if (bottom && right) return HTBOTTOMRIGHT;
                if (top) return HTTOP;
                if (bottom) return HTBOTTOM;
                if (left) return HTLEFT;
                if (right) return HTRIGHT;
            }

            double captionBottom = (canResize ? border.Top : 0) + CaptionHeight;
            return p.Y < captionBottom ? HTCAPTION : HTCLIENT;
        }

        // ---------------------------------------------------------------------------------------

        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCACTIVATE = 0x0086;
        private const int WM_NCUAHDRAWCAPTION = 0x00AE;
        private const int WM_NCUAHDRAWFRAME = 0x00AF;
        private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
        private const int WVR_REDRAW = 0x0300;

        private const int HTCLIENT = 1, HTCAPTION = 2;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_NOOWNERZORDER = 0x0200;

        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const uint ABM_GETSTATE = 0x00000004, ABM_GETTASKBARPOS = 0x00000005;
        private const int ABS_AUTOHIDE = 0x0000001;
        private const int ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor, rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        private const int SM_CXFRAME = 32, SM_CYFRAME = 33, SM_CXPADDEDBORDER = 92;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("shell32.dll")]
        private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // dwmapi.dll only exists on Vista+; every call is guarded by an OS/composition check.
        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll")]
        private static extern bool DwmDefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }
}
