using System.Runtime.InteropServices;
using Ghostty.Interop;
using Ghostty.D3D12;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.System;

namespace WinUI3Example;

internal sealed partial class GhosttyTerminal : UserControl, IDisposable
{
    private GhosttyApp? _ghostty;
    private SharedTextureHelper? _helper;
    private SoftwareBitmap? _softwareBitmap;
    private SoftwareBitmapSource? _bitmapSource;
    private DispatcherTimer? _timer;
    private readonly Window _window;
    private bool _disposed;
    private double _scale = 1.0;
    private double _pendingWidth, _pendingHeight;
    private bool _loaded;
    private readonly Image _image;

    private const double USER_DEFAULT_SCREEN_DPI = 96.0;

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    // For getting window handle
    private static readonly Guid IID_IWindowNative =
        new("EECDBF0E-BAE9-4CB6-A68E-9598E1CB57BB");

    // COM interface for accessing bitmap buffer data
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    private ghostty_input_mouse_button_e _lastMouseButton;

    private static unsafe IntPtr GetWindowHandle(Window window)
    {
        var unknown = Marshal.GetIUnknownForObject(window);
        try
        {
            Marshal.QueryInterface(unknown, in IID_IWindowNative, out var windowNativePtr);
            var vtable = Marshal.ReadIntPtr(windowNativePtr);
            var getWindowHandle = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int>)getWindowHandle)(windowNativePtr, out var hwnd);
            Marshal.Release(windowNativePtr);
            Marshal.ThrowExceptionForHR(hr);
            return hwnd;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public GhosttyTerminal(Window window)
    {
        _window = window;

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        IsTabStop = true;

        // Create Image as child
        _image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        // Input events (same as before)
        PointerMoved += OnPointerMoved;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        GotFocus += OnGotFocus;
        LostFocus += OnLostFocus;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Add the image as a child when the control is loaded into the visual tree
        if (_image.Parent == null)
        {
            // Create a simple Grid to host the Image
            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            grid.Children.Add(_image);

            // Set as content of the control
            Content = grid;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = GetWindowHandle(_window);
        var dpi = GetDpiForWindow(hwnd);
        _scale = dpi / USER_DEFAULT_SCREEN_DPI;

        // Wire keyboard to window root (same pattern as before)
        if (_window.Content is FrameworkElement root)
        {
            root.PreviewKeyDown += OnKeyDown;
            root.PreviewKeyUp += OnKeyUp;
        }

        _loaded = true;
        TryCreateGhostty();
    }

    private void TryCreateGhostty()
    {
        if (_ghostty != null || !_loaded) return;
        if (_pendingWidth <= 0 || _pendingHeight <= 0) return;

        int w = (int)_pendingWidth;
        int h = (int)_pendingHeight;

        _ghostty = new GhosttyApp(
            w, h, _scale,
            wakeup: _ => { },
            action: (_, _, _) => false,
            readClipboard: (_, _, _) => false,
            confirmReadClipboard: (_, _, _, _) => { },
            writeClipboard: (_, _, _, _, _) => { },
            closeSurface: (_, _) => DispatcherQueue.TryEnqueue(() => _window.Close()));

        _helper = new SharedTextureHelper(w, h);
        _ghostty.SetOcclusion(true);

        ApplySize();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (s, e) => DoFrame();
        _timer.Start();
    }

    private unsafe void DoFrame()
    {
        if (_ghostty == null || _helper == null) return;

        _ghostty.Tick();

        var snap = _ghostty.SharedTextureSnapshot;
        if (snap == null || snap.Value.resource_handle == 0) return;
        var s = snap.Value;

        var frame = _helper.AcquireFrame(s.resource_handle, s.fence_handle, s.fence_value, s.version);
        if (frame == null) return;

        var f = frame.Value;

        // Create or recreate SoftwareBitmap if size changed
        if (_softwareBitmap == null || _softwareBitmap.PixelWidth != f.Width || _softwareBitmap.PixelHeight != f.Height)
        {
            _softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, f.Width, f.Height, BitmapAlphaMode.Ignore);
            _bitmapSource = new SoftwareBitmapSource();
        }

        // Copy pixel data row-by-row into the SoftwareBitmap
        unsafe
        {
            using (var bitmapBuffer = _softwareBitmap.LockBuffer(BitmapBufferAccessMode.Write))
            using (var reference = bitmapBuffer.CreateReference())
            {
                // Use COM interop to get direct pointer access
                var byteAccess = (IMemoryBufferByteAccess)reference;
                byte* buffer;
                uint capacity;
                byteAccess.GetBuffer(out buffer, out capacity);

                // Copy data row by row
                for (int y = 0; y < f.Height; y++)
                {
                    var src = (byte*)f.Data + y * f.RowPitch;
                    var dst = buffer + y * f.Width * 4;
                    Buffer.MemoryCopy(src, dst, f.Width * 4, f.Width * 4);
                }
            }
        }

        _helper.ReleaseFrame();

        // Update Image source on UI thread (outside unsafe context)
        DispatcherQueue.TryEnqueue(() =>
        {
            var updateTask = UpdateImageSource();
        });
    }

    private async System.Threading.Tasks.Task UpdateImageSource()
    {
        if (_bitmapSource != null && _softwareBitmap != null)
            await _bitmapSource.SetBitmapAsync(_softwareBitmap);
        _image.Source = _bitmapSource;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _pendingWidth = e.NewSize.Width;
        _pendingHeight = e.NewSize.Height;

        if (_ghostty == null)
        {
            TryCreateGhostty();
            return;
        }
        ApplySize();

        // Resize helper
        int w = (int)(_pendingWidth * _scale);
        int h = (int)(_pendingHeight * _scale);
        if (w > 0 && h > 0)
            _helper?.Resize(w, h);
    }

    private void ApplySize()
    {
        var w = (uint)(_pendingWidth * _scale);
        var h = (uint)(_pendingHeight * _scale);
        if (w > 0 && h > 0)
            _ghostty!.SetSize(w, h);
    }

    // --- Focus ---
    private void OnGotFocus(object sender, RoutedEventArgs e) => _ghostty?.SetFocus(true);
    private void OnLostFocus(object sender, RoutedEventArgs e) => _ghostty?.SetFocus(false);

    // --- Keyboard ---
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_ghostty == null) return;
        var vk = (uint)e.Key;
        var scanCode = MapVirtualKeyW(vk, 0);

        var key = new ghostty_input_key_s
        {
            action = ghostty_input_action_e.GHOSTTY_ACTION_PRESS,
            mods = GetMods(),
            keycode = scanCode,
        };
        _ghostty.SendKey(key);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_ghostty == null) return;
        var key = new ghostty_input_key_s
        {
            action = ghostty_input_action_e.GHOSTTY_ACTION_RELEASE,
            mods = GetMods(),
            keycode = MapVirtualKeyW((uint)e.Key, 0),
        };
        _ghostty.SendKey(key);
        e.Handled = true;
    }

    // --- Mouse ---
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_ghostty == null) return;
        var pos = e.GetCurrentPoint(this).Position;
        _ghostty.SendMousePos(pos.X, pos.Y, GetMods());
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_ghostty == null) return;
        var point = e.GetCurrentPoint(this);
        var button = MapPointerButton(point.Properties);
        if (button.HasValue)
        {
            _lastMouseButton = button.Value;
            _ghostty.SendMouseButton(
                ghostty_input_mouse_state_e.GHOSTTY_MOUSE_PRESS,
                button.Value, GetMods());
            e.Handled = true;
        }
        CapturePointer(e.Pointer);
        Focus(FocusState.Pointer);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_ghostty == null) return;
        _ghostty.SendMouseButton(
            ghostty_input_mouse_state_e.GHOSTTY_MOUSE_RELEASE,
            _lastMouseButton, GetMods());
        e.Handled = true;
        ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_ghostty == null) return;
        var point = e.GetCurrentPoint(this);
        var delta = point.Properties.MouseWheelDelta / 120.0;
        _ghostty.SendMouseScroll(0, delta, (int)GetMods());
        e.Handled = true;
    }

    // --- Helpers ---
    private static ghostty_input_mods_e GetMods()
    {
        var mods = (ghostty_input_mods_e)0;
        if (IsKeyDown(VirtualKey.Shift)) mods |= ghostty_input_mods_e.GHOSTTY_MODS_SHIFT;
        if (IsKeyDown(VirtualKey.Control)) mods |= ghostty_input_mods_e.GHOSTTY_MODS_CTRL;
        if (IsKeyDown(VirtualKey.Menu)) mods |= ghostty_input_mods_e.GHOSTTY_MODS_ALT;
        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
            mods |= ghostty_input_mods_e.GHOSTTY_MODS_SUPER;
        return mods;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (GetAsyncKeyState((int)key) & 0x8000) != 0;

    private static ghostty_input_mouse_button_e? MapPointerButton(PointerPointProperties props)
    {
        if (props.IsLeftButtonPressed) return ghostty_input_mouse_button_e.GHOSTTY_MOUSE_LEFT;
        if (props.IsRightButtonPressed) return ghostty_input_mouse_button_e.GHOSTTY_MOUSE_RIGHT;
        if (props.IsMiddleButtonPressed) return ghostty_input_mouse_button_e.GHOSTTY_MOUSE_MIDDLE;
        return null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
        _helper?.Dispose();
        _ghostty?.Dispose();
        _ghostty = null;
    }
}
