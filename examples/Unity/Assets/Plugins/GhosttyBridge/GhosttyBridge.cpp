#include <d3d11.h>
#include <dxgi.h>
#include <stdint.h>

// Unity native plugin interface (minimal inline definition to avoid header dependency)
struct IUnityInterfaces;

typedef void(__stdcall* IUnityPluginLoadFunc)(IUnityInterfaces* unityInterfaces);
typedef void(__stdcall* IUnityPluginUnloadFunc)();

struct IUnityInterface {
    virtual ~IUnityInterface() {}
};

struct IUnityGraphicsD3D11 : public IUnityInterface {
    virtual ID3D11Device* __stdcall GetDevice() = 0;
};

// GUIDs for Unity's IUnityGraphicsD3D11
// {AAB37EF8-7A87-D748-BF76-967F07EFB177}
static const long long kUnityGraphicsD3D11_GUID_high = 0xAAB37EF87A87D748ULL;
static const long long kUnityGraphicsD3D11_GUID_low  = 0xBF76967F07EFB177ULL;

struct IUnityInterfaces {
    virtual IUnityInterface* __stdcall GetInterface(long long guidHigh, long long guidLow) = 0;
    // other methods omitted
};

// Global state
static ID3D11Device* g_UnityDevice = nullptr;

extern "C" {

__declspec(dllexport) void __stdcall UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    auto* d3d11 = static_cast<IUnityGraphicsD3D11*>(
        unityInterfaces->GetInterface(kUnityGraphicsD3D11_GUID_high, kUnityGraphicsD3D11_GUID_low)
    );
    if (d3d11) {
        g_UnityDevice = d3d11->GetDevice();
    }
}

__declspec(dllexport) void __stdcall UnityPluginUnload()
{
    g_UnityDevice = nullptr;
}

// Opens a DXGI shared handle on Unity's D3D11 device.
// Returns an ID3D11ShaderResourceView* suitable for Texture2D.CreateExternalTexture.
// Caller must release the returned SRV via GhosttyBridge_ReleaseSRV.
__declspec(dllexport) void* __stdcall GhosttyBridge_OpenSharedTexture(
    void* sharedHandle,
    uint32_t width,
    uint32_t height)
{
    if (!g_UnityDevice || !sharedHandle)
        return nullptr;

    ID3D11Texture2D* texture = nullptr;
    HRESULT hr = g_UnityDevice->OpenSharedResource(
        (HANDLE)sharedHandle,
        __uuidof(ID3D11Texture2D),
        (void**)&texture
    );
    if (FAILED(hr) || !texture)
        return nullptr;

    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = 1;
    srvDesc.Texture2D.MostDetailedMip = 0;

    ID3D11ShaderResourceView* srv = nullptr;
    hr = g_UnityDevice->CreateShaderResourceView(texture, &srvDesc, &srv);
    texture->Release();

    if (FAILED(hr))
        return nullptr;

    return srv;
}

// Releases a shader resource view previously returned by GhosttyBridge_OpenSharedTexture.
__declspec(dllexport) void __stdcall GhosttyBridge_ReleaseSRV(void* srv)
{
    if (srv) {
        static_cast<ID3D11ShaderResourceView*>(srv)->Release();
    }
}

// Returns Unity's D3D11 device pointer (for diagnostics/debugging).
__declspec(dllexport) void* __stdcall GhosttyBridge_GetDevice()
{
    return g_UnityDevice;
}

} // extern "C"
