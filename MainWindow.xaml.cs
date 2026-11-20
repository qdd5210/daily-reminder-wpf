using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DailyReminder;

public partial class MainWindow : Window
{
    private const double MinWindowWidth = 260;
    private const double MinWindowHeight = 130;
    private const double CompactWidth = 246;
    private const double CompactHeight = 64;

    private readonly bool _startMinimized;
    private readonly string _dataDirectory;
    private readonly string _dataFile;
    private readonly ObservableCollection<TaskRow> _rows = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly IReadOnlyDictionary<string, ThemePalette> ThemePalettes =
        new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase)
        {
            ["night"] = new(
                "夜蓝",
                Color.FromRgb(0x13, 0x1E, 0x30),
                Color.FromArgb(0x38, 0x7D, 0xD3, 0xFC),
                Color.FromArgb(0x0C, 0xC4, 0xB5, 0xFD),
                Color.FromRgb(0x7D, 0xD3, 0xFC),
                Color.FromRgb(0x09, 0x20, 0x2B)),
            ["teal"] = new(
                "青雾",
                Color.FromRgb(0x10, 0x20, 0x22),
                Color.FromArgb(0x38, 0x57, 0xD6, 0xC0),
                Color.FromArgb(0x0C, 0x64, 0xC7, 0xB7),
                Color.FromRgb(0x74, 0xE0, 0xC8),
                Color.FromRgb(0x08, 0x25, 0x1F)),
            ["amber"] = new(
                "暖铜",
                Color.FromRgb(0x24, 0x1A, 0x16),
                Color.FromArgb(0x38, 0xF5, 0xB5, 0x6B),
                Color.FromArgb(0x10, 0xE5, 0x8D, 0x61),
                Color.FromRgb(0xF7, 0xC2, 0x7A),
                Color.FromRgb(0x2B, 0x17, 0x0A))
        };

    private ReminderData _data = new();
    private ReminderTask? _draftTask;
    private DispatcherTimer? _ticker;
    private DispatcherTimer? _topmostTicker;
    private DispatcherTimer? _rowReminderTimer;
    private bool _isCompact;
    private bool _initializingUi = true;
    private bool _isClosing;
    private bool _editingDraft;
    private bool _alarmApplied;
    private bool _cancelAlarmEdit;
    private bool _alarmFieldsDirty;
    private bool _temporaryTopmostActive;
    private string? _editingTaskId;
    private string? _activeReminderTaskId;
    private Border? _activeReminderBorder;
    private ReminderWindow? _activeDesktopReminder;
    private Brush? _activeReminderBackground;
    private Brush? _activeReminderBorderBrush;
    private Thickness _activeReminderBorderThickness;
    private Effect? _activeReminderEffect;
    private int _activeReminderZIndex;
    private readonly Queue<string> _pendingReminderIds = new();
    private double _normalWidth = 380;
    private double _normalHeight = 220;
    private double _normalLeft;
    private double _normalTop;
    private string _currentDay = DateTime.Today.ToString("yyyy-MM-dd");

    public MainWindow(bool startMinimized)
    {
        _startMinimized = startMinimized;
        _dataDirectory = AppPaths.ResolveDataDirectory(Environment.GetCommandLineArgs().Skip(1).ToArray());
        AppPaths.Initialize(_dataDirectory);
        _dataFile = Path.Combine(_dataDirectory, "tasks.json");

        InitializeComponent();
        LoadData();
        ConfigureInitialWindow();
        InitializeUi();
        _initializingUi = false;
    }

    private void ConfigureInitialWindow()
    {
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(MinWindowWidth, workArea.Width - 44);
        var maxHeight = Math.Max(MinWindowHeight, workArea.Height - 54);
        var savedWidth = _data.Settings.WindowWidth > 0 ? _data.Settings.WindowWidth : 380;
        var savedHeight = _data.Settings.WindowHeight > 0 ? _data.Settings.WindowHeight : 220;

        _normalWidth = Math.Clamp(savedWidth, MinWindowWidth, maxWidth);
        _normalHeight = Math.Clamp(savedHeight, MinWindowHeight, maxHeight);
        Width = _normalWidth;
        Height = _normalHeight;
        MinWidth = MinWindowWidth;
        MinHeight = MinWindowHeight;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2 - 16);
        _normalLeft = Left;
        _normalTop = Top;
    }

    private void InitializeUi()
    {
        TaskItems.ItemsSource = _rows;
        TimeTextBox.Text = NextRoundTime();
        DateTextBox.Text = DateTime.Today.ToString("yyyy-MM-dd");
        TopmostCheckBox.IsChecked = _data.Settings.AlwaysOnTop;
        AutostartCheckBox.IsChecked = AutostartEnabled();
        ApplyAppearance();
        UpdateDateVisibility();
        UpdateDraftSummary();
        UpdateResponsiveLayout();
        RefreshView();
        ShowDraftRow();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateHeaderDate();
        _ticker = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _ticker.Tick += Ticker_Tick;
        _ticker.Start();

        _topmostTicker = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _topmostTicker.Tick += (_, _) =>
        {
            if ((_data.Settings.AlwaysOnTop || _temporaryTopmostActive)
                && _activeDesktopReminder is null && !AlarmPopup.IsOpen && !SettingsPopup.IsOpen)
            {
                RefreshWindowTopmost();
            }
        };
        _topmostTicker.Start();

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (_startMinimized)
            {
                EnterCompactMode();
            }
            ApplyWindowTopmost();
            CheckDueTasks();
        }));
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_isClosing && (_data.Settings.AlwaysOnTop || _temporaryTopmostActive)
                && _activeDesktopReminder is null && !AlarmPopup.IsOpen && !SettingsPopup.IsOpen)
            {
                RefreshWindowTopmost();
            }
        }));
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        ApplyWindowTopmost();
    }

    private void ApplyWindowTopmost(bool bringToFront = false)
    {
        var enabled = _data.Settings.AlwaysOnTop;
        Topmost = enabled;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        const int hwndTopmost = -1;
        const int hwndNotTopmost = -2;
        const uint noSize = 0x0001;
        const uint noMove = 0x0002;
        const uint showWindow = 0x0040;
        const uint noActivate = 0x0010;

        var applied = SetWindowPos(
            handle,
            new IntPtr(enabled ? hwndTopmost : hwndNotTopmost),
            0,
            0,
            0,
            0,
            noSize | noMove | (bringToFront ? showWindow : noActivate));

        if (!applied && IsLoaded)
        {
            SetStatus($"置顶失败：{Marshal.GetLastWin32Error()}", true);
        }

        if (bringToFront && enabled)
        {
            Activate();
            SetForegroundWindow(handle);
        }
    }

    private void RefreshWindowTopmost()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        const int hwndTopmost = -1;
        const uint noSize = 0x0001;
        const uint noMove = 0x0002;
        const uint noActivate = 0x0010;
        SetWindowPos(
            handle,
            new IntPtr(hwndTopmost),
            0,
            0,
            0,
            0,
            noSize | noMove | noActivate);
    }

    private void BringWindowToFront()
    {
        if (_isCompact)
        {
            RestoreFromCompact();
        }

        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Topmost = true;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            const int hwndTopmost = -1;
            const uint noSize = 0x0001;
            const uint noMove = 0x0002;
            const uint showWindow = 0x0040;
            SetWindowPos(
                handle,
                new IntPtr(hwndTopmost),
                0,
                0,
                0,
                0,
                noSize | noMove | showWindow);
            SetForegroundWindow(handle);
        }

        Activate();
        _temporaryTopmostActive = !_data.Settings.AlwaysOnTop;
    }

    internal void ShowPanel()
    {
        BringWindowToFront();
        if (_activeReminderTaskId is null)
        {
            RestoreConfiguredTopmost();
        }
    }

    private void RestoreConfiguredTopmost()
    {
        if (!_temporaryTopmostActive || _data.Settings.AlwaysOnTop)
        {
            _temporaryTopmostActive = false;
            return;
        }

        _temporaryTopmostActive = false;
        Topmost = false;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        const int hwndNotTopmost = -2;
        const uint noSize = 0x0001;
        const uint noMove = 0x0002;
        const uint noActivate = 0x0010;
        SetWindowPos(
            handle,
            new IntPtr(hwndNotTopmost),
            0,
            0,
            0,
            0,
            noSize | noMove | noActivate);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        AlarmPopup.IsOpen = false;
        SettingsPopup.IsOpen = false;
        _ticker?.Stop();
        _topmostTicker?.Stop();
        _rowReminderTimer?.Stop();
        _activeDesktopReminder?.Close();
        SaveData();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6)
        {
            PreviewReminder_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && (AlarmPopup.IsOpen || SettingsPopup.IsOpen))
        {
            _cancelAlarmEdit = true;
            AlarmPopup.IsOpen = false;
            SettingsPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_isCompact)
            {
                RestoreFromCompact();
            }
            ShowDraftRow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _isCompact)
        {
            RestoreFromCompact();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && DraftRow.Visibility == Visibility.Visible)
        {
            CancelDraft();
            e.Handled = true;
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initializingUi)
        {
            UpdateResponsiveLayout();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        EnterCompactMode();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RestoreCompact_Click(object sender, RoutedEventArgs e)
    {
        RestoreFromCompact();
    }

    private void EnterCompactMode()
    {
        if (_isCompact || _isClosing)
        {
            return;
        }

        AlarmPopup.IsOpen = false;
        SettingsPopup.IsOpen = false;
        SaveData();
        _normalWidth = Width;
        _normalHeight = Height;
        _normalLeft = Left;
        _normalTop = Top;
        _isCompact = true;

        MinWidth = 0;
        MinHeight = 0;
        MainContent.Visibility = Visibility.Collapsed;
        CompactContent.Visibility = Visibility.Visible;
        SetResizeHandlesVisible(false);

        var workArea = SystemParameters.WorkArea;
        Width = CompactWidth;
        Height = CompactHeight;
        Left = workArea.Right - CompactWidth - 22;
        Top = workArea.Top + 24;
        UpdateCompactText();
        ApplyWindowTopmost();
    }

    private void RestoreFromCompact()
    {
        if (!_isCompact || _isClosing)
        {
            return;
        }

        _isCompact = false;
        CompactContent.Visibility = Visibility.Collapsed;
        MainContent.Visibility = Visibility.Visible;
        MinWidth = MinWindowWidth;
        MinHeight = MinWindowHeight;
        SetResizeHandlesVisible(true);

        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(_normalWidth, MinWindowWidth, Math.Max(MinWindowWidth, workArea.Width - 44));
        Height = Math.Clamp(_normalHeight, MinWindowHeight, Math.Max(MinWindowHeight, workArea.Height - 54));
        Left = Math.Clamp(_normalLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(_normalTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        Activate();
        ApplyWindowTopmost(_data.Settings.AlwaysOnTop);
        if (DraftRow.Visibility == Visibility.Visible)
        {
            TitleTextBox.Focus();
        }
        else
        {
            AddRowButton.Focus();
        }
    }

    private void SetResizeHandlesVisible(bool visible)
    {
        var state = visible ? Visibility.Visible : Visibility.Collapsed;
        ResizeLeft.Visibility = state;
        ResizeRight.Visibility = state;
        ResizeTop.Visibility = state;
        ResizeBottom.Visibility = state;
        ResizeTopLeft.Visibility = state;
        ResizeTopRight.Visibility = state;
        ResizeBottomLeft.Visibility = state;
        ResizeBottomRight.Visibility = state;
    }

    private void UpdateCompactText()
    {
        var pending = _data.Tasks.Count(task => !IsDoneToday(task));
        CompactPendingText.Text = pending == 0
            ? "今天已清空 · 点击展开"
            : $"{pending} 项未完成 · 点击展开";
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TitlePlaceholder is not null)
        {
            TitlePlaceholder.Visibility = string.IsNullOrEmpty(TitleTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void DraftTitle_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTaskFromInputs();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelDraft();
            e.Handled = true;
        }
    }

    private void AlarmInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryApplyAlarmEditor(true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _cancelAlarmEdit = true;
            AlarmPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        ShowDraftRow();
    }

    private void AddDraft_Click(object sender, RoutedEventArgs e)
    {
        AddTaskFromInputs();
        e.Handled = true;
    }

    private void ShowDraftRow()
    {
        AlarmPopup.IsOpen = false;
        SettingsPopup.IsOpen = false;

        var createdDraft = false;
        if (_draftTask is null)
        {
            createdDraft = true;
            _draftTask = new ReminderTask
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "",
                Time = "",
                Repeat = "daily",
                Date = "",
                StartDate = "",
                Done = "",
                LastFired = "",
                Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var previousInitializing = _initializingUi;
            _initializingUi = true;
            TimeTextBox.Text = "";
            DateTextBox.Text = DateTime.Today.ToString("yyyy-MM-dd");
            DailyRadio.IsChecked = true;
            UpdateDateVisibility();
            _initializingUi = previousInitializing;
        }

        if (createdDraft)
        {
            TitleTextBox.Clear();
        }

        DraftRow.Visibility = Visibility.Visible;
        UpdateDraftSummary();
        if (!_initializingUi)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                TaskScroll.ScrollToEnd();
                TitleTextBox.Focus();
                TitleTextBox.CaretIndex = TitleTextBox.Text.Length;
            }));
        }
    }

    private void CancelDraft_Click(object sender, RoutedEventArgs e)
    {
        CancelDraft();
        e.Handled = true;
    }

    private void CancelDraft()
    {
        if (AlarmPopup.IsOpen && _editingDraft)
        {
            _cancelAlarmEdit = true;
            AlarmPopup.IsOpen = false;
        }

        _draftTask = null;
        _editingTaskId = null;
        _editingDraft = false;
        TitleTextBox.Clear();
        ShowDraftRow();
    }

    private void AddTaskFromInputs()
    {
        if (_draftTask is null)
        {
            return;
        }

        var title = TitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            SetStatus("先写点内容", true);
            TitleTextBox.Focus();
            return;
        }

        var now = CurrentMinute();
        var today = now.Date;
        var hasReminder = !string.IsNullOrWhiteSpace(_draftTask.Time);
        var hour = 0;
        var minute = 0;
        var isOnce = false;
        DateTime targetDate = today;

        if (hasReminder && !TryParseTime(_draftTask.Time, out hour, out minute))
        {
            SetStatus("时间写成 09:30 这样", true);
            OpenAlarmEditor(_draftTask, AlarmButton, true);
            return;
        }

        if (hasReminder)
        {
            isOnce = string.Equals(_draftTask.Repeat, "once", StringComparison.OrdinalIgnoreCase);
            if (isOnce)
            {
                if (!TryParseDate(_draftTask.Date, out targetDate))
                {
                    SetStatus("日期写成 2026-11-19 这样", true);
                    OpenAlarmEditor(_draftTask, AlarmButton, true);
                    return;
                }

                var scheduled = targetDate.Date.AddHours(hour).AddMinutes(minute);
                if (scheduled < now)
                {
                    SetStatus("这个时间已经过去了", true);
                    OpenAlarmEditor(_draftTask, AlarmButton, true);
                    return;
                }
            }
        }

        var scheduledToday = today.AddHours(hour).AddMinutes(minute);
        var startDate = hasReminder
            ? (scheduledToday < now ? today.AddDays(1) : today)
            : today;
        var task = new ReminderTask
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Time = hasReminder ? $"{hour:00}:{minute:00}" : "",
            Repeat = hasReminder ? (isOnce ? "once" : "daily") : "",
            Date = hasReminder && isOnce ? targetDate.ToString("yyyy-MM-dd") : "",
            StartDate = hasReminder && !isOnce ? startDate.ToString("yyyy-MM-dd") : "",
            Done = "",
            LastFired = "",
            Created = now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        _data.Tasks.Add(task);
        AlarmPopup.IsOpen = false;
        _draftTask = null;
        SaveData();
        RefreshView();
        TitleTextBox.Clear();
        ShowDraftRow();
        SetStatus(!hasReminder ? "已加入清单，未设置提醒" : isOnce ? "已加入日程" : "已加入今天");
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(CheckDueTasks));
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string taskId })
        {
            return;
        }

        if (string.Equals(_activeReminderTaskId, taskId, StringComparison.Ordinal))
        {
            StopActiveRowReminder();
        }

        _data.Tasks.RemoveAll(task => task.Id == taskId);
        SaveData();
        RefreshView();
        SetStatus("已删除");
        e.Handled = true;
    }

    private void TaskRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is not FrameworkElement { DataContext: TaskRow row })
        {
            return;
        }

        if (string.Equals(_activeReminderTaskId, row.Id, StringComparison.Ordinal))
        {
            StopActiveRowReminder();
            SetStatus("提醒已关闭");
            e.Handled = true;
            return;
        }

        ToggleTask(row.Id);
        e.Handled = true;
    }

    private void TaskRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskRow row }
            && string.Equals(_activeReminderTaskId, row.Id, StringComparison.Ordinal))
        {
            StopActiveRowReminder();
            SetStatus("提醒已关闭");
            e.Handled = true;
        }
    }

    private void CompleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string taskId })
        {
            ToggleTask(taskId);
            e.Handled = true;
        }
    }

    private void RowTitle_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TaskRow row } editor)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            SaveRowTitle(editor, row);
            ShowDraftRow();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            editor.Text = row.Title;
            TitleTextBox.Focus();
            e.Handled = true;
        }
    }

    private void RowTitle_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isClosing && sender is TextBox { DataContext: TaskRow row } editor)
        {
            SaveRowTitle(editor, row);
        }
    }

    private void SaveRowTitle(TextBox editor, TaskRow row)
    {
        var title = editor.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            editor.Text = row.Title;
            return;
        }

        if (string.Equals(title, row.Title, StringComparison.Ordinal))
        {
            return;
        }

        var task = _data.Tasks.FirstOrDefault(item => item.Id == row.Id);
        if (task is null)
        {
            return;
        }

        task.Title = title;
        row.Title = title;
        SaveData();
    }

    private void ToggleTask(string taskId)
    {
        if (string.Equals(_activeReminderTaskId, taskId, StringComparison.Ordinal))
        {
            StopActiveRowReminder();
        }

        var task = _data.Tasks.FirstOrDefault(item => item.Id == taskId);
        if (task is null)
        {
            return;
        }

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (string.Equals(task.Repeat, "daily", StringComparison.OrdinalIgnoreCase))
        {
            task.Done = string.Equals(task.Done, today, StringComparison.Ordinal)
                ? ""
                : today;
        }
        else
        {
            task.Done = string.IsNullOrEmpty(task.Done) ? "1" : "";
        }

        SaveData();
        RefreshView();
    }

    private void Repeat_Checked(object sender, RoutedEventArgs e)
    {
        if (DateSettingRow is null || TimeTextBox is null)
        {
            return;
        }

        UpdateDateVisibility();
        if (!_initializingUi)
        {
            _alarmFieldsDirty = true;
        }
    }

    private void UpdateDateVisibility()
    {
        DateSettingRow.Visibility = OnceRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void DraftAlarm_Click(object sender, RoutedEventArgs e)
    {
        if (_draftTask is null || sender is not Button button)
        {
            return;
        }

        OpenAlarmEditor(_draftTask, button, true);
        e.Handled = true;
    }

    private void RowAlarm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string taskId } button)
        {
            return;
        }

        var task = _data.Tasks.FirstOrDefault(item => item.Id == taskId);
        if (task is null)
        {
            return;
        }

        OpenAlarmEditor(task, button, false);
        e.Handled = true;
    }

    private void OpenAlarmEditor(ReminderTask task, Button target, bool isDraft)
    {
        _editingDraft = isDraft;
        _editingTaskId = isDraft ? null : task.Id;
        _alarmApplied = false;
        _cancelAlarmEdit = false;
        _alarmFieldsDirty = false;
        AlarmErrorText.Visibility = Visibility.Collapsed;
        AlarmErrorText.Text = "";

        var previousInitializing = _initializingUi;
        _initializingUi = true;
        TimeTextBox.Text = task.Time;
        DateTextBox.Text = TryParseDate(task.Date, out var taskDate)
            ? taskDate.ToString("yyyy-MM-dd")
            : DateTime.Today.ToString("yyyy-MM-dd");
        if (string.Equals(task.Repeat, "once", StringComparison.OrdinalIgnoreCase))
        {
            OnceRadio.IsChecked = true;
        }
        else
        {
            DailyRadio.IsChecked = true;
        }
        UpdateDateVisibility();
        _initializingUi = previousInitializing;

        AlarmPopup.PlacementTarget = target;
        AlarmPopup.HorizontalOffset = -234;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            AlarmPopup.IsOpen = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                TimeTextBox.Focus();
                TimeTextBox.SelectAll();
            }));
        }));
    }

    private void AlarmField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializingUi)
        {
            _alarmFieldsDirty = true;
        }
    }

    private void AlarmPopup_Closed(object? sender, EventArgs e)
    {
        if (_cancelAlarmEdit)
        {
            ResetAlarmEditorState();
            return;
        }

        if (_isClosing)
        {
            ResetAlarmEditorState();
            return;
        }

        if (!_alarmApplied && _alarmFieldsDirty)
        {
            TryApplyAlarmEditor(false);
        }

        ResetAlarmEditorState();
    }

    private void SaveAlarm_Click(object sender, RoutedEventArgs e)
    {
        TryApplyAlarmEditor(true);
        e.Handled = true;
    }

    private void ClearAlarm_Click(object sender, RoutedEventArgs e)
    {
        ApplyNoAlarm();
        e.Handled = true;
    }

    private void ApplyNoAlarm()
    {
        ReminderTask? target = _editingDraft
            ? _draftTask
            : _data.Tasks.FirstOrDefault(task => task.Id == _editingTaskId);
        if (target is null)
        {
            SetAlarmError("没有找到这一行");
            return;
        }

        target.Time = "";
        target.Repeat = "";
        target.Date = "";
        target.StartDate = "";
        target.LastFired = "";
        var editingDraft = _editingDraft;
        _alarmApplied = true;
        AlarmPopup.IsOpen = false;
        if (editingDraft)
        {
            UpdateDraftSummary();
        }
        else
        {
            SaveData();
            RefreshView();
        }

        SetStatus("这一行不设置提醒");
    }

    private bool TryApplyAlarmEditor(bool closeOnSuccess)
    {
        ReminderTask? target = _editingDraft
            ? _draftTask
            : _data.Tasks.FirstOrDefault(task => task.Id == _editingTaskId);
        if (target is null)
        {
            SetAlarmError("没有找到这一行");
            return false;
        }

        if (string.IsNullOrWhiteSpace(TimeTextBox.Text))
        {
            ApplyNoAlarm();
            return true;
        }

        if (!TryParseTime(TimeTextBox.Text, out var hour, out var minute))
        {
            SetAlarmError("时间写成 09:30 这样");
            SetStatus("时间写成 09:30 这样", true);
            return false;
        }

        var now = CurrentMinute();
        var today = now.Date;
        var time = $"{hour:00}:{minute:00}";
        var isOnce = OnceRadio.IsChecked == true;
        var targetDate = today;
        if (isOnce && !TryParseDate(DateTextBox.Text, out targetDate))
        {
            SetAlarmError("日期写成 2026-11-19 这样");
            SetStatus("日期写成 2026-11-19 这样", true);
            return false;
        }

        var scheduled = targetDate.Date.AddHours(hour).AddMinutes(minute);
        if (isOnce && scheduled < now)
        {
            SetAlarmError("这个时间已经过去了");
            SetStatus("这个时间已经过去了", true);
            return false;
        }

        target.Time = time;
        target.Repeat = isOnce ? "once" : "daily";
        target.LastFired = "";
        target.Done = "";
        if (isOnce)
        {
            target.Date = targetDate.ToString("yyyy-MM-dd");
            target.StartDate = "";
        }
        else
        {
            target.Date = "";
            var scheduledToday = today.AddHours(hour).AddMinutes(minute);
            target.StartDate = (scheduledToday < now ? today.AddDays(1) : today)
                .ToString("yyyy-MM-dd");
        }

        var editingDraft = _editingDraft;
        _alarmApplied = true;
        AlarmErrorText.Visibility = Visibility.Collapsed;
        AlarmErrorText.Text = "";
        if (closeOnSuccess)
        {
            AlarmPopup.IsOpen = false;
        }

        if (editingDraft)
        {
            UpdateDraftSummary();
            SetStatus("这一行的提醒已更新");
        }
        else
        {
            SaveData();
            RefreshView();
            SetStatus("这一行的提醒已更新");
        }

        _editingTaskId = null;
        _editingDraft = false;

        if (!editingDraft)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(CheckDueTasks));
        }

        return true;
    }

    private void SetAlarmError(string message)
    {
        AlarmErrorText.Text = message;
        AlarmErrorText.Visibility = Visibility.Visible;
    }

    private void ResetAlarmEditorState()
    {
        _alarmApplied = false;
        _cancelAlarmEdit = false;
        _alarmFieldsDirty = false;
        _editingTaskId = null;
        _editingDraft = false;
    }

    private void UpdateDraftSummary()
    {
        if (_draftTask is null || AlarmButton is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_draftTask.Time))
        {
            AlarmButton.ToolTip = "设置提醒（可选，当前不提醒）";
            return;
        }

        var repeat = string.Equals(_draftTask.Repeat, "once", StringComparison.OrdinalIgnoreCase)
            ? "仅一次"
            : "每天";
        AlarmButton.ToolTip = $"修改提醒：{_draftTask.Time} · {repeat}";
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompact)
        {
            RestoreFromCompact();
        }

        SettingsPopup.IsOpen = !SettingsPopup.IsOpen;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_isCompact)
        {
            RestoreFromCompact();
        }

        SettingsPopup.IsOpen = true;
    }

    private void SettingsPopup_Closed(object? sender, EventArgs e)
    {
        SaveData();
        SetStatus("样式与设置已保存");
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializingUi || sender is not RadioButton { Tag: string themeKey })
        {
            return;
        }

        if (!ThemePalettes.ContainsKey(themeKey))
        {
            return;
        }

        _data.Settings.Theme = themeKey;
        ApplyAppearance();
        SaveData();
        SetStatus("已切换面板样式");
    }

    private void ChooseBackground_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "选择待办背景",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            CheckFileExists = true
        };
        SettingsPopup.IsOpen = false;
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            LoadBackgroundBitmap(picker.FileName);
            Directory.CreateDirectory(_dataDirectory);
            var fileName = "background" + Path.GetExtension(picker.FileName).ToLowerInvariant();
            var destination = Path.Combine(_dataDirectory, fileName);
            if (!string.Equals(Path.GetFullPath(picker.FileName), destination, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(picker.FileName, destination, true);
            }
            _data.Settings.BackgroundImage = fileName;
            ApplyBackground();
            SaveData();
            SetStatus("已设置自定义背景");
        }
        catch (Exception exception)
        {
            SetStatus($"背景设置失败：{exception.Message}", true);
        }
    }

    private static BitmapImage LoadBackgroundBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 1600;
        bitmap.UriSource = new Uri(Path.GetFullPath(path));
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        _data.Settings.BackgroundImage = "";
        ApplyBackground();
        SaveData();
        SetStatus("已恢复玻璃背景");
    }

    private void ApplyBackground()
    {
        BackgroundImageLayer.Background = null;
        BackgroundImageLayer.Visibility = Visibility.Collapsed;
        BackgroundNameText.Text = "玻璃背景";
        if (string.IsNullOrWhiteSpace(_data.Settings.BackgroundImage))
        {
            return;
        }

        try
        {
            var path = Path.IsPathRooted(_data.Settings.BackgroundImage)
                ? _data.Settings.BackgroundImage
                : Path.Combine(_dataDirectory, _data.Settings.BackgroundImage);
            var brush = new ImageBrush(LoadBackgroundBitmap(path)) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            BackgroundImageLayer.Background = brush;
            BackgroundImageLayer.Opacity = Math.Clamp(_data.Settings.BackgroundOpacity, 0.1, 1);
            BackgroundImageLayer.Visibility = Visibility.Visible;
            BackgroundNameText.Text = Path.GetFileName(path);
        }
        catch (Exception exception)
        {
            SetStatus($"背景无法读取：{exception.Message}", true);
        }
    }

    private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializingUi)
        {
            return;
        }
        _data.Settings.BackgroundOpacity = BackgroundOpacitySlider.Value / 100;
        BackgroundImageLayer.Opacity = _data.Settings.BackgroundOpacity;
    }

    private void ReminderStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializingUi || sender is not RadioButton { Tag: string style })
        {
            return;
        }
        _data.Settings.ReminderStyle = style;
        SaveData();
    }

    private void PreviewReminder_Click(object sender, RoutedEventArgs e)
    {
        var task = _data.Tasks.FirstOrDefault(item => !IsDoneToday(item)) ?? _data.Tasks.FirstOrDefault();
        if (task is null)
        {
            SetStatus("先写一条待办，再预览提醒");
            return;
        }
        SettingsPopup.IsOpen = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ShowReminder(task)));
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializingUi)
        {
            return;
        }

        _data.Settings.GlassOpacity = Math.Clamp(OpacitySlider.Value / 100, 0.55, 0.96);
        OpacityValueText.Text = $"{OpacitySlider.Value:0}%";
        ApplyAppearance();
        SetStatus("已调整玻璃透明度");
    }

    private void PanelContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (string.Equals(item.Tag as string, "topmost", StringComparison.Ordinal))
            {
                item.IsChecked = _data.Settings.AlwaysOnTop;
            }
            else if (string.Equals(item.Tag as string, "autostart", StringComparison.Ordinal))
            {
                item.IsChecked = AutostartEnabled();
            }
        }
    }

    private void MenuTopmost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            SetTopmostSetting(item.IsChecked);
        }
    }

    private void MenuAutostart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            SetAutostartSetting(item.IsChecked);
        }
    }

    private void TopmostCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingUi)
        {
            return;
        }

        SetTopmostSetting(TopmostCheckBox.IsChecked == true);
    }

    private void SetTopmostSetting(bool enabled)
    {
        _data.Settings.AlwaysOnTop = enabled;
        if (!enabled)
        {
            _temporaryTopmostActive = false;
        }

        _initializingUi = true;
        TopmostCheckBox.IsChecked = enabled;
        _initializingUi = false;
        ApplyWindowTopmost(enabled);
        SaveData();
        SetStatus(_data.Settings.AlwaysOnTop ? "窗口已置顶并显示到最前" : "已取消置顶");
    }

    private void AutostartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingUi)
        {
            return;
        }

        SetAutostartSetting(AutostartCheckBox.IsChecked == true);
    }

    private void SetAutostartSetting(bool enabled)
    {
        try
        {
            if (enabled)
            {
                EnableAutostart();
                SetStatus("已开启开机自启");
            }
            else
            {
                DisableAutostart();
                SetStatus("已关闭开机自启");
            }
        }
        catch (Exception exception)
        {
            SetStatus($"自启设置失败：{exception.Message}", true);
        }

        _initializingUi = true;
        AutostartCheckBox.IsChecked = AutostartEnabled();
        _initializingUi = false;
    }

    private void Ticker_Tick(object? sender, EventArgs e)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (!string.Equals(today, _currentDay, StringComparison.Ordinal))
        {
            _currentDay = today;
            UpdateHeaderDate();
            RefreshView();
        }

        CheckDueTasks();
    }

    private void CheckDueTasks()
    {
        var now = DateTime.Now;
        var today = now.Date;
        var todayText = today.ToString("yyyy-MM-dd");
        var changed = false;
        var dueTasks = new List<ReminderTask>();

        foreach (var task in _data.Tasks.ToList())
        {
            if (!TryParseTime(task.Time, out var hour, out var minute))
            {
                continue;
            }

            if (string.Equals(task.Repeat, "once", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseDate(task.Date, out var targetDate) || !string.IsNullOrEmpty(task.Done))
                {
                    continue;
                }

                var scheduled = targetDate.Date.AddHours(hour).AddMinutes(minute);
                var fireKey = scheduled.ToString("yyyy-MM-dd HH:mm");
                if (string.Equals(task.LastFired, fireKey, StringComparison.Ordinal) || now < scheduled)
                {
                    continue;
                }

                task.LastFired = fireKey;
                changed = true;
                if ((now - scheduled).TotalHours <= 12)
                {
                    dueTasks.Add(task);
                }
                continue;
            }

            if (!TryParseDate(task.StartDate, out var startDate))
            {
                startDate = today;
            }

            if (today < startDate.Date
                || string.Equals(task.Done, todayText, StringComparison.Ordinal)
                || string.Equals(task.LastFired, todayText, StringComparison.Ordinal))
            {
                continue;
            }

            var scheduledToday = today.AddHours(hour).AddMinutes(minute);
            if (now >= scheduledToday && (now - scheduledToday).TotalHours <= 12)
            {
                task.LastFired = todayText;
                changed = true;
                dueTasks.Add(task);
            }
        }

        if (changed)
        {
            SaveData();
            if (_activeReminderTaskId is null)
            {
                RefreshView();
            }
        }

        foreach (var task in dueTasks)
        {
            if (!_pendingReminderIds.Contains(task.Id)
                && !string.Equals(_activeReminderTaskId, task.Id, StringComparison.Ordinal))
            {
                _pendingReminderIds.Enqueue(task.Id);
            }
        }
        StartNextRowReminder();
    }

    private void StartNextRowReminder()
    {
        if (_isClosing || _activeReminderTaskId is not null)
        {
            return;
        }

        while (_pendingReminderIds.TryDequeue(out var taskId))
        {
            var task = _data.Tasks.FirstOrDefault(item => item.Id == taskId);
            if (task is null || string.IsNullOrWhiteSpace(task.Time) || IsDoneToday(task))
            {
                continue;
            }

            ShowReminder(task);
            return;
        }
    }

    private void ShowReminder(ReminderTask task, bool retry = true)
    {
        StopActiveRowReminder(false);
        BringWindowToFront();

        var row = _rows.FirstOrDefault(item => item.Id == task.Id);
        if (row is null)
        {
            SetStatus($"提醒：{task.Title}", false, true);
            return;
        }

        TaskItems.UpdateLayout();
        TaskScroll.UpdateLayout();
        var container = TaskItems.ItemContainerGenerator.ContainerFromItem(row) as DependencyObject;
        var surface = FindDescendant<Border>(container, "TaskRowBorder");
        if (surface is null)
        {
            RestoreConfiguredTopmost();
            if (retry)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ShowReminder(task, false)));
                return;
            }
            SetStatus($"提醒：{task.Title}", false, true);
            return;
        }

        surface.BringIntoView();
        _activeReminderTaskId = task.Id;
        _activeReminderBorder = surface;
        _activeReminderBackground = surface.Background;
        _activeReminderBorderBrush = surface.BorderBrush;
        _activeReminderBorderThickness = surface.BorderThickness;
        _activeReminderEffect = surface.Effect;
        _activeReminderZIndex = Panel.GetZIndex(surface);

        var accent = GetActiveTheme().Accent;
        surface.Background = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B));
        surface.BorderBrush = new SolidColorBrush(accent);
        surface.BorderThickness = new Thickness(0, 0, 0, 2);
        surface.Effect = new DropShadowEffect
        {
            Color = accent,
            BlurRadius = 12,
            ShadowDepth = 0,
            Opacity = 0.78
        };
        Panel.SetZIndex(surface, 30);

        var origin = new Rect(surface.PointToScreen(new Point(0, 0)),
            surface.PointToScreen(new Point(surface.ActualWidth, surface.ActualHeight)));
        var desktopReminder = new ReminderWindow(
            task.Title, task.Time, _data.Settings.ReminderStyle, accent, origin)
        {
            Owner = this
        };
        _activeDesktopReminder = desktopReminder;
        desktopReminder.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeDesktopReminder, desktopReminder))
            {
                _activeDesktopReminder = null;
                StopActiveRowReminder();
            }
        };
        desktopReminder.Show();

        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch (Exception)
        {
        }

        _rowReminderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _rowReminderTimer.Tick += (_, _) =>
        {
            StopActiveRowReminder();
            SetStatus("提醒已关闭");
        };
        _rowReminderTimer.Start();
        SetStatus($"提醒：{task.Title}", false, true);
    }

    private void StopActiveRowReminder(bool showNext = true)
    {
        _rowReminderTimer?.Stop();
        _rowReminderTimer = null;
        var desktopReminder = _activeDesktopReminder;
        _activeDesktopReminder = null;
        desktopReminder?.Close();

        if (_activeReminderBorder is not null)
        {
            _activeReminderBorder.BeginAnimation(MinHeightProperty, null);
            _activeReminderBorder.BeginAnimation(OpacityProperty, null);
            _activeReminderBorder.Background = _activeReminderBackground;
            _activeReminderBorder.BorderBrush = _activeReminderBorderBrush;
            _activeReminderBorder.BorderThickness = _activeReminderBorderThickness;
            _activeReminderBorder.Effect = _activeReminderEffect;
            Panel.SetZIndex(_activeReminderBorder, _activeReminderZIndex);
            var emphasis = FindDescendant<TextBlock>(_activeReminderBorder, "ReminderEmphasis");
            if (emphasis is not null)
            {
                emphasis.Visibility = Visibility.Collapsed;
            }
        }

        _activeReminderTaskId = null;
        _activeReminderBorder = null;
        _activeReminderBackground = null;
        _activeReminderBorderBrush = null;
        _activeReminderEffect = null;
        RestoreConfiguredTopmost();
        if (showNext && !_isClosing)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(StartNextRowReminder));
        }
    }

    private void RefreshView()
    {
        if (_activeReminderTaskId is not null)
        {
            StopActiveRowReminder();
        }

        var sortedTasks = _data.Tasks
            .OrderBy(task => string.IsNullOrWhiteSpace(task.Time) ? 1 : 0)
            .ThenBy(task => task.Time, StringComparer.Ordinal)
            .ThenBy(task => task.Title, StringComparer.CurrentCulture)
            .ToList();

        _rows.Clear();
        foreach (var task in sortedTasks)
        {
            var isDone = IsDoneToday(task);
            var hasReminder = !string.IsNullOrWhiteSpace(task.Time);
            var repeatText = hasReminder
                ? string.Equals(task.Repeat, "daily", StringComparison.OrdinalIgnoreCase)
                    ? $"每天 · {GetTaskStatus(task)}"
                    : $"仅一次 · {task.Date} · {GetTaskStatus(task)}"
                : isDone
                    ? "已完成"
                    : "不提醒";
            _rows.Add(new TaskRow
            {
                Id = task.Id,
                Time = hasReminder ? task.Time : "—",
                Title = task.Title,
                Meta = repeatText,
                IsDone = isDone,
                HasReminder = hasReminder
            });
        }

        var doneCount = _data.Tasks.Count(IsDoneToday);
        TaskCountText.Text = _data.Tasks.Count == 0
            ? "空白"
            : $"{_data.Tasks.Count} 项  ·  {doneCount} 已完成";
        EmptyState.Visibility = Visibility.Collapsed;

        UpdateCompactText();
    }

    private string GetTaskStatus(ReminderTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Time))
        {
            return "不提醒";
        }

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (string.Equals(task.Repeat, "daily", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(task.Done, today, StringComparison.Ordinal))
            {
                return "今日已完成";
            }

            if (TryParseDate(task.StartDate, out var startDate) && startDate.Date > DateTime.Today)
            {
                return $"{startDate:yyyy-MM-dd} 开始";
            }

            return string.Equals(task.LastFired, today, StringComparison.Ordinal)
                ? "已提醒"
                : "待提醒";
        }

        if (!string.IsNullOrEmpty(task.Done))
        {
            return "已完成";
        }

        return string.IsNullOrEmpty(task.LastFired) ? "待提醒" : "已提醒";
    }

    private bool IsDoneToday(ReminderTask task)
    {
        if (string.Equals(task.Repeat, "daily", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                task.Done,
                DateTime.Today.ToString("yyyy-MM-dd"),
                StringComparison.Ordinal);
        }

        return !string.IsNullOrEmpty(task.Done);
    }

    private void UpdateHeaderDate()
    {
        var now = DateTime.Now;
        const string weekdays = "日一二三四五六";
        DateText.Text = $"{now:yyyy年MM月dd日}  ·  周{weekdays[(int)now.DayOfWeek]}";
    }

    private ThemePalette GetActiveTheme()
    {
        var themeKey = string.IsNullOrWhiteSpace(_data.Settings.Theme)
            ? "night"
            : _data.Settings.Theme;
        return ThemePalettes.TryGetValue(themeKey, out var theme)
            ? theme
            : ThemePalettes["night"];
    }

    private void ApplyAppearance()
    {
        var previousInitializing = _initializingUi;
        _initializingUi = true;
        try
        {
            var theme = GetActiveTheme();
            var opacity = Math.Clamp(
                _data.Settings.GlassOpacity is >= 0.55 and <= 0.96
                    ? _data.Settings.GlassOpacity
                    : 0.88,
                0.55,
                0.96);
            _data.Settings.GlassOpacity = opacity;
            _data.Settings.Theme = ThemePalettes.FirstOrDefault(pair =>
                ReferenceEquals(pair.Value, theme)).Key ?? "night";

            SetBrushResource("AccentBrush", theme.Accent);
            SetBrushResource("AccentBrightBrush", Mix(theme.Accent, Colors.White, 0.58));
            SetBrushResource("AccentPressedBrush", Mix(theme.Accent, Colors.White, 0.38));
            SetBrushResource("AccentGlowBrush", WithAlpha(theme.Accent, 0x30));
            SetBrushResource("AccentHoverGlowBrush", WithAlpha(theme.Accent, 0x5A));
            SetBrushResource("AccentBorderBrush", WithAlpha(theme.Accent, 0x37));
            SetBrushResource("AccentDimBrush", WithAlpha(theme.Accent, 0x4B));
            SetBrushResource("AccentRingBrush", WithAlpha(theme.Accent, 0x31));
            SetBrushResource("SelectionBrush", WithAlpha(theme.Accent, 0x74));
            SetBrushResource("AccentForegroundBrush", theme.AccentForeground);
            SetBrushResource("SegmentSelectedBrush", MixWithAlpha(theme.Shell, Colors.White, 0.14, 0x33));
            SetBrushResource("PopupBackgroundBrush", WithAlpha(Mix(theme.Shell, Colors.Black, 0.18), 0xF7));
            SetBrushResource("PopupBorderBrush", Colors.White, 0x38);

            GlassShell.Background = FrozenBrush(WithAlpha(
                theme.Shell,
                (byte)Math.Round(opacity * 255)));
            var glow = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            glow.GradientStops.Add(new GradientStop(theme.GlowStart, 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.35));
            glow.GradientStops.Add(new GradientStop(theme.GlowEnd, 0.72));
            glow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            glow.Freeze();
            ShellGlow.Background = glow;

            NightThemeRadio.IsChecked = string.Equals(_data.Settings.Theme, "night", StringComparison.OrdinalIgnoreCase);
            TealThemeRadio.IsChecked = string.Equals(_data.Settings.Theme, "teal", StringComparison.OrdinalIgnoreCase);
            AmberThemeRadio.IsChecked = string.Equals(_data.Settings.Theme, "amber", StringComparison.OrdinalIgnoreCase);
            BounceStyleRadio.IsChecked = _data.Settings.ReminderStyle == "bounce";
            JellyStyleRadio.IsChecked = _data.Settings.ReminderStyle == "jelly";
            SurpriseStyleRadio.IsChecked = _data.Settings.ReminderStyle == "surprise";
            BreatheStyleRadio.IsChecked = _data.Settings.ReminderStyle == "breathe";
            BackgroundOpacitySlider.Value = Math.Clamp(_data.Settings.BackgroundOpacity * 100, 10, 100);
            ApplyBackground();
            OpacitySlider.Value = Math.Round(opacity * 100);
            OpacityValueText.Text = $"{OpacitySlider.Value:0}%";
        }
        finally
        {
            _initializingUi = previousInitializing;
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (TitleTextBox is null)
        {
            return;
        }

        MainContent.Margin = new Thickness(14, 10, 14, 10);
        DateText.Visibility = ActualHeight < 180 ? Visibility.Collapsed : Visibility.Visible;
        PanelHintText.Visibility = ActualWidth < 450 || ActualHeight < 250
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SetBrushResource(string key, Color color)
    {
        Resources[key] = FrozenBrush(color);
    }

    private void SetBrushResource(string key, Color baseColor, byte alpha)
    {
        Resources[key] = FrozenBrush(WithAlpha(baseColor, alpha));
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Color Mix(Color first, Color second, double amount)
    {
        var ratio = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(first.R + (second.R - first.R) * ratio),
            (byte)Math.Round(first.G + (second.G - first.G) * ratio),
            (byte)Math.Round(first.B + (second.B - first.B) * ratio));
    }

    private static Color MixWithAlpha(Color first, Color second, double amount, byte alpha)
    {
        var mixed = Mix(first, second, amount);
        return WithAlpha(mixed, alpha);
    }

    private void SetStatus(string message, bool error = false, bool accent = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error
            ? (Brush)FindResource("DangerBrush")
            : accent
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("DimBrush");
    }

    private ReminderData LoadData()
    {
        try
        {
            if (File.Exists(_dataFile))
            {
                var json = File.ReadAllText(_dataFile, Encoding.UTF8);
                _data = JsonSerializer.Deserialize<ReminderData>(json, _jsonOptions) ?? new ReminderData();
            }
        }
        catch (Exception)
        {
            _data = new ReminderData();
        }

        _data.Settings ??= new ReminderSettings();
        _data.Tasks ??= new List<ReminderTask>();
        if (_data.Settings.ReminderStyle is not ("bounce" or "jelly" or "surprise" or "breathe"))
        {
            _data.Settings.ReminderStyle = "bounce";
        }
        if (string.IsNullOrWhiteSpace(_data.Settings.Theme)
            || !ThemePalettes.ContainsKey(_data.Settings.Theme))
        {
            _data.Settings.Theme = "night";
        }

        if (_data.Settings.GlassOpacity is < 0.55 or > 0.96)
        {
            _data.Settings.GlassOpacity = 0.88;
        }

        foreach (var task in _data.Tasks)
        {
            task.Id = string.IsNullOrWhiteSpace(task.Id) ? Guid.NewGuid().ToString("N") : task.Id;
            task.Title ??= "";
            task.Time ??= "";
            task.Repeat = string.IsNullOrWhiteSpace(task.Time)
                ? ""
                : string.IsNullOrWhiteSpace(task.Repeat) ? "daily" : task.Repeat;
            task.Date ??= "";
            task.StartDate ??= "";
            task.Done ??= "";
            task.LastFired ??= "";
            task.Created ??= "";
        }

        return _data;
    }

    private void SaveData()
    {
        if (!_isClosing && !_isCompact && IsLoaded && WindowState == WindowState.Normal
            && _activeReminderTaskId is null)
        {
            _data.Settings.WindowWidth = Math.Max(MinWindowWidth, Math.Round(Width));
            _data.Settings.WindowHeight = Math.Max(MinWindowHeight, Math.Round(Height));
        }

        try
        {
            Directory.CreateDirectory(_dataDirectory);
            var json = JsonSerializer.Serialize(_data, _jsonOptions);
            var temporaryFile = Path.Combine(_dataDirectory, $".tasks-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryFile, json, new UTF8Encoding(false));
            File.Move(temporaryFile, _dataFile, true);
            if (!_isClosing)
            {
                SetStatus("已自动保存");
            }
        }
        catch (Exception exception)
        {
            SetStatus($"保存失败：{exception.Message}", true);
        }
    }

    private static string NextRoundTime()
    {
        var next = DateTime.Now.AddMinutes(5);
        var roundedMinute = next.Minute / 5 * 5;
        return $"{next.Hour:00}:{roundedMinute:00}";
    }

    private static DateTime CurrentMinute()
    {
        var now = DateTime.Now;
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
    }

    private static bool TryParseTime(string? text, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Replace('：', ':').Split(':');
        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour)
               && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute)
               && hour is >= 0 and <= 23
               && minute is >= 0 and <= 59;
    }

    private static bool TryParseDate(string? text, out DateTime date)
    {
        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy-M-d",
            "yyyy/M/d",
            "yyyy.MM.dd",
            "yyyy.M.d"
        };
        return DateTime.TryParseExact(
            (text ?? "").Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is ContentElement content
                ? ContentOperations.GetParent(content)
                : VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? root, string? name = null)
        where T : FrameworkElement
    {
        if (root is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match
                && (string.IsNullOrEmpty(name)
                    || string.Equals(match.Name, name, StringComparison.Ordinal)))
            {
                return match;
            }

            var nested = FindDescendant<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    private static string AutostartFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Start Menu",
        "Programs",
        "Startup",
        "DailyReminder.vbs");

    private static bool AutostartEnabled()
    {
        return File.Exists(AutostartFile);
    }

    private static void EnableAutostart()
    {
        var executable = Environment.ProcessPath
                         ?? Path.Combine(AppContext.BaseDirectory, "DailyReminder.exe");
        var command = $"\"{executable}\" --minimized";
        var escapedCommand = command.Replace("\"", "\"\"");
        var content = "Set sh = CreateObject(\"WScript.Shell\")\r\n"
                      + $"sh.Run \"{escapedCommand}\", 0, False\r\n";

        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        File.WriteAllText(AutostartFile, content, new UTF8Encoding(false));
    }

    private static void DisableAutostart()
    {
        if (File.Exists(AutostartFile))
        {
            File.Delete(AutostartFile);
        }
    }

    private void ResizeLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isCompact)
        {
            return;
        }

        var newWidth = Math.Max(MinWidth, Width - e.HorizontalChange);
        var applied = Width - newWidth;
        Left += applied;
        Width = newWidth;
        e.Handled = true;
    }

    private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isCompact)
        {
            return;
        }

        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        e.Handled = true;
    }

    private void ResizeTop_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isCompact)
        {
            return;
        }

        var newHeight = Math.Max(MinHeight, Height - e.VerticalChange);
        var applied = Height - newHeight;
        Top += applied;
        Height = newHeight;
        e.Handled = true;
    }

    private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isCompact)
        {
            return;
        }

        Height = Math.Max(MinHeight, Height + e.VerticalChange);
        e.Handled = true;
    }

    private void ResizeTopLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeLeft_DragDelta(sender, e);
        ResizeTop_DragDelta(sender, e);
    }

    private void ResizeTopRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeRight_DragDelta(sender, e);
        ResizeTop_DragDelta(sender, e);
    }

    private void ResizeBottomLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeLeft_DragDelta(sender, e);
        ResizeBottom_DragDelta(sender, e);
    }

    private void ResizeBottomRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeRight_DragDelta(sender, e);
        ResizeBottom_DragDelta(sender, e);
    }

    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _normalWidth = Width;
        _normalHeight = Height;
        _normalLeft = Left;
        _normalTop = Top;
        SaveData();
    }

    private sealed record ThemePalette(
        string Name,
        Color Shell,
        Color GlowStart,
        Color GlowEnd,
        Color Accent,
        Color AccentForeground);
}
