// GhosttyBridge - C++ Unity native plugin that owns the libghostty lifecycle
// and D3D11 shared texture interop. C# calls into this plugin for everything.
//
// All ghostty calls run on a dedicated worker thread to avoid D3D11 context
// conflicts with Unity's render thread. Unity-side D3D11 calls (OpenSharedResource,
// CreateShaderResourceView) stay on the caller's thread.

#include <d3d11.h>
#include <dxgi.h>
#include <stdint.h>
#include <string.h>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

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
    void ghostty_surface_draw(ghostty_surface_t surface);
}

// ---- No-op callbacks ----

static void cb_wakeup(void*) {}
static bool cb_action(void*, void*, void*) { return false; }
static bool cb_read_clipboard(void*, int, void*) { return false; }
static void cb_confirm_read_clipboard(void*, void*, void*, int) {}
static void cb_write_clipboard(void*, int, void*, size_t, uint8_t) {}
static void cb_close_surface(void*, uint8_t) {}

// ---- Worker thread (all ghostty calls run here) ----
// Pure Win32 implementation -- no STL, no CRT thread APIs.
// Uses a fixed-size ring buffer of task slots, a critical section for
// mutual exclusion, and Win32 events for signaling.

// Task function signatures. Context is passed as an opaque void*.
typedef void (*TaskFn)(void* ctx);

struct TaskSlot {
    TaskFn   fn;           // function to execute
    void*    ctx;          // opaque context pointer (caller-owned)
    HANDLE   done_event;   // if non-NULL, worker signals this after fn()
};

static const int QUEUE_CAPACITY = 64;

static TaskSlot  g_Slots[QUEUE_CAPACITY];
static volatile LONG g_Head = 0;   // next slot to dequeue (worker reads)
static volatile LONG g_Tail = 0;   // next slot to enqueue (producers write)

static CRITICAL_SECTION g_CS;
static HANDLE g_QueueEvent  = NULL; // signaled when queue becomes non-empty
static HANDLE g_WorkerThread = NULL;
static volatile LONG g_Shutdown = 0;

// Enqueue a task (caller must hold g_CS). Returns false if queue is full.
static bool Enqueue(TaskFn fn, void* ctx, HANDLE done_event) {
    LONG next = (g_Tail + 1) % QUEUE_CAPACITY;
    if (next == g_Head) return false; // full
    g_Slots[g_Tail].fn         = fn;
    g_Slots[g_Tail].ctx        = ctx;
    g_Slots[g_Tail].done_event = done_event;
    g_Tail = next;
    return true;
}

static DWORD WINAPI WorkerProc(LPVOID) {
    while (true) {
        WaitForSingleObject(g_QueueEvent, INFINITE);

        // Drain all pending tasks
        while (true) {
            TaskSlot slot;
            EnterCriticalSection(&g_CS);
            if (g_Head == g_Tail) {
                // Queue empty -- reset event so we block next iteration
                ResetEvent(g_QueueEvent);
                LeaveCriticalSection(&g_CS);
                break;
            }
            slot = g_Slots[g_Head];
            g_Head = (g_Head + 1) % QUEUE_CAPACITY;
            LeaveCriticalSection(&g_CS);

            slot.fn(slot.ctx);
            if (slot.done_event)
                SetEvent(slot.done_event);
        }

        if (InterlockedCompareExchange(&g_Shutdown, 0, 0) != 0)
            break;
    }
    return 0;
}

// Post a task and block until it completes, returning a result via out pointer.
// result_ptr is written by the TaskFn through its ctx.
static void RunSync(TaskFn fn, void* ctx) {
    HANDLE evt = CreateEventW(NULL, TRUE, FALSE, NULL);
    EnterCriticalSection(&g_CS);
    Enqueue(fn, ctx, evt);
    LeaveCriticalSection(&g_CS);
    SetEvent(g_QueueEvent);
    WaitForSingleObject(evt, INFINITE);
    CloseHandle(evt);
}

// Convenience: same as RunSync but makes the void-task intent explicit.
static void RunSyncVoid(TaskFn fn, void* ctx) {
    RunSync(fn, ctx);
}

// Fire-and-forget post for input events.
// IMPORTANT: ctx must point to data that outlives the async call or be NULL.
// For heap-allocated context, the TaskFn itself must free it.
static void RunAsync(TaskFn fn, void* ctx) {
    EnterCriticalSection(&g_CS);
    Enqueue(fn, ctx, NULL);
    LeaveCriticalSection(&g_CS);
    SetEvent(g_QueueEvent);
}

// Heap helpers (avoid new/delete which pull in CRT operator new)
static void* BridgeAlloc(size_t bytes) {
    return HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, bytes);
}
static void BridgeFree(void* p) {
    if (p) HeapFree(GetProcessHeap(), 0, p);
}

static void StartWorker() {
    if (g_WorkerThread) return;
    InterlockedExchange(&g_Shutdown, 0);
    InitializeCriticalSection(&g_CS);
    g_QueueEvent = CreateEventW(NULL, TRUE, FALSE, NULL); // manual-reset
    g_Head = g_Tail = 0;
    // 128 MB stack -- Zig's stack probing needs a large stack.
    g_WorkerThread = CreateThread(
        NULL, 128 * 1024 * 1024, WorkerProc, NULL, STACK_SIZE_PARAM_IS_A_RESERVATION, NULL);
}

static void StopWorker() {
    if (!g_WorkerThread) return;
    InterlockedExchange(&g_Shutdown, 1);
    SetEvent(g_QueueEvent);
    WaitForSingleObject(g_WorkerThread, 5000);
    CloseHandle(g_WorkerThread);
    g_WorkerThread = NULL;
    CloseHandle(g_QueueEvent);
    g_QueueEvent = NULL;
    DeleteCriticalSection(&g_CS);
}

// ---- Global state ----

static ID3D11Device* g_UnityDevice = NULL;
static ID3D11DeviceContext* g_UnityContext = NULL;
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

// Helper: open the DXGI shared handle on Unity's device (runs on caller's thread).
static void OpenSharedResource(GhosttyInstance* inst) {
    HANDLE dxgiHandle = *(HANDLE*)inst->shared_handle_ptr;
    if (!dxgiHandle || !g_UnityDevice) return;

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

// ---- Task context structs for worker thread dispatch ----

struct CreateCtx {
    GhosttyInstance* inst;
    uint32_t width;
    uint32_t height;
    double scaleFactor;
    bool result;
};

static void Task_Create(void* p) {
    CreateCtx* c = (CreateCtx*)p;
    GhosttyInstance* inst = c->inst;

    // One-time ghostty init
    if (!g_GhosttyInitialized) {
        if (ghostty_init(0, NULL) != 0) { c->result = false; return; }
        g_GhosttyInitialized = true;
    }

    ghostty_config_t config = ghostty_config_new();
    if (!config) { c->result = false; return; }

    ghostty_config_load_default_files(config);
    ghostty_config_load_recursive_files(config);
    ghostty_config_finalize(config);

    ghostty_runtime_config_s rt;
    memset(&rt, 0, sizeof(rt));
    rt.wakeup_cb = (void*)cb_wakeup;
    rt.action_cb = (void*)cb_action;
    rt.read_clipboard_cb = (void*)cb_read_clipboard;
    rt.confirm_read_clipboard_cb = (void*)cb_confirm_read_clipboard;
    rt.write_clipboard_cb = (void*)cb_write_clipboard;
    rt.close_surface_cb = (void*)cb_close_surface;

    inst->app = ghostty_app_new(&rt, config);
    ghostty_config_free(config);
    if (!inst->app) { c->result = false; return; }

    ghostty_surface_config_s sc = ghostty_surface_config_new();
    sc.platform_tag = GHOSTTY_PLATFORM_WINDOWS;
    sc.platform.windows.hwnd = NULL;
    sc.platform.windows.swap_chain_panel = NULL;
    sc.platform.windows.shared_texture_out = inst->shared_handle_ptr;
    sc.platform.windows.texture_width = c->width;
    sc.platform.windows.texture_height = c->height;
    sc.scale_factor = c->scaleFactor;

    inst->surface = ghostty_surface_new(inst->app, &sc);
    if (!inst->surface) {
        ghostty_app_free(inst->app);
        inst->app = NULL;
        c->result = false;
        return;
    }

    ghostty_surface_set_occlusion(inst->surface, true);
    c->result = true;
}

static void Task_Destroy(void* p) {
    GhosttyInstance* inst = (GhosttyInstance*)p;
    if (inst->surface) ghostty_surface_free(inst->surface);
    if (inst->app) ghostty_app_free(inst->app);
}

static void Task_Tick(void* p) {
    GhosttyInstance* inst = (GhosttyInstance*)p;
    if (inst->app) ghostty_app_tick(inst->app);
    if (inst->surface) ghostty_surface_draw(inst->surface);
}

struct ResizeCtx {
    GhosttyInstance* inst;
    uint32_t width;
    uint32_t height;
};

static void Task_Resize(void* p) {
    ResizeCtx* c = (ResizeCtx*)p;
    ghostty_surface_set_size(c->inst->surface, c->width, c->height);
    ghostty_surface_draw(c->inst->surface);
}

struct FocusCtx {
    ghostty_surface_t surface;
    bool focused;
};

static void Task_SetFocus(void* p) {
    FocusCtx* c = (FocusCtx*)p;
    ghostty_surface_set_focus(c->surface, c->focused);
    BridgeFree(c);
}

struct OcclusionCtx {
    ghostty_surface_t surface;
    bool visible;
};

static void Task_SetOcclusion(void* p) {
    OcclusionCtx* c = (OcclusionCtx*)p;
    ghostty_surface_set_occlusion(c->surface, c->visible);
    BridgeFree(c);
}

struct ContentScaleCtx {
    ghostty_surface_t surface;
    double x;
    double y;
};

static void Task_SetContentScale(void* p) {
    ContentScaleCtx* c = (ContentScaleCtx*)p;
    ghostty_surface_set_content_scale(c->surface, c->x, c->y);
    BridgeFree(c);
}

struct KeyCtx {
    ghostty_surface_t surface;
    int action;
    int mods;
    uint32_t keycode;
    bool result;
};

static void Task_SendKey(void* p) {
    KeyCtx* c = (KeyCtx*)p;
    ghostty_input_key_s key;
    memset(&key, 0, sizeof(key));
    key.action = c->action;
    key.mods = c->mods;
    key.keycode = c->keycode;
    c->result = ghostty_surface_key(c->surface, key);
}

struct TextCtx {
    ghostty_surface_t surface;
    uint32_t len;
    char buf[1]; // flexible array -- allocated with extra bytes
};

static void Task_SendText(void* p) {
    TextCtx* c = (TextCtx*)p;
    ghostty_surface_text(c->surface, c->buf, c->len);
    BridgeFree(c);
}

struct MousePosCtx {
    ghostty_surface_t surface;
    double x;
    double y;
    int mods;
};

static void Task_SendMousePos(void* p) {
    MousePosCtx* c = (MousePosCtx*)p;
    ghostty_surface_mouse_pos(c->surface, c->x, c->y, c->mods);
    BridgeFree(c);
}

struct MouseButtonCtx {
    ghostty_surface_t surface;
    int state;
    int button;
    int mods;
    bool result;
};

static void Task_SendMouseButton(void* p) {
    MouseButtonCtx* c = (MouseButtonCtx*)p;
    c->result = ghostty_surface_mouse_button(c->surface, c->state, c->button, c->mods);
}

struct MouseScrollCtx {
    ghostty_surface_t surface;
    double x;
    double y;
    int mods;
};

static void Task_SendMouseScroll(void* p) {
    MouseScrollCtx* c = (MouseScrollCtx*)p;
    ghostty_surface_mouse_scroll(c->surface, c->x, c->y, c->mods);
    BridgeFree(c);
}

// ---- Unity plugin entry points ----

extern "C" {

__declspec(dllexport) void __stdcall UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    IUnityGraphicsD3D11* d3d11 = static_cast<IUnityGraphicsD3D11*>(
        unityInterfaces->GetInterface(kUnityGraphicsD3D11_GUID_high, kUnityGraphicsD3D11_GUID_low));
    if (d3d11) {
        g_UnityDevice = d3d11->GetDevice();
        if (g_UnityDevice)
            g_UnityDevice->GetImmediateContext(&g_UnityContext);
    }
    StartWorker();
}

__declspec(dllexport) void __stdcall UnityPluginUnload()
{
    StopWorker();
    if (g_UnityContext) { g_UnityContext->Release(); g_UnityContext = NULL; }
    g_UnityDevice = NULL;
}

// ---- Lifecycle ----

__declspec(dllexport) void* __stdcall GhosttyBridge_Create(
    uint32_t width, uint32_t height, double scaleFactor)
{
    if (!g_UnityDevice || !g_WorkerThread)
        return NULL;

    GhosttyInstance* inst = (GhosttyInstance*)BridgeAlloc(sizeof(GhosttyInstance));
    inst->width = width;
    inst->height = height;
    // Allocate a void*-sized slot for the shared handle
    void** handleSlot = (void**)BridgeAlloc(sizeof(void*));
    *handleSlot = NULL;
    inst->shared_handle_ptr = handleSlot;

    CreateCtx ctx;
    ctx.inst = inst;
    ctx.width = width;
    ctx.height = height;
    ctx.scaleFactor = scaleFactor;
    ctx.result = false;
    RunSync(Task_Create, &ctx);

    if (!ctx.result) {
        BridgeFree(inst->shared_handle_ptr);
        BridgeFree(inst);
        return NULL;
    }

    OpenSharedResource(inst);
    return inst;
}

__declspec(dllexport) void __stdcall GhosttyBridge_Destroy(void* handle)
{
    if (!handle) return;
    GhosttyInstance* inst = (GhosttyInstance*)handle;

    if (inst->srv) inst->srv->Release();
    if (inst->shared_tex) inst->shared_tex->Release();

    RunSyncVoid(Task_Destroy, inst);

    BridgeFree(inst->shared_handle_ptr);
    BridgeFree(inst);
}

// ---- Per-frame ----

__declspec(dllexport) void __stdcall GhosttyBridge_Tick(void* handle)
{
    if (!handle) return;
    RunSyncVoid(Task_Tick, handle);
}

__declspec(dllexport) void* __stdcall GhosttyBridge_GetSRV(void* handle)
{
    if (!handle) return NULL;
    return ((GhosttyInstance*)handle)->srv;
}

__declspec(dllexport) uint32_t __stdcall GhosttyBridge_GetWidth(void* handle)
{
    if (!handle) return 0;
    return ((GhosttyInstance*)handle)->width;
}

__declspec(dllexport) uint32_t __stdcall GhosttyBridge_GetHeight(void* handle)
{
    if (!handle) return 0;
    return ((GhosttyInstance*)handle)->height;
}

// ---- Resize ----

__declspec(dllexport) void __stdcall GhosttyBridge_Resize(
    void* handle, uint32_t width, uint32_t height)
{
    if (!handle || !g_UnityDevice) return;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    if (width == inst->width && height == inst->height) return;

    if (inst->srv) { inst->srv->Release(); inst->srv = NULL; }
    if (inst->shared_tex) { inst->shared_tex->Release(); inst->shared_tex = NULL; }

    inst->width = width;
    inst->height = height;

    ResizeCtx ctx;
    ctx.inst = inst;
    ctx.width = width;
    ctx.height = height;
    RunSyncVoid(Task_Resize, &ctx);

    OpenSharedResource(inst);
}

// ---- Input (ghostty calls on worker, fire-and-forget for low latency) ----

__declspec(dllexport) void __stdcall GhosttyBridge_SetFocus(void* handle, bool focused)
{
    if (!handle) return;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    FocusCtx* c = (FocusCtx*)BridgeAlloc(sizeof(FocusCtx));
    c->surface = inst->surface;
    c->focused = focused;
    RunAsync(Task_SetFocus, c);
}

__declspec(dllexport) void __stdcall GhosttyBridge_SetOcclusion(void* handle, bool visible)
{
    if (!handle) return;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    OcclusionCtx* c = (OcclusionCtx*)BridgeAlloc(sizeof(OcclusionCtx));
    c->surface = inst->surface;
    c->visible = visible;
    RunAsync(Task_SetOcclusion, c);
}

__declspec(dllexport) void __stdcall GhosttyBridge_SetContentScale(
    void* handle, double x, double y)
{
    if (!handle) return;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    ContentScaleCtx* c = (ContentScaleCtx*)BridgeAlloc(sizeof(ContentScaleCtx));
    c->surface = inst->surface;
    c->x = x;
    c->y = y;
    RunAsync(Task_SetContentScale, c);
}

__declspec(dllexport) bool __stdcall GhosttyBridge_SendKey(
    void* handle, int action, int mods, uint32_t keycode)
{
    if (!handle) return false;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    KeyCtx ctx;
    ctx.surface = inst->surface;
    ctx.action = action;
    ctx.mods = mods;
    ctx.keycode = keycode;
    ctx.result = false;
    RunSync(Task_SendKey, &ctx);
    return ctx.result;
}

__declspec(dllexport) void __stdcall GhosttyBridge_SendText(
    void* handle, const char* text, uint32_t len)
{
    if (!handle || !text) return;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    // Allocate TextCtx + extra bytes for the string copy
    size_t allocSize = sizeof(TextCtx) + len; // buf[1] already in sizeof
    TextCtx* c = (TextCtx*)BridgeAlloc(allocSize);
    c->surface = inst->surface;
    c->len = len;
    memcpy(c->buf, text, len);
    RunAsync(Task_SendText, c);
}

__declspec(dllexport) void __stdcall GhosttyBridge_SendMousePos(
    void* handle, double x, double y, int mods)
{
    if (!handle) return;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    MousePosCtx* c = (MousePosCtx*)BridgeAlloc(sizeof(MousePosCtx));
    c->surface = inst->surface;
    c->x = x;
    c->y = y;
    c->mods = mods;
    RunAsync(Task_SendMousePos, c);
}

__declspec(dllexport) bool __stdcall GhosttyBridge_SendMouseButton(
    void* handle, int state, int button, int mods)
{
    if (!handle) return false;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    MouseButtonCtx ctx;
    ctx.surface = inst->surface;
    ctx.state = state;
    ctx.button = button;
    ctx.mods = mods;
    ctx.result = false;
    RunSync(Task_SendMouseButton, &ctx);
    return ctx.result;
}

__declspec(dllexport) void __stdcall GhosttyBridge_SendMouseScroll(
    void* handle, double x, double y, int mods)
{
    if (!handle) return;
    GhosttyInstance* inst = (GhosttyInstance*)handle;
    MouseScrollCtx* c = (MouseScrollCtx*)BridgeAlloc(sizeof(MouseScrollCtx));
    c->surface = inst->surface;
    c->x = x;
    c->y = y;
    c->mods = mods;
    RunAsync(Task_SendMouseScroll, c);
}

} // extern "C"
