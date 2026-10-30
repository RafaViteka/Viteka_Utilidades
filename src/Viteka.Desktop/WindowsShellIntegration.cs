using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Viteka.Desktop;

// Use the work area of the window's actual monitor, in physical pixels, not the primary screen.
internal sealed class WindowsShellIntegration : IDisposable
{
    internal const string AppId = "Viteka.Utilidades";
    private readonly Window window;
    private HwndSource? source;
    private IntPtr largeIcon, smallIcon;
    private readonly double minimumWidth, minimumHeight;
    internal WindowsShellIntegration(Window window)
    {
        this.window = window; minimumWidth = window.MinWidth; minimumHeight = window.MinHeight;
        window.SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source.AddHook(Hook);
            ApplyIcons();
        };
        window.ContentRendered += (_, _) => { ApplyIcons(); ClampNormalWindow(); };
        // A maximized launch skips the normal-window clamp; apply it when the user restores.
        window.StateChanged += (_, _) =>
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ClampNormalWindow));
    }
    internal static void RegisterIdentity() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
    private IntPtr Handle => new WindowInteropHelper(window).Handle;
    private MonitorInfo Monitor()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(Handle, 2), ref info)) throw new System.ComponentModel.Win32Exception();
        return info;
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x24)
        {
            var monitor = Monitor(); var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            limits.MaxPosition = new PointI { X = monitor.Work.Left - monitor.Bounds.Left, Y = monitor.Work.Top - monitor.Bounds.Top };
            limits.MaxSize = new PointI { X = monitor.Work.Right - monitor.Work.Left, Y = monitor.Work.Bottom - monitor.Work.Top };
            var scale = GetDpiForWindow(hwnd) / 96d;
            limits.MinTrackSize = new PointI { X = Math.Min((int)Math.Ceiling(minimumWidth * scale), limits.MaxSize.X), Y = Math.Min((int)Math.Ceiling(minimumHeight * scale), limits.MaxSize.Y) };
            Marshal.StructureToPtr(limits, lParam, false); handled = true;
        }
        if (message is 0x2E0 or 0x7E or 0x1A)
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { ApplyIcons(); ClampNormalWindow(); }));
        return IntPtr.Zero;
    }
    private void ClampNormalWindow()
    {
        if (window.WindowState != WindowState.Normal) return;
        var work = Monitor().Work; var dpi = VisualTreeHelper.GetDpi(window);
        window.MinWidth = Math.Min(minimumWidth, (work.Right - work.Left) / dpi.DpiScaleX);
        window.MinHeight = Math.Min(minimumHeight, (work.Bottom - work.Top) / dpi.DpiScaleY);
        if (!GetWindowRect(Handle, out var rect)) return;
        var width = Math.Min(rect.Right - rect.Left, work.Right - work.Left);
        var height = Math.Min(rect.Bottom - rect.Top, work.Bottom - work.Top);
        var left = Math.Clamp(rect.Left, work.Left, work.Right - width);
        var top = Math.Clamp(rect.Top, work.Top, work.Bottom - height);
        if (left != rect.Left || top != rect.Top || width != rect.Right - rect.Left || height != rect.Bottom - rect.Top)
            SetWindowPos(Handle, IntPtr.Zero, left, top, width, height, 0x14);
    }
    private void ApplyIcons()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "viteka.ico");
        if (!File.Exists(path)) return;
        var dpi = GetDpiForWindow(Handle);
        var big = LoadImage(IntPtr.Zero, path, 1, GetSystemMetricsForDpi(11, dpi), GetSystemMetricsForDpi(12, dpi), 0x10);
        var small = LoadImage(IntPtr.Zero, path, 1, GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi), 0x10);
        if (big == IntPtr.Zero || small == IntPtr.Zero) { if (big != IntPtr.Zero) DestroyIcon(big); if (small != IntPtr.Zero) DestroyIcon(small); return; }
        SendMessage(Handle, 0x80, new IntPtr(1), big);
        SendMessage(Handle, 0x80, IntPtr.Zero, small);
        if (largeIcon != IntPtr.Zero) DestroyIcon(largeIcon); if (smallIcon != IntPtr.Zero) DestroyIcon(smallIcon);
        largeIcon = big; smallIcon = small;
    }
    internal bool HasNativeIcons => largeIcon != IntPtr.Zero && smallIcon != IntPtr.Zero && SendMessage(Handle, 0x7F, IntPtr.Zero, IntPtr.Zero) == smallIcon && SendMessage(Handle, 0x7F, new IntPtr(1), IntPtr.Zero) == largeIcon;
    internal bool WithinWorkArea()
    {
        var work = Monitor().Work;
        if (DwmGetWindowAttribute(Handle, 9, out var visible, Marshal.SizeOf<RectI>()) != 0 && !GetWindowRect(Handle, out visible)) return false;
        return visible.Left >= work.Left - 2 && visible.Top >= work.Top - 2 && visible.Right <= work.Right + 2 && visible.Bottom <= work.Bottom + 2;
    }
    internal string Diagnostics()
    {
        var work = Monitor().Work;
        GetWindowRect(Handle, out var bounds);
        DwmGetWindowAttribute(Handle, 9, out var visible, Marshal.SizeOf<RectI>());
        return $"state={window.WindowState}; dpi={GetDpiForWindow(Handle)}; icons={largeIcon}/{smallIcon}; actual={SendMessage(Handle, 0x7F, new IntPtr(1), IntPtr.Zero)}/{SendMessage(Handle, 0x7F, IntPtr.Zero, IntPtr.Zero)}; work={work.Left},{work.Top},{work.Right},{work.Bottom}; bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}; visible={visible.Left},{visible.Top},{visible.Right},{visible.Bottom}";
    }
    internal void SmokeCtrlWheel()
    {
        if (!App.SmokeTest) throw new InvalidOperationException();
        GetWindowRect(Handle, out var rect);
        SetForegroundWindow(Handle);
        SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        keybd_event(0x11, 0, 0, UIntPtr.Zero);
        try { mouse_event(0x0800, 0, 0, 120, UIntPtr.Zero); }
        finally { keybd_event(0x11, 0, 2, UIntPtr.Zero); }
    }
    public void Dispose()
    {
        if (source is { IsDisposed: false }) source.RemoveHook(Hook);
        if (largeIcon != IntPtr.Zero) DestroyIcon(largeIcon); if (smallIcon != IntPtr.Zero) DestroyIcon(smallIcon);
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public PointI Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectI Bounds, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RectI rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RectI rect, int size);
}
