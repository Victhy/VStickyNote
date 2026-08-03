using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VSticky.Models;
using VSticky.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace VSticky;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _tray;
    private readonly List<NoteWindow> _notes = [];
    private readonly List<NoteData> _store = [];
    private bool _exiting;
    private readonly Random _random = new();
    private Window? _trayMenuHost;
    private ContextMenu? _trayMenu;
    private GlobalHotkeyService? _hotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        SettingsStore.Load();
        Loc.Initialize(SettingsStore.Current.Language);
        SettingsStore.Current.Language = Loc.CurrentCode;
        SettingsStore.Save();
        Loc.Changed += OnLanguageChanged;

        AutostartService.SetEnabled(SettingsStore.Current.StartWithWindows);
        InitHotkey();
        InitTray();

        _store.Clear();
        _store.AddRange(NoteStorage.Load());

        var active = _store.Where(n => !n.IsDeleted).ToList();
        if (active.Count > 0)
        {
            foreach (var data in active)
                CreateNote(data, persist: false);
        }
        else if (!NoteStorage.StoreExists)
        {
            // 仅首次运行创建默认便签；若全部已删除则保持空桌面
            CreateNote();
        }
    }

    private void InitHotkey()
    {
        _hotkey = new GlobalHotkeyService();
        _hotkey.Activated += () => Dispatcher.Invoke(() => CreateNote());

        if (SettingsStore.Current.HotkeyNewNoteEnabled)
            ApplyHotkeyEnabled(true, showTipOnFail: false);
    }

    private bool ApplyHotkeyEnabled(bool enabled, bool showTipOnFail)
    {
        if (_hotkey == null)
            return false;

        if (!enabled)
        {
            _hotkey.Unregister();
            SettingsStore.Current.HotkeyNewNoteEnabled = false;
            SettingsStore.Save();
            return true;
        }

        if (_hotkey.TryRegisterNewNoteHotkey())
        {
            SettingsStore.Current.HotkeyNewNoteEnabled = true;
            SettingsStore.Save();
            return true;
        }

        SettingsStore.Current.HotkeyNewNoteEnabled = false;
        SettingsStore.Save();

        if (showTipOnFail && _tray != null)
        {
            _tray.BalloonTipTitle = Loc.T("tip.hotkeyFailTitle");
            _tray.BalloonTipText = Loc.T("tip.hotkeyFail");
            _tray.BalloonTipIcon = Forms.ToolTipIcon.Warning;
            _tray.ShowBalloonTip(2500);
        }

        return false;
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_tray != null)
                _tray.Text = Loc.T("app.name");
            BuildTrayMenu();
        });
    }

    private void InitTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = Loc.T("app.name"),
            Visible = true,
            Icon = CreateTrayIcon()
        };
        _tray.MouseUp += Tray_MouseUp;
        BuildTrayMenu();
    }

    private void BuildTrayMenu()
    {
        var style = (Style)FindResource("NoteMenuItem");
        var danger = (Style)FindResource("NoteMenuItem.Danger");
        var sep = (Style)FindResource("NoteSeparator");
        var menuStyle = (Style)FindResource("NoteContextMenu");

        _trayMenu = new ContextMenu { Style = menuStyle };

        var newItem = new MenuItem
        {
            Header = Loc.T("menu.new"),
            Style = style,
            Icon = MenuIcons.Glyph(MenuIcons.Add)
        };
        newItem.Click += (_, _) => CreateNote();

        var showAllItem = new MenuItem
        {
            Header = Loc.T("menu.showAll"),
            Style = style,
            Icon = MenuIcons.Glyph(MenuIcons.ShowAll)
        };
        showAllItem.Click += (_, _) => ShowAllHiddenNotes();

        var languageItem = new MenuItem
        {
            Header = Loc.T("menu.language"),
            Style = style,
            Icon = MenuIcons.Glyph(MenuIcons.Language)
        };
        foreach (var lang in Loc.Languages)
        {
            var item = new MenuItem
            {
                Header = lang.NativeName,
                Tag = lang.Code,
                IsCheckable = true,
                IsChecked = lang.Code.Equals(Loc.CurrentCode, StringComparison.OrdinalIgnoreCase),
                Style = style
            };
            item.Click += (_, _) =>
            {
                Loc.SetLanguage(lang.Code);
                SettingsStore.Current.Language = Loc.CurrentCode;
                SettingsStore.Save();
            };
            languageItem.Items.Add(item);
        }

        var startItem = new MenuItem
        {
            Header = Loc.T("menu.startWithWindows"),
            Style = style,
            Icon = MenuIcons.Glyph(MenuIcons.Boot),
            IsCheckable = true,
            IsChecked = SettingsStore.Current.StartWithWindows
        };
        startItem.Click += (_, _) =>
        {
            var enabled = startItem.IsChecked == true;
            SettingsStore.Current.StartWithWindows = enabled;
            SettingsStore.Save();
            AutostartService.SetEnabled(enabled);
        };

        var hotkeyManageItem = new MenuItem
        {
            Header = Loc.T("menu.hotkeyManage"),
            Style = style,
            Icon = MenuIcons.Glyph(MenuIcons.Keyboard)
        };
        var hotkeyNewNoteItem = new MenuItem
        {
            Header = Loc.T("menu.hotkeyNewNote"),
            Style = style,
            Icon = MenuIcons.Glyph(MenuIcons.Add),
            IsCheckable = true,
            IsChecked = SettingsStore.Current.HotkeyNewNoteEnabled
        };
        hotkeyNewNoteItem.Click += (_, _) =>
        {
            var want = hotkeyNewNoteItem.IsChecked == true;
            if (!ApplyHotkeyEnabled(want, showTipOnFail: true))
                hotkeyNewNoteItem.IsChecked = false;
        };
        hotkeyManageItem.Items.Add(hotkeyNewNoteItem);

        var snapItem = new MenuItem
        {
            Header = Loc.T("menu.snap"),
            Style = style,
            Icon = MenuIcons.Glyph(MenuIcons.Snap),
            IsCheckable = true,
            IsChecked = SettingsStore.Current.SnapEnabled
        };
        snapItem.Click += (_, _) =>
        {
            SettingsStore.Current.SnapEnabled = snapItem.IsChecked == true;
            SettingsStore.Save();
        };

        var exitItem = new MenuItem
        {
            Header = Loc.T("menu.exit"),
            Style = danger,
            Icon = MenuIcons.DangerGlyph(MenuIcons.Exit)
        };
        exitItem.Click += (_, _) => ExitApp();

        _trayMenu.Items.Add(newItem);
        _trayMenu.Items.Add(showAllItem);
        _trayMenu.Items.Add(languageItem);
        _trayMenu.Items.Add(new Separator { Style = sep });
        _trayMenu.Items.Add(startItem);
        _trayMenu.Items.Add(hotkeyManageItem);
        _trayMenu.Items.Add(snapItem);
        _trayMenu.Items.Add(new Separator { Style = sep });
        _trayMenu.Items.Add(exitItem);
    }

    private void Tray_MouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            CreateNote();
            return;
        }

        if (e.Button == Forms.MouseButtons.Right)
            ShowTrayMenu();
    }

    private void ShowTrayMenu()
    {
        if (_trayMenu == null)
            return;

        EnsureTrayMenuHost();
        ApplyTrayMenuTint();
        BuildTrayMenu();

        // 先关闭再打开，保证每次右键都能弹出
        _trayMenu.IsOpen = false;

        var mouse = Forms.Control.MousePosition;
        var dpi = VisualTreeHelper.GetDpi(_trayMenuHost!);
        var x = mouse.X / dpi.DpiScaleX;
        var y = mouse.Y / dpi.DpiScaleY;

        _trayMenu.Placement = PlacementMode.AbsolutePoint;
        _trayMenu.PlacementTarget = _trayMenuHost;
        _trayMenu.HorizontalOffset = x;
        _trayMenu.VerticalOffset = y;
        _trayMenu.IsOpen = true;

        // 激活宿主，避免菜单立刻失焦关闭
        _trayMenuHost!.Activate();
    }

    private void EnsureTrayMenuHost()
    {
        if (_trayMenuHost != null)
            return;

        _trayMenuHost = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            Opacity = 0,
            Left = -10000,
            Top = -10000
        };
        _trayMenuHost.Show();
    }

    private void ApplyTrayMenuTint()
    {
        if (_trayMenu == null) return;

        var bg = MediaColor.FromArgb(250, 247, 245, 240);
        var hover = MediaColor.FromArgb(28, 0, 0, 0);
        var border = MediaColor.FromArgb(28, 0, 0, 0);

        _trayMenu.Resources["NoteMenu.Bg"] = new SolidColorBrush(bg);
        _trayMenu.Resources["NoteMenu.Hover"] = new SolidColorBrush(hover);
        _trayMenu.Resources["NoteMenu.Border"] = new SolidColorBrush(border);
        _trayMenu.Background = new SolidColorBrush(bg);
        _trayMenu.BorderBrush = new SolidColorBrush(border);
    }

    private static Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Drawing.Color.Transparent);
            using var brush = new SolidBrush(Drawing.Color.FromArgb(255, 255, 214, 102));
            g.FillRectangle(brush, 1, 1, 13, 13);
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(200, 120, 90, 20));
            g.DrawRectangle(pen, 1, 1, 13, 13);
            using var line = new Drawing.Pen(Drawing.Color.FromArgb(180, 90, 70, 20), 1);
            g.DrawLine(line, 4, 5, 11, 5);
            g.DrawLine(line, 4, 8, 11, 8);
            g.DrawLine(line, 4, 11, 9, 11);
        }

        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        var clone = (Icon)icon.Clone();
        DestroyIcon(hIcon);
        bmp.Dispose();
        return clone;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private void CreateNote(NoteData? seed = null, bool persist = true)
    {
        var settings = SettingsStore.Current;
        var (left, top) = NextNotePosition();
        var data = seed ?? settings.CreateNote(
            left: left,
            top: top,
            colorHex: PickColor());

        data.IsDeleted = false;
        data.DeletedAt = null;
        UpsertStore(data);

        var win = new NoteWindow(data);
        win.GetSnapTargets = () => GetSnapTargets(win);
        win.RequestNewNote += _ => CreateNote();
        win.RequestDelete += DeleteNote;
        win.RequestHide += HideNote;
        win.RequestPersist += () => PersistAll();
        win.Closed += (_, _) =>
        {
            _notes.Remove(win);
            if (_exiting) return;
            PersistAll();
        };

        _notes.Add(win);
        win.Show();
        if (data.IsHidden)
            win.Hide();

        if (persist)
            PersistAll();
    }

    /// <summary>新便签放在当前可见便签下方，左对齐纵向排列。</summary>
    private (double left, double top) NextNotePosition()
    {
        const double gap = NoteSnapper.Gap;
        const double fallbackLeft = 100;
        const double fallbackTop = 100;

        var visible = _notes.Where(n => n.IsVisible).ToList();
        if (visible.Count == 0)
            return (fallbackLeft, fallbackTop);

        var left = visible.Min(n => n.Left);
        var bottom = visible.Max(n => n.Top + n.ActualHeight);
        return (left, bottom + gap);
    }

    private IReadOnlyList<Rect> GetSnapTargets(NoteWindow self)
    {
        var list = new List<Rect>(_notes.Count);
        foreach (var n in _notes)
        {
            if (ReferenceEquals(n, self) || !n.IsLoaded || !n.IsVisible)
                continue;

            var w = n.ActualWidth > 0 ? n.ActualWidth : n.Width;
            var h = n.ActualHeight > 0 ? n.ActualHeight : n.Height;
            if (w <= 0 || h <= 0) continue;

            list.Add(new Rect(n.Left, n.Top, w, h));
        }

        return list;
    }

    private void HideNote(NoteWindow win)
    {
        win.FlushToData();
        win.Data.IsHidden = true;
        win.Data.UpdatedAt = DateTime.Now;
        UpsertStore(win.Data);
        win.Hide();
        PersistAll();
    }

    private void ShowAllHiddenNotes()
    {
        foreach (var win in _notes)
        {
            if (!win.Data.IsHidden && win.IsVisible)
                continue;

            win.Data.IsHidden = false;
            win.Data.UpdatedAt = DateTime.Now;
            UpsertStore(win.Data);
            win.Show();
        }

        PersistAll();
    }

    private void DeleteNote(NoteWindow win)
    {
        SyncWindowToData(win);
        win.Data.IsDeleted = true;
        win.Data.IsHidden = false;
        win.Data.DeletedAt = DateTime.Now;
        win.Data.UpdatedAt = DateTime.Now;
        UpsertStore(win.Data);

        _notes.Remove(win);
        win.Close();
        PersistAll();
    }

    private void UpsertStore(NoteData data)
    {
        var idx = _store.FindIndex(n => n.Id == data.Id);
        if (idx >= 0)
            _store[idx] = data;
        else
            _store.Add(data);
    }

    private static void SyncWindowToData(NoteWindow win)
    {
        win.Data.Left = win.Left;
        win.Data.Top = win.Top;
        win.Data.Width = win.Width;
        win.Data.Height = win.Height;
        win.Data.UpdatedAt = DateTime.Now;
    }

    private string PickColor()
    {
        var colors = SettingsStore.Current.Colors;
        if (colors.Count == 0)
            return "#C8E6C9";
        return colors[_random.Next(colors.Count)].Hex;
    }

    private void PersistAll()
    {
        foreach (var win in _notes)
        {
            win.FlushToData();
            UpsertStore(win.Data);
        }

        NoteStorage.Save(_store);
        SettingsStore.Save();
    }

    private void ExitApp()
    {
        _exiting = true;
        PersistAll();
        if (_trayMenu != null)
            _trayMenu.IsOpen = false;

        _hotkey?.Dispose();
        _hotkey = null;

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        foreach (var note in _notes.ToList())
            note.Close();

        _trayMenuHost?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PersistAll();
        _hotkey?.Dispose();
        _hotkey = null;
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }
}
