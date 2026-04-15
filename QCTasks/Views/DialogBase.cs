using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace QCTasks.Views;

/// <summary>
/// Базовый класс для всех диалогов: кастомная рамка, drag, тот же стиль что и MainWindow.
/// </summary>
public class DialogBase : Window
{
    private const double ResizeBorder = 6;

    protected DialogBase()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = System.Windows.Media.Brushes.Transparent;
        AllowsTransparency = false;

        var chrome = new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var workArea = SystemParameters.WorkArea;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var s = dpi.PixelsPerDip;
            var bp = (int)(ResizeBorder * s);
            mmi.ptMaxSize.x = (int)(workArea.Width * s) + bp * 2;
            mmi.ptMaxSize.y = (int)(workArea.Height * s) + bp * 2;
            mmi.ptMaxPosition.x = (int)(workArea.Left * s) - bp;
            mmi.ptMaxPosition.y = (int)(workArea.Top * s) - bp;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }
    protected void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        DragMove();
}