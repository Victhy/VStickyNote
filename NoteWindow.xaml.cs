using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using VSticky.Models;
using VSticky.Services;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using WpfCursors = System.Windows.Input.Cursors;

namespace VSticky;

public partial class NoteWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExToolWindow = 0x00000080;

    private const double NormalMinWidth = 160;
    private const double NormalMinHeight = 70;
    /// <summary>角标胶囊固定尺寸（宽:高 ≈ 3.2:1）。</summary>
    private const double BadgeWidth = 56;
    private const double BadgeHeight = 30;
    private const double ResizeBorder = 6;

    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _focusExitTimer;
    private bool _suppressEvents;
    private bool _isEditing;
    private bool _pendingDrag;
    private bool _pendingBadgeClick;
    private bool _isSnapDragging;
    private bool _suppressLocationSave;
    private bool _imeComposing;
    private bool _badgeExpanded;
    private Point _dragOrigin;
    private Point _dragMouseOriginDip;
    private Point _dragWindowOrigin;

    private bool IsBadgeCollapsed => Data.IsBadge && !_badgeExpanded;

    public NoteData Data { get; }

    /// <summary>返回其他可见便签的屏幕矩形（DIP），用于拖动吸附。</summary>
    public Func<IReadOnlyList<Rect>>? GetSnapTargets { get; set; }

    public event Action<NoteWindow>? RequestNewNote;
    public event Action<NoteWindow>? RequestDelete;
    public event Action<NoteWindow>? RequestHide;
    public event Action? RequestPersist;

    public NoteWindow(NoteData data)
    {
        InitializeComponent();
        Data = data;

        BodyBox.ContextMenu = null;
        InputMethod.SetIsInputMethodEnabled(BodyBox, true);
        BodyBox.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);

        TextCompositionManager.AddPreviewTextInputStartHandler(BodyBox, (_, _) => _imeComposing = true);
        TextCompositionManager.AddPreviewTextInputHandler(BodyBox, (_, _) => _imeComposing = false);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(BodyBox, OnTextInputUpdate);

        AssignMenuIcons();
        BuildColorMenus();
        BuildFontMenus();
        ApplyLocalization();
        NoteMenu.Closed += NoteMenu_Closed;

        Loc.Changed += OnLanguageChanged;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            PersistNoteAndSettings();
        };

        _focusExitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _focusExitTimer.Tick += (_, _) =>
        {
            _focusExitTimer.Stop();
            TryExitEditAfterFocusLoss();
        };

        SizeChanged += (_, _) => UpdateCornerClip();
        SourceInitialized += (_, _) => ExcludeFromAltTab();
        Loaded += (_, _) =>
        {
            ApplyFromModel();
            ShowPreviewMode();
        };
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>设为工具窗口，避免出现在 Alt+Tab 切换列表中。</summary>
    private void ExcludeFromAltTab()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        exStyle |= WsExToolWindow;
        exStyle &= ~WsExAppWindow;
        SetWindowLong(hwnd, GwlExStyle, exStyle);
    }

    private void OnTextInputUpdate(object sender, TextCompositionEventArgs e)
    {
        _imeComposing = !string.IsNullOrEmpty(e.TextComposition?.CompositionText);
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            ApplyLocalization();
            BuildColorMenus();
            BuildFontMenus();
            SyncFontMenuChecks();
        });
    }

    private void ApplyLocalization()
    {
        Title = Loc.T("window.title");
        NewNoteMenuItem.Header = Loc.T("menu.new");
        ColorMenu.Header = Loc.T("menu.color");
        FontMenu.Header = Loc.T("menu.font");
        FontSizeMenu.Header = Loc.T("menu.fontSize");
        AdjustMenuItem.Header = Loc.T("menu.adjust");
        TopmostMenu.Header = Loc.T("menu.topmost");
        HideNoteMenuItem.Header = Loc.T("menu.hide");
        DeleteMenuItem.Header = Loc.T("menu.delete");
        OpacityLabel.Text = Loc.T("label.opacity");
        CornerLabel.Text = Loc.T("label.corner");
        StayOpenPinButton.ToolTip = Loc.T("menu.badgeStayOpen");
        UpdateBadgeMenuUi();
        if (IsBadgeCollapsed)
            UpdateBadgeLabel();
    }

    private void AssignMenuIcons()
    {
        NewNoteMenuItem.Icon = MenuIcons.Glyph(MenuIcons.Add);
        ColorMenu.Icon = MenuIcons.Glyph(MenuIcons.Color);
        FontMenu.Icon = MenuIcons.Glyph(MenuIcons.Font);
        FontSizeMenu.Icon = MenuIcons.Glyph(MenuIcons.FontSize);
        AdjustMenuItem.Icon = MenuIcons.Glyph(MenuIcons.Adjust);
        BadgeModeMenuItem.Icon = MenuIcons.Glyph(MenuIcons.Badge);
        HideNoteMenuItem.Icon = MenuIcons.Glyph(MenuIcons.Hide);
        DeleteMenuItem.Icon = MenuIcons.DangerGlyph(MenuIcons.Delete);
    }

    private void UpdateBadgeMenuUi()
    {
        if (Data.IsBadge)
        {
            BadgeModeMenuItem.Header = Loc.T("menu.unbadge");
            BadgeModeMenuItem.Icon = MenuIcons.Glyph(MenuIcons.Restore);
        }
        else
        {
            BadgeModeMenuItem.Header = Loc.T("menu.badge");
            BadgeModeMenuItem.Icon = MenuIcons.Glyph(MenuIcons.Badge);
        }

        UpdateStayOpenPinUi();
    }

    private void UpdateStayOpenPinUi()
    {
        var show = Data.IsBadge && _badgeExpanded;
        StayOpenPinButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        StayOpenPinGlyph.Foreground = new SolidColorBrush(
            NotePalette.ForegroundFor(Data.ColorHex, Data.Opacity));
        StayOpenPinRotate.Angle = Data.BadgeStayOpen ? 0 : 45;
    }

    private void BuildColorMenus()
    {
        ColorMenu.Items.Clear();
        foreach (var color in SettingsStore.Current.Colors)
        {
            var item = new MenuItem
            {
                Header = LocHelper.ColorDisplayName(color),
                Tag = color.Hex,
                Style = (Style)FindResource("NoteMenuItem"),
                Icon = CreateColorSwatch(color.Hex)
            };
            item.Click += Color_Click;
            ColorMenu.Items.Add(item);
        }
    }

    private static System.Windows.Shapes.Ellipse CreateColorSwatch(string hex)
    {
        var c = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
        return new System.Windows.Shapes.Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(c),
            Stroke = new SolidColorBrush(MediaColor.FromArgb(60, 0, 0, 0)),
            StrokeThickness = 1
        };
    }

    private void BuildFontMenus()
    {
        var settings = SettingsStore.Current;
        var itemStyle = (Style)FindResource("NoteMenuItem");

        FontMenu.Items.Clear();
        foreach (var font in settings.FontChoices)
        {
            var item = new MenuItem
            {
                Header = font,
                Tag = font,
                IsCheckable = true,
                Style = itemStyle,
                FontFamily = new MediaFontFamily(font)
            };
            item.Click += FontFamily_Click;
            FontMenu.Items.Add(item);
        }

        FontSizeMenu.Items.Clear();
        foreach (var size in settings.FontSizeChoices)
        {
            var item = new MenuItem
            {
                Header = $"{size:0}",
                Tag = size,
                IsCheckable = true,
                Style = itemStyle
            };
            item.Click += FontSize_Click;
            FontSizeMenu.Items.Add(item);
        }
    }

    private void ApplyMenuTint()
    {
        var c = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(Data.ColorHex)!;
        static byte Mix(byte channel, double towardWhite) =>
            (byte)Math.Clamp((int)Math.Round(channel * (1 - towardWhite) + 255 * towardWhite), 0, 255);

        var bg = MediaColor.FromArgb(250, Mix(c.R, 0.86), Mix(c.G, 0.86), Mix(c.B, 0.86));
        var hover = MediaColor.FromArgb(36, c.R, c.G, c.B);
        var border = MediaColor.FromArgb(28, 0, 0, 0);

        NoteMenu.Resources["NoteMenu.Bg"] = new SolidColorBrush(bg);
        NoteMenu.Resources["NoteMenu.Hover"] = new SolidColorBrush(hover);
        NoteMenu.Resources["NoteMenu.Border"] = new SolidColorBrush(border);
        NoteMenu.Background = new SolidColorBrush(bg);
        NoteMenu.BorderBrush = new SolidColorBrush(border);
    }

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ApplyMenuTint();
        ApplyLocalization();
        BuildColorMenus();
        BuildFontMenus();
        SyncFontMenuChecks();
        UpdateBadgeMenuUi();
        NoteMenu.PlacementTarget = this;
        NoteMenu.IsOpen = true;
        e.Handled = true;
    }

    private void SyncFontMenuChecks()
    {
        foreach (MenuItem item in FontMenu.Items)
            item.IsChecked = string.Equals(item.Tag as string, Data.FontFamily, StringComparison.OrdinalIgnoreCase);

        foreach (MenuItem item in FontSizeMenu.Items)
            item.IsChecked = item.Tag is double size && Math.Abs(size - Data.FontSize) < 0.01;
    }

    private void ApplyFromModel()
    {
        _suppressEvents = true;
        try
        {
            Left = Data.Left;
            Top = Data.Top;
            Width = Math.Max(Data.Width, NormalMinWidth);
            Height = Math.Max(Data.Height, NormalMinHeight);
            Topmost = Data.Topmost;
            TopmostMenu.IsChecked = Data.Topmost;
            BodyBox.Text = Data.Text ?? string.Empty;
            OpacitySlider.Value = Data.Opacity;
            RadiusSlider.Value = Data.CornerRadius;
            ApplyAppearance();
            SyncFontMenuChecks();
            UpdateBadgeMenuUi();

            if (Data.IsBadge)
            {
                _badgeExpanded = false;
                ShowBadgeCollapsedVisual();
            }
            else
            {
                _badgeExpanded = false;
                ShowNormalContentVisual();
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ApplyAppearance()
    {
        var radius = IsBadgeCollapsed
            ? (ActualHeight > 1 ? ActualHeight / 2 : BadgeHeight / 2)
            : Data.CornerRadius;
        RootBorder.CornerRadius = new CornerRadius(radius);
        var baseColor = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(Data.ColorHex)!;
        var alpha = (byte)Math.Clamp((int)Math.Round(Data.Opacity * 255), 40, 255);
        RootBorder.Background = new SolidColorBrush(MediaColor.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));

        var fg = NotePalette.ForegroundFor(Data.ColorHex, Data.Opacity);
        var muted = NotePalette.MutedForegroundFor(Data.ColorHex, Data.Opacity);
        var fgBrush = new SolidColorBrush(fg);
        var mutedBrush = new SolidColorBrush(muted);

        BodyBox.FontFamily = new MediaFontFamily(Data.FontFamily);
        BodyBox.FontSize = Data.FontSize;
        BodyBox.Foreground = fgBrush;
        BodyBox.CaretBrush = fgBrush;
        BadgeLabel.FontFamily = BodyBox.FontFamily;
        BadgeLabel.Foreground = fgBrush;
        OpacityLabel.Foreground = mutedBrush;
        CornerLabel.Foreground = mutedBrush;
        UpdateStayOpenPinUi();

        if (!_isEditing && !IsBadgeCollapsed)
            RefreshPreview();

        UpdateCornerClip();
    }

    private void RefreshPreview()
    {
        PreviewViewer.Document = MarkdownRenderer.ToFlowDocument(
            BodyBox.Text,
            Data.FontFamily,
            Data.FontSize,
            NotePalette.ForegroundFor(Data.ColorHex, Data.Opacity));
    }

    private void UpdateCornerClip()
    {
        if (RootBorder.ActualWidth <= 0 || RootBorder.ActualHeight <= 0)
            return;

        var radius = IsBadgeCollapsed
            ? RootBorder.ActualHeight / 2
            : Data.CornerRadius;
        RootBorder.Clip = new RectangleGeometry(
            new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight),
            radius,
            radius);
    }

    public void FlushToData() => SyncToModel();

    private void SyncToModel()
    {
        Data.Left = Left;
        Data.Top = Top;
        if (!IsBadgeCollapsed)
        {
            Data.Width = Width;
            Data.Height = Height;
        }

        Data.Text = BodyBox.Text;
        Data.Opacity = OpacitySlider.Value;
        Data.CornerRadius = RadiusSlider.Value;
        Data.Topmost = Topmost;
        Data.UpdatedAt = DateTime.Now;
    }

    private void PersistNoteAndSettings()
    {
        SyncToModel();
        SettingsStore.SaveFromNote(Data);
        RequestPersist?.Invoke();
    }

    private void ScheduleSave()
    {
        if (_suppressEvents) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void EnterEditMode()
    {
        if (_isEditing || IsBadgeCollapsed) return;
        _focusExitTimer.Stop();
        _isEditing = true;
        _pendingDrag = false;
        EndSnapDrag(persist: false);

        PreviewViewer.Visibility = Visibility.Collapsed;
        BodyBox.Visibility = Visibility.Visible;
        BodyBox.Cursor = WpfCursors.IBeam;
        InputMethod.SetIsInputMethodEnabled(BodyBox, true);
        BodyBox.Focus();
        Keyboard.Focus(BodyBox);
        BodyBox.CaretIndex = BodyBox.Text.Length;
    }

    private void ShowPreviewMode()
    {
        _isEditing = false;
        _imeComposing = false;
        _pendingDrag = false;
        EndSnapDrag(persist: false);

        SyncToModel();
        RefreshPreview();
        BodyBox.Visibility = Visibility.Collapsed;
        PreviewViewer.Visibility = Visibility.Visible;
        Cursor = WpfCursors.Arrow;
        RootBorder.Cursor = WpfCursors.Arrow;
        PreviewViewer.Cursor = WpfCursors.Arrow;
    }

    private void ExitEditMode()
    {
        if (!_isEditing)
        {
            ShowPreviewMode();
            return;
        }

        PersistNoteAndSettings();
        ShowPreviewMode();
    }

    private void HideQuickPanel() => QuickPanel.Visibility = Visibility.Collapsed;

    private void TryExitEditAfterFocusLoss()
    {
        if (!_isEditing) return;
        if (_imeComposing) return;
        if (IsKeyboardFocusWithin) return;
        if (BodyBox.IsKeyboardFocused || BodyBox.IsKeyboardFocusWithin) return;

        ExitEditMode();
        HideQuickPanel();
    }

    private void BodyBox_TextChanged(object sender, TextChangedEventArgs e) => ScheduleSave();

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents || !IsLoaded) return;
        Data.Opacity = OpacitySlider.Value;
        ApplyAppearance();
        ScheduleSave();
    }

    private void RadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents || !IsLoaded) return;
        Data.CornerRadius = RadiusSlider.Value;
        ApplyAppearance();
        ScheduleSave();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEditing || IsBadgeCollapsed || e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount >= 2)
        {
            EnterEditMode();
            e.Handled = true;
            return;
        }

        if (e.OriginalSource == RootBorder || e.OriginalSource is Grid)
        {
            BeginSnapDrag();
            e.Handled = true;
        }
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSnapDragging) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndSnapDrag(persist: true);
            return;
        }

        UpdateSnapDragPosition();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSnapDragging)
            EndSnapDrag(persist: true);
    }

    private void Window_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isSnapDragging)
            EndSnapDrag(persist: true);
    }

    private void Preview_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEditing || e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount >= 2)
        {
            _pendingDrag = false;
            EnterEditMode();
            e.Handled = true;
            return;
        }

        _pendingDrag = true;
        _dragOrigin = e.GetPosition(this);
        PreviewViewer.CaptureMouse();
        e.Handled = true;
    }

    private void Preview_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pendingDrag || _isEditing) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var delta = e.GetPosition(this) - _dragOrigin;
        if (delta.LengthSquared > 16)
        {
            _pendingDrag = false;
            if (PreviewViewer.IsMouseCaptured)
                PreviewViewer.ReleaseMouseCapture();
            BeginSnapDrag();
        }
    }

    private void Preview_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pendingDrag) return;
        _pendingDrag = false;
        if (PreviewViewer.IsMouseCaptured)
            PreviewViewer.ReleaseMouseCapture();
    }

    private void BeginSnapDrag()
    {
        _pendingDrag = false;
        _isSnapDragging = true;
        _suppressLocationSave = true;
        _dragMouseOriginDip = GetMouseDip();
        _dragWindowOrigin = new Point(Left, Top);
        CaptureMouse();
    }

    private void UpdateSnapDragPosition()
    {
        var mouse = GetMouseDip();
        var left = _dragWindowOrigin.X + (mouse.X - _dragMouseOriginDip.X);
        var top = _dragWindowOrigin.Y + (mouse.Y - _dragMouseOriginDip.Y);

        // 托盘关闭吸附，或按住 Shift 时临时禁用
        if (SettingsStore.Current.SnapEnabled &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            var targets = GetSnapTargets?.Invoke();
            if (targets is { Count: > 0 })
            {
                var width = ActualWidth > 0 ? ActualWidth : Width;
                var height = ActualHeight > 0 ? ActualHeight : Height;
                var snapped = NoteSnapper.Snap(new Rect(left, top, width, height), targets);
                left = snapped.X;
                top = snapped.Y;
            }
        }

        Left = left;
        Top = top;
    }

    private void EndSnapDrag(bool persist)
    {
        if (!_isSnapDragging) return;

        _isSnapDragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        _suppressLocationSave = false;
        if (persist && IsLoaded)
            ScheduleSave();
    }

    private Point GetMouseDip()
    {
        var p = Forms.Control.MousePosition;
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(p.X / dpi.DpiScaleX, p.Y / dpi.DpiScaleY);
    }

    private void BodyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isEditing) return;
        _focusExitTimer.Stop();
        _focusExitTimer.Start();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (IsActive) return;
            if (_imeComposing) return;
            if (NoteMenu.IsOpen) return;

            ApplyDeactivateSideEffects();
        });
    }

    private void NoteMenu_Closed(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (IsActive) return;
            if (_imeComposing) return;
            ApplyDeactivateSideEffects();
        });
    }

    private void ApplyDeactivateSideEffects()
    {
        HideQuickPanel();
        if (_isEditing)
            ExitEditMode();

        if (Data.IsBadge && _badgeExpanded && !Data.BadgeStayOpen)
            CollapseBadge();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isEditing)
                ExitEditMode();
            HideQuickPanel();

            if (Data.IsBadge && _badgeExpanded && !Data.BadgeStayOpen)
                CollapseBadge();

            e.Handled = true;
        }
    }

    private void ToggleQuickPanel_Click(object sender, RoutedEventArgs e)
    {
        if (IsBadgeCollapsed) return;

        QuickPanel.Visibility = QuickPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string hex })
        {
            Data.ColorHex = hex;
            ApplyAppearance();
            ScheduleSave();
        }
    }

    private void FontFamily_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string font })
        {
            Data.FontFamily = font;
            ApplyAppearance();
            SyncFontMenuChecks();
            ScheduleSave();
        }
    }

    private void FontSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: double size })
        {
            Data.FontSize = size;
            ApplyAppearance();
            SyncFontMenuChecks();
            ScheduleSave();
        }
    }

    private void Topmost_Click(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostMenu.IsChecked == true;
        Data.Topmost = Topmost;
        ScheduleSave();
    }

    private void StayOpenPin_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void StayOpenPin_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!Data.IsBadge || !_badgeExpanded) return;

        Data.BadgeStayOpen = !Data.BadgeStayOpen;
        UpdateStayOpenPinUi();
        ScheduleSave();
        e.Handled = true;
    }

    private void BadgeMode_Click(object sender, RoutedEventArgs e)
    {
        if (Data.IsBadge)
            ExitBadgeMode();
        else
            EnterBadgeMode();
    }

    private void EnterBadgeMode()
    {
        if (Data.IsBadge) return;

        SyncToModel();
        Data.IsBadge = true;
        _badgeExpanded = false;
        ShowBadgeCollapsedVisual();
        ScheduleSave();
    }

    private void ExitBadgeMode()
    {
        if (!Data.IsBadge) return;

        var wasCollapsed = IsBadgeCollapsed;
        Data.IsBadge = false;
        _badgeExpanded = false;

        if (wasCollapsed)
            ShowNormalContentVisual();
        else
        {
            ApplyAppearance();
            UpdateBadgeMenuUi();
        }

        ScheduleSave();
    }

    private void ExpandBadgeTemporarily()
    {
        if (!Data.IsBadge || _badgeExpanded) return;

        _badgeExpanded = true;
        ShowNormalContentVisual();
        Activate();
    }

    private void CollapseBadge()
    {
        if (!Data.IsBadge || !_badgeExpanded) return;

        SyncToModel();
        _badgeExpanded = false;
        ShowBadgeCollapsedVisual();
        ScheduleSave();
    }

    private void ShowNormalContentVisual()
    {
        BadgeBar.Visibility = Visibility.Collapsed;
        NoteContentGrid.Visibility = Visibility.Visible;
        ResizeMode = ResizeMode.CanResize;
        Chrome.ResizeBorderThickness = new Thickness(ResizeBorder);
        MinWidth = NormalMinWidth;
        MinHeight = NormalMinHeight;

        var restoreSuppress = _suppressEvents;
        _suppressEvents = true;
        try
        {
            Width = Math.Max(Data.Width, NormalMinWidth);
            Height = Math.Max(Data.Height, NormalMinHeight);
        }
        finally
        {
            _suppressEvents = restoreSuppress;
        }

        ApplyAppearance();
        if (!_isEditing)
            ShowPreviewMode();
        UpdateBadgeMenuUi();
    }

    private void ShowBadgeCollapsedVisual()
    {
        HideQuickPanel();
        if (_isEditing)
        {
            _isEditing = false;
            _imeComposing = false;
            BodyBox.Visibility = Visibility.Collapsed;
            PreviewViewer.Visibility = Visibility.Visible;
        }

        NoteContentGrid.Visibility = Visibility.Collapsed;
        BadgeBar.Visibility = Visibility.Visible;
        UpdateBadgeLabel();

        ResizeMode = ResizeMode.NoResize;
        Chrome.ResizeBorderThickness = new Thickness(0);
        MinWidth = 40;
        MinHeight = 20;

        UpdateBadgeLabel();
        var restoreSuppress = _suppressEvents;
        _suppressEvents = true;
        try
        {
            Width = BadgeWidth;
            Height = BadgeHeight;
        }
        finally
        {
            _suppressEvents = restoreSuppress;
        }

        var radius = BadgeHeight / 2;
        RootBorder.CornerRadius = new CornerRadius(radius);
        UpdateCornerClip();
        UpdateBadgeMenuUi();
    }

    private void UpdateBadgeLabel()
    {
        var raw = BodyBox.Text ?? Data.Text ?? string.Empty;
        var line = raw.Replace("\r\n", "\n")
            .Split('\n')
            .Select(static l => l.Trim())
            .FirstOrDefault(static l => l.Length > 0);

        BadgeLabel.Text = string.IsNullOrEmpty(line)
            ? Loc.T("badge.empty")
            : StripMarkdownLite(line);
    }

    private static string StripMarkdownLite(string text)
    {
        if (text.StartsWith('#'))
            text = text.TrimStart('#').TrimStart();
        if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal))
            text = text[2..];
        return text.Trim('*', '_', '`', ' ');
    }

    private void Badge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsBadgeCollapsed || e.ChangedButton != MouseButton.Left) return;

        _pendingBadgeClick = true;
        _dragOrigin = e.GetPosition(this);
        BadgeBar.CaptureMouse();
        e.Handled = true;
    }

    private void Badge_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pendingBadgeClick || !IsBadgeCollapsed) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var delta = e.GetPosition(this) - _dragOrigin;
        if (delta.LengthSquared > 16)
        {
            _pendingBadgeClick = false;
            if (BadgeBar.IsMouseCaptured)
                BadgeBar.ReleaseMouseCapture();
            BeginSnapDrag();
        }
    }

    private void Badge_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pendingBadgeClick) return;

        _pendingBadgeClick = false;
        if (BadgeBar.IsMouseCaptured)
            BadgeBar.ReleaseMouseCapture();

        ExpandBadgeTemporarily();
        e.Handled = true;
    }

    private void NewNote_Click(object sender, RoutedEventArgs e) => RequestNewNote?.Invoke(this);

    private void HideNote_Click(object sender, RoutedEventArgs e) => RequestHide?.Invoke(this);

    private void Delete_Click(object sender, RoutedEventArgs e) => RequestDelete?.Invoke(this);

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (IsLoaded && !_suppressLocationSave) ScheduleSave();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded)
        {
            UpdateCornerClip();
            ScheduleSave();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Loc.Changed -= OnLanguageChanged;
        SyncToModel();
        SettingsStore.SaveFromNote(Data);
        base.OnClosing(e);
    }
}