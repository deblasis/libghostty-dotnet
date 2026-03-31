using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Ghostty.Unity
{
    public sealed class GhosttyTexture : IDisposable
    {
        private IntPtr _app;
        private IntPtr _surface;
        private IntPtr _sharedHandlePtr;
        private IntPtr _srvPtr;
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

        public Texture2D Texture => _texture;
        public IntPtr Surface => _surface;
        public IntPtr App => _app;
        public bool IsValid => _surface != IntPtr.Zero && _texture != null;

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
            // One-time ghostty init
            GhosttyNative.ghostty_init();

            // Create and finalize config
            var config = GhosttyNative.ghostty_config_new();
            GhosttyNative.ghostty_config_load_default_files(config);
            GhosttyNative.ghostty_config_finalize(config);

            // Create app with runtime callbacks
            var runtimeConfig = new ghostty_runtime_config_s
            {
                userdata = IntPtr.Zero,
                wakeup_cb = _wakeupCb,
                action_cb = _actionCb,
                read_clipboard_cb = _readClipboardCb,
                confirm_read_clipboard_cb = _confirmReadClipboardCb,
                write_clipboard_cb = _writeClipboardCb,
                close_surface_cb = _closeSurfaceCb,
            };

            _app = GhosttyNative.ghostty_app_new(ref runtimeConfig, config);
            GhosttyNative.ghostty_config_free(config);

            if (_app == IntPtr.Zero)
            {
                Debug.LogError("GhosttyTexture: failed to create ghostty app");
                return;
            }

            // Allocate memory for the shared handle output
            _sharedHandlePtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_sharedHandlePtr, IntPtr.Zero);

            // Create surface in shared texture mode (no hwnd, no swap_chain_panel)
            var surfaceCfg = GhosttyNative.ghostty_surface_config_new();
            surfaceCfg.platform_tag = ghostty_platform_e.GHOSTTY_PLATFORM_WINDOWS;
            surfaceCfg.platform.windows.hwnd = IntPtr.Zero;
            surfaceCfg.platform.windows.swap_chain_panel = IntPtr.Zero;
            surfaceCfg.platform.windows.shared_texture_out = _sharedHandlePtr;
            surfaceCfg.platform.windows.texture_width = _width;
            surfaceCfg.platform.windows.texture_height = _height;
            surfaceCfg.scale_factor = _scaleFactor;
            surfaceCfg.context = ghostty_surface_context_e.GHOSTTY_SURFACE_CONTEXT_WINDOW;

            _surface = GhosttyNative.ghostty_surface_new(_app, ref surfaceCfg);

            if (_surface == IntPtr.Zero)
            {
                Debug.LogError("GhosttyTexture: failed to create ghostty surface");
                return;
            }

            // Read the shared DXGI handle that ghostty wrote
            var sharedHandle = Marshal.ReadIntPtr(_sharedHandlePtr);
            if (sharedHandle == IntPtr.Zero)
            {
                Debug.LogError("GhosttyTexture: ghostty did not produce a shared texture handle");
                return;
            }

            CreateUnityTexture(sharedHandle);
        }

        private void CreateUnityTexture(IntPtr sharedHandle)
        {
            // Open the shared handle on Unity's D3D11 device via the bridge plugin
            _srvPtr = GhosttyNative.GhosttyBridge_OpenSharedTexture(
                sharedHandle, _width, _height);

            if (_srvPtr == IntPtr.Zero)
            {
                Debug.LogError("GhosttyTexture: failed to open shared texture on Unity device");
                return;
            }

            // Wrap as Unity Texture2D
            _texture = Texture2D.CreateExternalTexture(
                (int)_width,
                (int)_height,
                TextureFormat.BGRA32,
                false,  // no mipmaps
                false,  // not linear (sRGB)
                _srvPtr);

            _texture.filterMode = FilterMode.Bilinear;
            _texture.wrapMode = TextureWrapMode.Clamp;
        }

        public void Tick()
        {
            if (_app != IntPtr.Zero)
                GhosttyNative.ghostty_app_tick(_app);
        }

        public void Resize(uint width, uint height)
        {
            if (_surface == IntPtr.Zero || (width == _width && height == _height))
                return;

            // Release old Unity-side resources
            if (_srvPtr != IntPtr.Zero)
            {
                GhosttyNative.GhosttyBridge_ReleaseSRV(_srvPtr);
                _srvPtr = IntPtr.Zero;
            }
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

            _width = width;
            _height = height;

            // Tell ghostty to resize its shared texture
            GhosttyNative.ghostty_surface_resize_shared_texture(_surface, width, height);

            // Read the updated shared handle
            var sharedHandle = Marshal.ReadIntPtr(_sharedHandlePtr);
            if (sharedHandle != IntPtr.Zero)
                CreateUnityTexture(sharedHandle);
        }

        public void SetFocus(bool focused)
        {
            if (_surface != IntPtr.Zero)
                GhosttyNative.ghostty_app_set_focus(_surface, focused);
        }

        public void SetContentScale(double x, double y)
        {
            if (_surface != IntPtr.Zero)
                GhosttyNative.ghostty_surface_set_content_scale(_surface, x, y);
        }

        // ---- Callback implementations ----

        private static void OnWakeup(IntPtr userdata)
        {
            // In shared texture mode we poll via Tick() in Update(), so wakeup is a no-op.
        }

        private static bool OnAction(IntPtr app, ghostty_target_s target, ghostty_action_s action)
        {
            return false;
        }

        private static bool OnReadClipboard(IntPtr userdata, int loc, IntPtr state)
        {
            return false;
        }

        private static void OnConfirmReadClipboard(IntPtr userdata, IntPtr str, IntPtr state, int req)
        {
        }

        private static void OnWriteClipboard(IntPtr userdata, int loc, IntPtr content, nuint count, byte confirm)
        {
        }

        private static void OnCloseSurface(IntPtr userdata, byte processAlive)
        {
        }

        // ---- Cleanup ----

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_srvPtr != IntPtr.Zero)
            {
                GhosttyNative.GhosttyBridge_ReleaseSRV(_srvPtr);
                _srvPtr = IntPtr.Zero;
            }

            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

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

            if (_sharedHandlePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sharedHandlePtr);
                _sharedHandlePtr = IntPtr.Zero;
            }

            foreach (var handle in _gcHandles)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }
    }
}
