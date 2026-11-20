using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DailyReminder;

public partial class ReminderWindow : Window
{
    private readonly string _taskTitle;
    private readonly string _taskTime;
    private readonly string _style;
    private readonly Color _accent;
    private readonly Rect _origin;
    private readonly DispatcherTimer _autoCloseTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _layerTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public ReminderWindow(string taskTitle, string taskTime, string style, Color accent, Rect origin)
    {
        _taskTitle = taskTitle;
        _taskTime = taskTime;
        _style = style;
        _accent = accent;
        _origin = origin;
        InitializeComponent();
        _autoCloseTimer.Tick += (_, _) => Close();
        _layerTimer.Tick += (_, _) => RaiseLayer();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var point = new NativePoint { X = (int)_origin.X, Y = (int)_origin.Y };
        var monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            Close();
            return;
        }

        SetWindowPos(handle, new IntPtr(-1), info.Work.Left, info.Work.Top,
            info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top, 0x0050);
        var dpi = Math.Max(96u, GetDpiForWindow(handle));
        var scale = 96.0 / dpi;
        Width = (info.Work.Right - info.Work.Left) * scale;
        Height = (info.Work.Bottom - info.Work.Top) * scale;
        SetWindowPos(handle, new IntPtr(-1), info.Work.Left, info.Work.Top,
            info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top, 0x0050);

        TitleText.Text = _taskTitle;
        TimeText.Text = _style == "surprise"
            ? $"‼  {(_taskTime.Length == 0 ? "该行动啦" : _taskTime)}  ‼"
            : _taskTime.Length == 0 ? "该行动啦" : _taskTime;
        TimeText.Foreground = new SolidColorBrush(_accent);
        FloatingReminder.Width = Math.Min(Width - 60, _taskTitle.Length <= 16 ? 660 : 900);
        var fontScale = _taskTitle.Length switch { <= 8 => 2.7, <= 20 => 2.2, _ => 1.7 };
        ReminderAnimations.Start(TitleText, _style, _accent, fontScale);

        var targetLeft = (Width - FloatingReminder.Width) / 2;
        var targetTop = Math.Max(40, Height * 0.30);
        var source = PointFromScreen(_origin.TopLeft);
        Animate(Canvas.LeftProperty, source.X - 20, targetLeft);
        Animate(Canvas.TopProperty, source.Y - 25, targetTop);
        var entrance = new DoubleAnimation(0.18, 1, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
        };
        EntranceScale.BeginAnimation(ScaleTransform.ScaleXProperty, entrance);
        EntranceScale.BeginAnimation(ScaleTransform.ScaleYProperty, entrance.Clone());
        _autoCloseTimer.Start();
        _layerTimer.Start();
    }

    private void Animate(DependencyProperty property, double from, double to)
    {
        FloatingReminder.BeginAnimation(property,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void RaiseLayer()
    {
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        _layerTimer.Stop();
        ReminderAnimations.Stop(TitleText);
        base.OnClosed(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int left, int top, int width, int height, uint flags);
}
