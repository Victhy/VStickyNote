using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VSticky.Services;

/// <summary>全局热键：Ctrl+Win+Alt+N 新建便签。</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkN = 0x4E;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly int _hotkeyId;
    private HwndSource? _source;
    private bool _registered;

    public GlobalHotkeyService(int hotkeyId = 0x5653) => _hotkeyId = hotkeyId;

    public event Action? Activated;

    public bool IsRegistered => _registered;

    public bool TryRegisterNewNoteHotkey()
    {
        EnsureSource();
        Unregister();

        var modifiers = ModControl | ModWin | ModAlt | ModNoRepeat;
        _registered = RegisterHotKey(_source!.Handle, _hotkeyId, modifiers, VkN);
        return _registered;
    }

    public void Unregister()
    {
        if (_source == null || !_registered)
            return;

        UnregisterHotKey(_source.Handle, _hotkeyId);
        _registered = false;
    }

    private void EnsureSource()
    {
        if (_source != null)
            return;

        var parameters = new HwndSourceParameters("VStickyHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == _hotkeyId)
        {
            Activated?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
