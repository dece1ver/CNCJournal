using QCTasks.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace QCTasks;

public partial class MainWindow : Window
{
    private const double ResizeBorder = 6;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        StateChanged += (_, _) =>
        {
            RootGrid.Margin = WindowState == WindowState.Maximized
                ? new Thickness(ResizeBorder)
                : new Thickness(0);
        };
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
            var dpi = VisualTreeHelper.GetDpi(this);
            var scaling = dpi.PixelsPerDip;

            mmi.ptMaxPosition.x = (int)(workArea.Left * scaling);
            mmi.ptMaxPosition.y = (int)(workArea.Top * scaling);
            mmi.ptMaxSize.x = (int)(workArea.Width * scaling);
            mmi.ptMaxSize.y = (int)(workArea.Height * scaling);

            var borderPx = (int)(ResizeBorder * scaling);
            mmi.ptMaxSize.x += borderPx * 2;
            mmi.ptMaxSize.y += borderPx * 2;
            mmi.ptMaxPosition.x -= borderPx;
            mmi.ptMaxPosition.y -= borderPx;

            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
    
}