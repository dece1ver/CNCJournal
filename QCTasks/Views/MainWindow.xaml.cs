using QCTasks.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
        var vm = new MainViewModel { OwnerWindow = this };
        DataContext = vm;

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

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void LimitButton_Click(object sender, RoutedEventArgs e) =>
        LimitPopup.IsOpen = !LimitPopup.IsOpen;

    private void LimitPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            int.TryParse(btn.Tag?.ToString(), out int limit) &&
            DataContext is MainViewModel vm)
        {
            vm.TasksLimit = limit;
        }
        LimitPopup.IsOpen = false;
    }
}