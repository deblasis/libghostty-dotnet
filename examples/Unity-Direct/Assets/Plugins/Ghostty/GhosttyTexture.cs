using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace Ghostty.Unity
{
    public sealed class GhosttyTexture : IDisposable
    {
        private IntPtr _app;
        private IntPtr _surface;
        private IntPtr _sharedTextureHandlePtr;
        private IntPtr _device;
        private IntPtr _context;
        private IntPtr _staging;
        private Texture2D _texture;
        private uint _width;
        private uint _height;
        private double _scaleFactor;
        private bool _disposed;

        // Pin callback delegates to prevent GC collection while native code holds pointers
        private readonly ghostty_runtime_wakeup_cb _wakeupCb;
        private readonly ghostty_runtime_action_cb _actionCb;
        private readonly ghostty_runtime_read_clipboard_cb _readClipboardCb;
        private readonly ghostty_runtime_confirm_read_clipboard_cb _confirmReadClipboardCb;
        private readonly ghostty_runtime_write_clipboard_cb _writeClipboardCb;
        private readonly ghostty_runtime_close_surface_cb _closeSurfaceCb;
        private readonly GCHandle[] _gcHandles;

        private static int s_initState; // 0=not started, 1=done, -1=failed

        public Texture2D Texture => _texture;
        public IntPtr Surface => _surface;
        public IntPtr App => _app;
        public bool IsValid => _surface != IntPtr.Zero && _texture != null;

        // D3D11 COM vtable delegates for staging texture operations
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CreateTexture2DFn(
            IntPtr self, D3D11Texture2DDesc* desc, IntPtr initialData, out IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CopyResourceFn(IntPtr self, IntPtr dst, IntPtr src);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int MapFn(
            IntPtr self, IntPtr resource, uint subresource, uint mapType, uint mapFlags,
            D3D11MappedSubresource* mappedResource);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UnmapFn(IntPtr self, IntPtr resource, uint subresource);

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11Texture2DDesc
        {
            public uint Width, Height, MipLevels, ArraySize, Format;
            public uint SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11MappedSubresource
        {
            public IntPtr pData;
            public uint RowPitch, DepthPitch;
        }

        public GhosttyTexture(uint width, uint height, double scaleFactor)
        {
            _width = width;
            _height = height;
            _scaleFactor = scaleFactor;

            // Create and pin callback delegates
            _wakeupCb = OnWakeup;
            _actionCb = OnAction;
            _readClipboardCb = OnReadClipboard;
            _confirmReadClipboardCb = OnConfirmReadClipboard;
            _writeClipboardCb = OnWriteClipboard;
            _closeSurfaceCb = OnCloseSurface;

            _gcHandles = new GCHandle[6];
            _gcHandles[0] = GCHandle.Alloc(_wakeupCb);
            _gcHandles[1] = GCHandle.Alloc(_actionCb);
            _gcHandles[2] = GCHandle.Alloc(_readClipboardCb);
            _gcHandles[3] = GCHandle.Alloc(_confirmReadClipboardCb);
            _gcHandles[4] = GCHandle.Alloc(_writeClipboardCb);
            _gcHandles[5] = GCHandle.Alloc(_closeSurfaceCb);

            Initialize();
        }

        private void Initialize()
        {
            Debug.Log("GhosttyTexture: Initialize() starting");

            // Suppress crash dialogs and install an exception filter that prevents
            // ghostty background thread crashes from killing Unity's process.
            SetErrorMode(0x0001 | 0x0002 | 0x8000);
            _prevFilter = SetUnhandledExceptionFilter(Marshal.GetFunctionPointerForDelegate(_exceptionFilter));

            // All ghostty native calls must run on a Win32 thread with a large stack.
            // Unity's Mono threads have small stacks incompatible with Zig's stack probing.
            NativeThread.Run(() =>
            {
                // Thread-safe one-time ghostty init
                if (Interlocked.CompareExchange(ref s_initState, 1, 0) == 0)
                {
                    Debug.Log("GhosttyTexture: calling ghostty_init...");
                    var result = GhosttyNative.ghostty_init(0, IntPtr.Zero);
                    Debug.Log($"GhosttyTexture: ghostty_init returned {result}");
                    if (result != 0)
                    {
                        Volatile.Write(ref s_initState, -1);
                        Debug.LogError("GhosttyTexture: ghostty_init failed");
                        return;
                    }
                }
                else if (Volatile.Read(ref s_initState) == -1)
                {
                    Debug.LogError("GhosttyTexture: ghostty_init previously failed");
                    return;
                }

                // Create and finalize config
                Debug.Log("GhosttyTexture: creating config...");
                var config = GhosttyNative.ghostty_config_new();
                if (config == IntPtr.Zero)
                {
                    Debug.LogError("GhosttyTexture: ghostty_config_new failed");
                    return;
                }

                try
                {
                    Debug.Log("GhosttyTexture: loading config files...");
                    GhosttyNative.ghostty_config_load_default_files(config);
                    GhosttyNative.ghostty_config_load_recursive_files(config);
                    GhosttyNative.ghostty_config_finalize(config);
                    Debug.Log("GhosttyTexture: config finalized");

                    var runtimeConfig = new ghostty_runtime_config_s
                    {
                        userdata = IntPtr.Zero,
                        supports_selection_clipboard = 0,
                        wakeup_cb = Marshal.GetFunctionPointerForDelegate(_wakeupCb),
                        action_cb = Marshal.GetFunctionPointerForDelegate(_actionCb),
                        read_clipboard_cb = Marshal.GetFunctionPointerForDelegate(_readClipboardCb),
                        confirm_read_clipboard_cb = Marshal.GetFunctionPointerForDelegate(_confirmReadClipboardCb),
                        write_clipboard_cb = Marshal.GetFunctionPointerForDelegate(_writeClipboardCb),
                        close_surface_cb = Marshal.GetFunctionPointerForDelegate(_closeSurfaceCb),
                    };

                    Debug.Log("GhosttyTexture: calling ghostty_app_new...");
                    _app = GhosttyNative.ghostty_app_new(ref runtimeConfig, config);
                    Debug.Log($"GhosttyTexture: ghostty_app_new returned {_app}");
                }
                finally
                {
                    GhosttyNative.ghostty_config_free(config);
                }

                if (_app == IntPtr.Zero)
                {
                    Debug.LogError("GhosttyTexture: failed to create ghostty app");
                    return;
                }

                // Allocate memory for the shared handle output
                _sharedTextureHandlePtr = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(_sharedTextureHandlePtr, IntPtr.Zero);

                // Create surface in shared texture mode
                var surfaceCfg = GhosttyNative.ghostty_surface_config_new();
                surfaceCfg.platform_tag = ghostty_platform_e.GHOSTTY_PLATFORM_WINDOWS;
                surfaceCfg.platform.windows.hwnd = IntPtr.Zero;
                surfaceCfg.platform.windows.swap_chain_panel = IntPtr.Zero;
                surfaceCfg.platform.windows.shared_texture_out = _sharedTextureHandlePtr;
                surfaceCfg.platform.windows.texture_width = _width;
                surfaceCfg.platform.windows.texture_height = _height;
                surfaceCfg.scale_factor = _scaleFactor;

                Debug.Log("GhosttyTexture: calling ghostty_surface_new...");
                _surface = GhosttyNative.ghostty_surface_new(_app, ref surfaceCfg);
                Debug.Log($"GhosttyTexture: ghostty_surface_new returned {_surface}");

                if (_surface == IntPtr.Zero)
                {
                    Debug.LogError("GhosttyTexture: failed to create ghostty surface");
                    return;
                }

                // Get ghostty's D3D11 device and context (borrowed pointers)
                Debug.Log("GhosttyTexture: getting D3D11 device/context...");
                _device = GhosttyNative.ghostty_surface_get_d3d11_device(_surface);
                _context = GhosttyNative.ghostty_surface_get_d3d11_context(_surface);
                Debug.Log($"GhosttyTexture: device={_device}, context={_context}");
            });

            if (_device == IntPtr.Zero || _context == IntPtr.Zero)
            {
                if (_surface != IntPtr.Zero)
                    Debug.LogError("GhosttyTexture: failed to get D3D11 device/context from ghostty");
                return;
            }

            Debug.Log("GhosttyTexture: creating staging texture...");
            CreateStagingTexture();
            Debug.Log("GhosttyTexture: staging texture created, creating Unity texture...");
            CreateUnityTexture();
            Debug.Log("GhosttyTexture: initialization complete");
        }

        private unsafe void CreateStagingTexture()
        {
            if (_staging != IntPtr.Zero)
            {
                Marshal.Release(_staging);
                _staging = IntPtr.Zero;
            }

            var desc = new D3D11Texture2DDesc
            {
                Width = _width,
                Height = _height,
                MipLevels = 1,
                ArraySize = 1,
                Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                SampleCount = 1,
                SampleQuality = 0,
                Usage = 3,              // D3D11_USAGE_STAGING
                BindFlags = 0,
                CPUAccessFlags = 0x20000, // D3D11_CPU_ACCESS_READ
                MiscFlags = 0,
            };

            // ID3D11Device vtable slot 5 = CreateTexture2D
            var vt = *(IntPtr*)_device;
            var fn = Marshal.GetDelegateForFunctionPointer<CreateTexture2DFn>(
                *(IntPtr*)((byte*)vt + 5 * IntPtr.Size));

            var hr = fn(_device, &desc, IntPtr.Zero, out _staging);
            if (hr < 0 || _staging == IntPtr.Zero)
            {
                Debug.LogError($"GhosttyTexture: CreateTexture2D for staging failed (HRESULT 0x{hr:X8})");
                _staging = IntPtr.Zero;
            }
        }

        private void CreateUnityTexture()
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

            _texture = new Texture2D((int)_width, (int)_height, TextureFormat.BGRA32, false);
            _texture.filterMode = FilterMode.Bilinear;
            _texture.wrapMode = TextureWrapMode.Clamp;
        }

        private int _tickCount;

        public unsafe void Tick()
        {
            if (_app == IntPtr.Zero)
                return;

            if (_tickCount++ < 3)
                Debug.Log($"GhosttyTexture: Tick #{_tickCount}");

            // ghostty_app_tick must also run on a native thread
            NativeThread.Run(() => GhosttyNative.ghostty_app_tick(_app));

            if (_context == IntPtr.Zero || _staging == IntPtr.Zero || _texture == null)
                return;

            IntPtr tex = IntPtr.Zero;
            NativeThread.Run(() => tex = GhosttyNative.ghostty_surface_get_d3d11_texture(_surface));
            if (tex == IntPtr.Zero)
                return;

            if (_tickCount <= 3)
                Debug.Log($"GhosttyTexture: Tick #{_tickCount} - got texture {tex}, doing CopyResource...");

            // D3D11 calls go through COM vtable, not Zig -- these are fine on any thread
            var vt = *(IntPtr*)_context;
            var copy = Marshal.GetDelegateForFunctionPointer<CopyResourceFn>(
                *(IntPtr*)((byte*)vt + 47 * IntPtr.Size));
            var map = Marshal.GetDelegateForFunctionPointer<MapFn>(
                *(IntPtr*)((byte*)vt + 14 * IntPtr.Size));
            var unmap = Marshal.GetDelegateForFunctionPointer<UnmapFn>(
                *(IntPtr*)((byte*)vt + 15 * IntPtr.Size));

            copy(_context, _staging, tex);

            D3D11MappedSubresource mapped;
            if (map(_context, _staging, 0, 1, 0, &mapped) >= 0)
            {
                var rawData = _texture.GetRawTextureData<byte>();
                int dstStride = (int)_width * 4;
                byte* dst = (byte*)rawData.GetUnsafePtr();

                for (int y = 0; y < _height; y++)
                {
                    Buffer.MemoryCopy(
                        (byte*)mapped.pData + y * mapped.RowPitch,
                        dst + y * dstStride,
                        dstStride,
                        dstStride);
                }

                _texture.Apply(false);
                unmap(_context, _staging, 0);
            }
        }

        public void Resize(uint width, uint height)
        {
            if (_surface == IntPtr.Zero || (width == _width && height == _height))
                return;

            _width = width;
            _height = height;

            NativeThread.Run(() => GhosttyNative.ghostty_surface_set_size(_surface, width, height));

            CreateStagingTexture();
            CreateUnityTexture();
        }

        public void SetFocus(bool focused)
        {
            if (_surface != IntPtr.Zero)
                NativeThread.Run(() => GhosttyNative.ghostty_surface_set_focus(_surface, focused));
        }

        public void SetContentScale(double x, double y)
        {
            if (_surface != IntPtr.Zero)
                NativeThread.Run(() => GhosttyNative.ghostty_surface_set_content_scale(_surface, x, y));
        }

        public void SetOcclusion(bool visible)
        {
            if (_surface != IntPtr.Zero)
                NativeThread.Run(() => GhosttyNative.ghostty_surface_set_occlusion(_surface, visible));
        }

        // ---- Callback implementations ----

        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint uMode);

        [DllImport("kernel32.dll")]
        private static extern IntPtr SetUnhandledExceptionFilter(IntPtr lpTopLevelExceptionFilter);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int UnhandledExceptionFilterDelegate(IntPtr exceptionInfo);

        // EXCEPTION_CONTINUE_SEARCH = 0, EXCEPTION_EXECUTE_HANDLER = 1
        private static readonly UnhandledExceptionFilterDelegate _exceptionFilter = ExceptionFilter;
        private static IntPtr _prevFilter;

        private static int ExceptionFilter(IntPtr exceptionInfo)
        {
            // Log and swallow -- prevents ghostty background thread crashes from killing Unity
            Debug.LogWarning("GhosttyTexture: caught unhandled exception on background thread (ghostty internal)");
            return 1; // EXCEPTION_EXECUTE_HANDLER -- terminate just that thread, not the process
        }

        private static void OnWakeup(IntPtr userdata) { }

        private static bool OnAction(IntPtr app, ghostty_target_s target, ghostty_action_s action)
            => false;

        private static bool OnReadClipboard(IntPtr userdata, int loc, IntPtr state)
            => false;

        private static void OnConfirmReadClipboard(IntPtr userdata, IntPtr str, IntPtr state, int req) { }

        private static void OnWriteClipboard(IntPtr userdata, int loc, IntPtr content, nuint count, byte confirm) { }

        private static void OnCloseSurface(IntPtr userdata, byte processAlive) { }

        // ---- Cleanup ----

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_staging != IntPtr.Zero)
            {
                Marshal.Release(_staging);
                _staging = IntPtr.Zero;
            }

            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

            NativeThread.Run(() =>
            {
                if (_surface != IntPtr.Zero)
                {
                    GhosttyNative.ghostty_surface_free(_surface);
                    _surface = IntPtr.Zero;
                }

                if (_app != IntPtr.Zero)
                {
                    GhosttyNative.ghostty_app_free(_app);
                    _app = IntPtr.Zero;
                }
            });

            if (_sharedTextureHandlePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sharedTextureHandlePtr);
                _sharedTextureHandlePtr = IntPtr.Zero;
            }

            foreach (var handle in _gcHandles)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }
    }
}
