// GhosttyBridge - C++ Unity native plugin that owns the libghostty lifecycle
// and D3D11 shared texture interop. C# calls into this plugin for everything.

#include <d3d11.h>
#include <dxgi.h>
#include <stdint.h>
#include <string.h>

// ---- Unity native plugin interface (minimal inline definitions) ----

struct IUnityInterfaces;
struct IUnityInterface { virtual ~IUnityInterface() {} };

struct IUnityGraphicsD3D11 : public IUnityInterface {
    virtual ID3D11Device* __stdcall GetDevice() = 0;
};

static const long long kUnityGraphicsD3D11_GUID_high = 0xAAB37EF87A87D748ULL;
static const long long kUnityGraphicsD3D11_GUID_low  = 0xBF76967F07EFB177ULL;

struct IUnityInterfaces {
    virtual IUnityInterface* __stdcall GetInterface(long long guidHigh, long long guidLow) = 0;
};

// ---- libghostty C API forward declarations ----

typedef void* ghostty_app_t;
typedef void* ghostty_surface_t;
typedef void* ghostty_config_t;

enum ghostty_platform_e { GHOSTTY_PLATFORM_WINDOWS = 3 };

struct ghostty_platform_windows_s {
    void* hwnd;
    void* swap_chain_panel;
    void* shared_texture_out;
    uint32_t texture_width;
    uint32_t texture_height;
};

struct ghostty_platform_u {
    ghostty_platform_windows_s windows;
};

struct ghostty_env_var_s {
    const char* key;
    const char* value;
};

struct ghostty_surface_config_s {
    int platform_tag;
    ghostty_platform_u platform;
    void* userdata;
    double scale_factor;
    float font_size;
    const char* working_directory;
    const char* command;
    ghostty_env_var_s* env_vars;
    size_t env_var_count;
    const char* initial_input;
    uint8_t wait_after_command;
    int context;
};

struct ghostty_runtime_config_s {
    void* userdata;
    uint8_t supports_selection_clipboard;
    void* wakeup_cb;
    void* action_cb;
    void* read_clipboard_cb;
    void* confirm_read_clipboard_cb;
    void* write_clipboard_cb;
    void* close_surface_cb;
};

enum ghostty_input_action_e {
    GHOSTTY_ACTION_RELEASE = 0,
    GHOSTTY_ACTION_PRESS = 1,
    GHOSTTY_ACTION_REPEAT = 2,
};

enum ghostty_input_mods_e {
    GHOSTTY_MODS_NONE  = 0,
    GHOSTTY_MODS_SHIFT = 1 << 0,
    GHOSTTY_MODS_CTRL  = 1 << 1,
    GHOSTTY_MODS_ALT   = 1 << 2,
    GHOSTTY_MODS_SUPER = 1 << 3,
};

enum ghostty_input_mouse_state_e {
    GHOSTTY_MOUSE_RELEASE = 0,
    GHOSTTY_MOUSE_PRESS = 1,
};

enum ghostty_input_mouse_button_e {
    GHOSTTY_MOUSE_BUTTON_LEFT = 0,
    GHOSTTY_MOUSE_BUTTON_RIGHT = 1,
    GHOSTTY_MOUSE_BUTTON_MIDDLE = 2,
};

struct ghostty_input_key_s {
    int action;
    int mods;
    int consumed_mods;
    uint32_t keycode;
    const char* text;
    uint32_t unshifted_codepoint;
    uint8_t composing;
};

// libghostty imports (linked against ghostty.dll)
extern "C" {
    int ghostty_init(size_t argc, void* argv);
    ghostty_config_t ghostty_config_new();
    void ghostty_config_load_default_files(ghostty_config_t config);
    void ghostty_config_load_recursive_files(ghostty_config_t config);
    void ghostty_config_finalize(ghostty_config_t config);
    void ghostty_config_free(ghostty_config_t config);
    ghostty_app_t ghostty_app_new(const ghostty_runtime_config_s* runtime_config, ghostty_config_t config);
    void ghostty_app_free(ghostty_app_t app);
    void ghostty_app_tick(ghostty_app_t app);
    ghostty_surface_config_s ghostty_surface_config_new();
    ghostty_surface_t ghostty_surface_new(ghostty_app_t app, const ghostty_surface_config_s* config);
    void ghostty_surface_free(ghostty_surface_t surface);
    void ghostty_surface_set_size(ghostty_surface_t surface, uint32_t width, uint32_t height);
    void ghostty_surface_set_focus(ghostty_surface_t surface, bool focused);
    void ghostty_surface_set_occlusion(ghostty_surface_t surface, bool visible);
    void ghostty_surface_set_content_scale(ghostty_surface_t surface, double x, double y);
    bool ghostty_surface_key(ghostty_surface_t surface, ghostty_input_key_s key);
    void ghostty_surface_text(ghostty_surface_t surface, const char* text, size_t len);
    void ghostty_surface_mouse_pos(ghostty_surface_t surface, double x, double y, int mods);
    bool ghostty_surface_mouse_button(ghostty_surface_t surface, int state, int button, int mods);
    void ghostty_surface_mouse_scroll(ghostty_surface_t surface, double x, double y, int mods);
    void* ghostty_surface_get_d3d11_device(ghostty_surface_t surface);
    void* ghostty_surface_get_d3d11_context(ghostty_surface_t surface);
    void* ghostty_surface_get_d3d11_texture(ghostty_surface_t surface);
}

// ---- No-op callbacks ----

static void cb_wakeup(void*) {}
static bool cb_action(void*, void*, void*) { return false; }
static bool cb_read_clipboard(void*, int, void*) { return false; }
static void cb_confirm_read_clipboard(void*, void*, void*, int) {}
static void cb_write_clipboard(void*, int, void*, size_t, uint8_t) {}
static void cb_close_surface(void*, uint8_t) {}

// ---- Global state ----

static ID3D11Device* g_UnityDevice = nullptr;
static ID3D11DeviceContext* g_UnityContext = nullptr;
static bool g_GhosttyInitialized = false;

// Per-terminal state
struct GhosttyInstance {
    ghostty_app_t app;
    ghostty_surface_t surface;
    void* shared_handle_ptr;      // AllocHGlobal-style, holds the DXGI handle
    ID3D11Texture2D* shared_tex;  // Opened on Unity's device
    ID3D11ShaderResourceView* srv;
    uint32_t width;
    uint32_t height;
};

// ---- Unity plugin entry points ----

extern "C" {

__declspec(dllexport) void __stdcall UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    auto* d3d11 = static_cast<IUnityGraphicsD3D11*>(
        unityInterfaces->GetInterface(kUnityGraphicsD3D11_GUID_high, kUnityGraphicsD3D11_GUID_low));
    if (d3d11) {
        g_UnityDevice = d3d11->GetDevice();
        if (g_UnityDevice)
            g_UnityDevice->GetImmediateContext(&g_UnityContext);
    }
}

__declspec(dllexport) void __stdcall UnityPluginUnload()
{
    if (g_UnityContext) { g_UnityContext->Release(); g_UnityContext = nullptr; }
    g_UnityDevice = nullptr;
}

// ---- Lifecycle ----

__declspec(dllexport) void* __stdcall GhosttyBridge_Create(
    uint32_t width, uint32_t height, double scaleFactor)
{
    if (!g_UnityDevice)
        return nullptr;

    // One-time ghostty init
    if (!g_GhosttyInitialized) {
        if (ghostty_init(0, nullptr) != 0)
            return nullptr;
        g_GhosttyInitialized = true;
    }

    auto* inst = new GhosttyInstance();
    memset(inst, 0, sizeof(*inst));
    inst->width = width;
    inst->height = height;

    // Config
    auto config = ghostty_config_new();
    if (!config) { delete inst; return nullptr; }

    ghostty_config_load_default_files(config);
    ghostty_config_load_recursive_files(config);
    ghostty_config_finalize(config);

    // Runtime config with no-op callbacks
    ghostty_runtime_config_s rt = {};
    rt.wakeup_cb = (void*)cb_wakeup;
    rt.action_cb = (void*)cb_action;
    rt.read_clipboard_cb = (void*)cb_read_clipboard;
    rt.confirm_read_clipboard_cb = (void*)cb_confirm_read_clipboard;
    rt.write_clipboard_cb = (void*)cb_write_clipboard;
    rt.close_surface_cb = (void*)cb_close_surface;

    inst->app = ghostty_app_new(&rt, config);
    ghostty_config_free(config);
    if (!inst->app) { delete inst; return nullptr; }

    // Allocate shared handle output
    inst->shared_handle_ptr = new void*(nullptr);

    // Surface in shared texture mode
    auto sc = ghostty_surface_config_new();
    sc.platform_tag = GHOSTTY_PLATFORM_WINDOWS;
    sc.platform.windows.hwnd = nullptr;
    sc.platform.windows.swap_chain_panel = nullptr;
    sc.platform.windows.shared_texture_out = inst->shared_handle_ptr;
    sc.platform.windows.texture_width = width;
    sc.platform.windows.texture_height = height;
    sc.scale_factor = scaleFactor;

    inst->surface = ghostty_surface_new(inst->app, &sc);
    if (!inst->surface) {
        ghostty_app_free(inst->app);
        delete (void**)inst->shared_handle_ptr;
        delete inst;
        return nullptr;
    }

    ghostty_surface_set_occlusion(inst->surface, true);

    // Open the shared DXGI handle on Unity's device
    HANDLE dxgiHandle = *(HANDLE*)inst->shared_handle_ptr;
    if (dxgiHandle) {
        HRESULT hr = g_UnityDevice->OpenSharedResource(
            dxgiHandle, __uuidof(ID3D11Texture2D), (void**)&inst->shared_tex);
        if (SUCCEEDED(hr) && inst->shared_tex) {
            D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
            srvDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
            srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            srvDesc.Texture2D.MipLevels = 1;
            g_UnityDevice->CreateShaderResourceView(inst->shared_tex, &srvDesc, &inst->srv);
        }
    }

    return inst;
}

__declspec(dllexport) void __stdcall GhosttyBridge_Destroy(void* handle)
{
    if (!handle) return;
    auto* inst = static_cast<GhosttyInstance*>(handle);

    if (inst->srv) inst->srv->Release();
    if (inst->shared_tex) inst->shared_tex->Release();
    if (inst->surface) ghostty_surface_free(inst->surface);
    if (inst->app) ghostty_app_free(inst->app);
    if (inst->shared_handle_ptr) delete (void**)inst->shared_handle_ptr;
    delete inst;
}

// ---- Per-frame ----

__declspec(dllexport) void __stdcall GhosttyBridge_Tick(void* handle)
{
    if (!handle) return;
    auto* inst = static_cast<GhosttyInstance*>(handle);
    if (inst->app) ghostty_app_tick(inst->app);
}

// Returns the SRV pointer for Texture2D.CreateExternalTexture
__declspec(dllexport) void* __stdcall GhosttyBridge_GetSRV(void* handle)
{
    if (!handle) return nullptr;
    return static_cast<GhosttyInstance*>(handle)->srv;
}

__declspec(dllexport) uint32_t __stdcall GhosttyBridge_GetWidth(void* handle)
{
    if (!handle) return 0;
    return static_cast<GhosttyInstance*>(handle)->width;
}

__declspec(dllexport) uint32_t __stdcall GhosttyBridge_GetHeight(void* handle)
{
    if (!handle) return 0;
    return static_cast<GhosttyInstance*>(handle)->height;
}

// ---- Resize ----

__declspec(dllexport) void __stdcall GhosttyBridge_Resize(
    void* handle, uint32_t width, uint32_t height)
{
    if (!handle || !g_UnityDevice) return;
    auto* inst = static_cast<GhosttyInstance*>(handle);
    if (width == inst->width && height == inst->height) return;

    // Release old Unity-side resources
    if (inst->srv) { inst->srv->Release(); inst->srv = nullptr; }
    if (inst->shared_tex) { inst->shared_tex->Release(); inst->shared_tex = nullptr; }

    inst->width = width;
    inst->height = height;

    // Tell ghostty to resize
    ghostty_surface_set_size(inst->surface, width, height);

    // Re-open the updated shared handle
    HANDLE dxgiHandle = *(HANDLE*)inst->shared_handle_ptr;
    if (dxgiHandle) {
        HRESULT hr = g_UnityDevice->OpenSharedResource(
            dxgiHandle, __uuidof(ID3D11Texture2D), (void**)&inst->shared_tex);
        if (SUCCEEDED(hr) && inst->shared_tex) {
            D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
            srvDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
            srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            srvDesc.Texture2D.MipLevels = 1;
            g_UnityDevice->CreateShaderResourceView(inst->shared_tex, &srvDesc, &inst->srv);
        }
    }
}

// ---- Input ----

__declspec(dllexport) void __stdcall GhosttyBridge_SetFocus(void* handle, bool focused)
{
    if (!handle) return;
    ghostty_surface_set_focus(static_cast<GhosttyInstance*>(handle)->surface, focused);
}

__declspec(dllexport) void __stdcall GhosttyBridge_SetContentScale(
    void* handle, double x, double y)
{
    if (!handle) return;
    ghostty_surface_set_content_scale(static_cast<GhosttyInstance*>(handle)->surface, x, y);
}

__declspec(dllexport) bool __stdcall GhosttyBridge_SendKey(
    void* handle, int action, int mods, uint32_t keycode)
{
    if (!handle) return false;
    ghostty_input_key_s key = {};
    key.action = action;
    key.mods = mods;
    key.keycode = keycode;
    return ghostty_surface_key(static_cast<GhosttyInstance*>(handle)->surface, key);
}

__declspec(dllexport) void __stdcall GhosttyBridge_SendText(
    void* handle, const char* text, uint32_t len)
{
    if (!handle || !text) return;
    ghostty_surface_text(static_cast<GhosttyInstance*>(handle)->surface, text, len);
}

__declspec(dllexport) void __stdcall GhosttyBridge_SendMousePos(
    void* handle, double x, double y, int mods)
{
    if (!handle) return;
    ghostty_surface_mouse_pos(static_cast<GhosttyInstance*>(handle)->surface, x, y, mods);
}

__declspec(dllexport) bool __stdcall GhosttyBridge_SendMouseButton(
    void* handle, int state, int button, int mods)
{
    if (!handle) return false;
    return ghostty_surface_mouse_button(
        static_cast<GhosttyInstance*>(handle)->surface, state, button, mods);
}

__declspec(dllexport) void __stdcall GhosttyBridge_SendMouseScroll(
    void* handle, double x, double y, int mods)
{
    if (!handle) return;
    ghostty_surface_mouse_scroll(static_cast<GhosttyInstance*>(handle)->surface, x, y, mods);
}

} // extern "C"
