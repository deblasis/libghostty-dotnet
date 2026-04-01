using System;
using System.Runtime.InteropServices;

namespace Ghostty.Unity
{
    // ---- Enums (subset needed for input and lifecycle) ----

    public enum ghostty_input_action_e : int
    {
        GHOSTTY_ACTION_RELEASE = 0,
        GHOSTTY_ACTION_PRESS = 1,
        GHOSTTY_ACTION_REPEAT = 2,
    }

    [Flags]
    public enum ghostty_input_mods_e : int
    {
        GHOSTTY_MODS_NONE = 0,
        GHOSTTY_MODS_SHIFT = 1 << 0,
        GHOSTTY_MODS_CTRL = 1 << 1,
        GHOSTTY_MODS_ALT = 1 << 2,
        GHOSTTY_MODS_SUPER = 1 << 3,
        GHOSTTY_MODS_CAPS = 1 << 4,
        GHOSTTY_MODS_NUM = 1 << 5,
    }

    public enum ghostty_input_mouse_state_e : int
    {
        GHOSTTY_MOUSE_RELEASE = 0,
        GHOSTTY_MOUSE_PRESS = 1,
    }

    public enum ghostty_input_mouse_button_e : int
    {
        GHOSTTY_MOUSE_BUTTON_LEFT = 0,
        GHOSTTY_MOUSE_BUTTON_RIGHT = 1,
        GHOSTTY_MOUSE_BUTTON_MIDDLE = 2,
    }

    public enum ghostty_platform_e : int
    {
        GHOSTTY_PLATFORM_INVALID = 0,
        GHOSTTY_PLATFORM_MACOS = 1,
        GHOSTTY_PLATFORM_IOS = 2,
        GHOSTTY_PLATFORM_WINDOWS = 3,
    }

    public enum ghostty_surface_context_e : int
    {
        GHOSTTY_SURFACE_CONTEXT_WINDOW = 0,
        GHOSTTY_SURFACE_CONTEXT_TAB = 1,
        GHOSTTY_SURFACE_CONTEXT_SPLIT = 2,
    }

    // ---- Structs ----

    [StructLayout(LayoutKind.Sequential)]
    public struct ghostty_platform_windows_s
    {
        public IntPtr hwnd;
        public IntPtr swap_chain_panel;
        public IntPtr shared_texture_out;
        public uint texture_width;
        public uint texture_height;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ghostty_platform_u
    {
        [FieldOffset(0)]
        public ghostty_platform_windows_s windows;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ghostty_env_var_s
    {
        public IntPtr key;
        public IntPtr value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ghostty_surface_config_s
    {
        public ghostty_platform_e platform_tag;
        public ghostty_platform_u platform;
        public IntPtr userdata;
        public double scale_factor;
        public float font_size;
        public IntPtr working_directory;
        public IntPtr command;
        public IntPtr env_vars;
        public nuint env_var_count;
        public IntPtr initial_input;
        public byte wait_after_command;
        public ghostty_surface_context_e context;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ghostty_input_key_s
    {
        public ghostty_input_action_e action;
        public ghostty_input_mods_e mods;
        public ghostty_input_mods_e consumed_mods;
        public uint keycode;
        public IntPtr text;
        public uint unshifted_codepoint;
        public byte composing;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ghostty_surface_size_s
    {
        public ushort columns;
        public ushort rows;
        public uint width_px;
        public uint height_px;
        public uint cell_width_px;
        public uint cell_height_px;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ghostty_target_s
    {
        public int tag;
        public IntPtr target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ghostty_action_s
    {
        public int tag;
        public IntPtr action;
    }

    // ---- Runtime config struct ----

    [StructLayout(LayoutKind.Sequential)]
    public struct ghostty_runtime_config_s
    {
        public IntPtr userdata;
        public byte supports_selection_clipboard;
        public IntPtr wakeup_cb;
        public IntPtr action_cb;
        public IntPtr read_clipboard_cb;
        public IntPtr confirm_read_clipboard_cb;
        public IntPtr write_clipboard_cb;
        public IntPtr close_surface_cb;
    }

    // ---- Delegates (callbacks from native code) ----

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ghostty_runtime_wakeup_cb(IntPtr userdata);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public delegate bool ghostty_runtime_action_cb(
        IntPtr app,
        ghostty_target_s target,
        ghostty_action_s action);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public delegate bool ghostty_runtime_read_clipboard_cb(
        IntPtr userdata,
        int clipboard_loc,
        IntPtr state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ghostty_runtime_confirm_read_clipboard_cb(
        IntPtr userdata,
        IntPtr str,
        IntPtr state,
        int request);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ghostty_runtime_write_clipboard_cb(
        IntPtr userdata,
        int clipboard_loc,
        IntPtr content,
        nuint contentCount,
        byte confirm);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ghostty_runtime_close_surface_cb(
        IntPtr userdata,
        byte processAlive);

    // ---- Native methods ----

    public static class GhosttyNative
    {
        private const string GhosttyLib = "ghostty";

        // -- Ghostty lifecycle --

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ghostty_init(nuint argc, IntPtr argv);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ghostty_config_new();

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_config_load_default_files(IntPtr config);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_config_load_recursive_files(IntPtr config);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_config_load_string(
            IntPtr config,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string str,
            nuint len);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_config_finalize(IntPtr config);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_config_free(IntPtr config);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ghostty_app_new(
            ref ghostty_runtime_config_s runtime_config,
            IntPtr config);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_app_free(IntPtr app);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_app_tick(IntPtr app);

        // -- Surface lifecycle --

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern ghostty_surface_config_s ghostty_surface_config_new();

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ghostty_surface_new(
            IntPtr app,
            ref ghostty_surface_config_s config);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_free(IntPtr surface);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_refresh(IntPtr surface);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_set_size(
            IntPtr surface,
            uint width,
            uint height);

        // -- D3D11 direct texture access --

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ghostty_surface_get_d3d11_device(IntPtr surface);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ghostty_surface_get_d3d11_context(IntPtr surface);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ghostty_surface_get_d3d11_texture(IntPtr surface);

        // -- Input --

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ghostty_surface_key(
            IntPtr surface,
            ref ghostty_input_key_s key);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_text(
            IntPtr surface,
            IntPtr text,
            nuint len);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_mouse_pos(
            IntPtr surface,
            double x,
            double y,
            ghostty_input_mods_e mods);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ghostty_surface_mouse_button(
            IntPtr surface,
            ghostty_input_mouse_state_e state,
            ghostty_input_mouse_button_e button,
            ghostty_input_mods_e mods);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_mouse_scroll(
            IntPtr surface,
            double x,
            double y,
            ghostty_input_mods_e mods);

        // -- Focus/display --

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_set_focus(
            IntPtr surface,
            [MarshalAs(UnmanagedType.U1)] bool focused);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_set_content_scale(
            IntPtr surface,
            double x,
            double y);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ghostty_surface_set_occlusion(
            IntPtr surface,
            [MarshalAs(UnmanagedType.U1)] bool visible);

        [DllImport(GhosttyLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern ghostty_surface_size_s ghostty_surface_size(IntPtr surface);
    }
}
