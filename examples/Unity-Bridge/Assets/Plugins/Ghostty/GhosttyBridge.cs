using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Ghostty.Unity
{
    /// <summary>
    /// Thin C# wrapper around the GhosttyBridge native plugin.
    /// All libghostty lifecycle and D3D11 interop lives in C++.
    /// </summary>
    public sealed class GhosttyBridge : IDisposable
    {
        private IntPtr _handle;
        private Texture2D _texture;
        private uint _width;
        private uint _height;
        private bool _disposed;

        public Texture2D Texture => _texture;
        public IntPtr Handle => _handle;
        public bool IsValid => _handle != IntPtr.Zero && _texture != null;

        public GhosttyBridge(uint width, uint height, double scaleFactor)
        {
            _width = width;
            _height = height;

            _handle = Native.GhosttyBridge_Create(width, height, scaleFactor);
            if (_handle == IntPtr.Zero)
            {
                Debug.LogError("GhosttyBridge: failed to create instance");
                return;
            }

            CreateTexture();
        }

        private void CreateTexture()
        {
            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

            var srv = Native.GhosttyBridge_GetSRV(_handle);
            if (srv == IntPtr.Zero)
            {
                Debug.LogError("GhosttyBridge: no SRV available");
                return;
            }

            _texture = Texture2D.CreateExternalTexture(
                (int)_width, (int)_height,
                TextureFormat.BGRA32,
                false, false, srv);
            _texture.filterMode = FilterMode.Bilinear;
            _texture.wrapMode = TextureWrapMode.Clamp;
        }

        public void Tick()
        {
            if (_handle != IntPtr.Zero)
                Native.GhosttyBridge_Tick(_handle);
        }

        public void Resize(uint width, uint height)
        {
            if (_handle == IntPtr.Zero || (width == _width && height == _height))
                return;

            _width = width;
            _height = height;
            Native.GhosttyBridge_Resize(_handle, width, height);
            CreateTexture();
        }

        public void SetFocus(bool focused)
        {
            if (_handle != IntPtr.Zero)
                Native.GhosttyBridge_SetFocus(_handle, focused);
        }

        public void SetOcclusion(bool visible)
        {
            if (_handle != IntPtr.Zero)
                Native.GhosttyBridge_SetOcclusion(_handle, visible);
        }

        public void SetContentScale(double x, double y)
        {
            if (_handle != IntPtr.Zero)
                Native.GhosttyBridge_SetContentScale(_handle, x, y);
        }

        public bool SendKey(int action, int mods, uint keycode)
        {
            if (_handle == IntPtr.Zero) return false;
            return Native.GhosttyBridge_SendKey(_handle, action, mods, keycode);
        }

        public unsafe void SendText(string text)
        {
            if (_handle == IntPtr.Zero || string.IsNullOrEmpty(text)) return;

            var maxBytes = Encoding.UTF8.GetMaxByteCount(text.Length);
            byte* buf = stackalloc byte[maxBytes];
            int len;
            fixed (char* chars = text)
                len = Encoding.UTF8.GetBytes(chars, text.Length, buf, maxBytes);
            Native.GhosttyBridge_SendText(_handle, (IntPtr)buf, (uint)len);
        }

        public void SendMousePos(double x, double y, int mods = 0)
        {
            if (_handle != IntPtr.Zero)
                Native.GhosttyBridge_SendMousePos(_handle, x, y, mods);
        }

        public bool SendMouseButton(int state, int button, int mods = 0)
        {
            if (_handle == IntPtr.Zero) return false;
            return Native.GhosttyBridge_SendMouseButton(_handle, state, button, mods);
        }

        public void SendMouseScroll(double x, double y, int mods = 0)
        {
            if (_handle != IntPtr.Zero)
                Native.GhosttyBridge_SendMouseScroll(_handle, x, y, mods);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_texture != null)
            {
                UnityEngine.Object.Destroy(_texture);
                _texture = null;
            }

            if (_handle != IntPtr.Zero)
            {
                Native.GhosttyBridge_Destroy(_handle);
                _handle = IntPtr.Zero;
            }
        }

        private static class Native
        {
            private const string Lib = "GhosttyBridge";

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern IntPtr GhosttyBridge_Create(
                uint width, uint height, double scaleFactor);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_Destroy(IntPtr handle);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_Tick(IntPtr handle);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern IntPtr GhosttyBridge_GetSRV(IntPtr handle);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_Resize(
                IntPtr handle, uint width, uint height);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_SetFocus(
                IntPtr handle, [MarshalAs(UnmanagedType.U1)] bool focused);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_SetOcclusion(
                IntPtr handle, [MarshalAs(UnmanagedType.U1)] bool visible);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_SetContentScale(
                IntPtr handle, double x, double y);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool GhosttyBridge_SendKey(
                IntPtr handle, int action, int mods, uint keycode);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_SendText(
                IntPtr handle, IntPtr text, uint len);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_SendMousePos(
                IntPtr handle, double x, double y, int mods);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            [return: MarshalAs(UnmanagedType.U1)]
            public static extern bool GhosttyBridge_SendMouseButton(
                IntPtr handle, int state, int button, int mods);

            [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
            public static extern void GhosttyBridge_SendMouseScroll(
                IntPtr handle, double x, double y, int mods);
        }
    }
}
