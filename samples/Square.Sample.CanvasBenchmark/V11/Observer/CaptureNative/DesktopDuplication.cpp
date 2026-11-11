// Reference: Microsoft Windows-classic-samples, MIT (see LICENSE.microsoft.txt).
// DXGIDesktopDuplication/cpp/DuplicationManager.cpp: InitDupl/GetFrame/DoneWithFrame.
// DXGIDesktopDuplication/cpp/OutputManager.cpp: ProcessMonoMask staging ROI copy/map.
// This implementation captures only the benchmark's 96x8 desktop ROI; no presentation.
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <cstdint>
#include <cstring>
#include <memory>
#include <new>

using Microsoft::WRL::ComPtr;

struct CaptureInfo
{
    int64_t qpcFrequency;
    int32_t left, top, right, bottom;
    uint32_t refreshNumerator, refreshDenominator;
    uint32_t rotation, outputIndex;
    int64_t adapterLuid;
};
struct FrameInfo
{
    int64_t lastPresentQpc, lastMouseQpc, acquiredQpc, completedQpc;
    uint32_t accumulatedFrames, pointerOnly, protectedContentMaskedOut, rectsCoalesced;
    uint32_t sourceWidth, sourceHeight, sourceFormat, rowPitch, roiLeft, roiTop, nativeFirstCellBgra, nativeBufferHash;
};
static_assert(sizeof(CaptureInfo) == 48 && sizeof(FrameInfo) == 80, "C ABI layout");

struct Capture
{
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGIOutputDuplication> duplication;
    ComPtr<ID3D11Texture2D> staging;
    CaptureInfo info{};
};

static HRESULT Initialize(Capture& capture, int x, int y)
{
    ComPtr<IDXGIFactory1> factory;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(factory.GetAddressOf()));
    if (FAILED(hr)) return hr;
    for (UINT ai = 0; ; ++ai)
    {
        ComPtr<IDXGIAdapter1> adapter;
        hr = factory->EnumAdapters1(ai, adapter.GetAddressOf());
        if (hr == DXGI_ERROR_NOT_FOUND) break;
        if (FAILED(hr)) return hr;
        for (UINT oi = 0; ; ++oi)
        {
            ComPtr<IDXGIOutput> output;
            hr = adapter->EnumOutputs(oi, output.GetAddressOf());
            if (hr == DXGI_ERROR_NOT_FOUND) break;
            if (FAILED(hr)) return hr;
            DXGI_OUTPUT_DESC description{};
            hr = output->GetDesc(&description);
            if (FAILED(hr)) return hr;
            const RECT& r = description.DesktopCoordinates;
            // The whole physical client belongs to one output, including the marker ROI.
            if (!description.AttachedToDesktop || x < r.left || y < r.top ||
                x + 1280 > r.right || y + 800 > r.bottom) continue;
            if (description.Rotation != DXGI_MODE_ROTATION_IDENTITY) return DXGI_ERROR_UNSUPPORTED;
            hr = D3D11CreateDevice(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_SINGLETHREADED,
                nullptr, 0, D3D11_SDK_VERSION, capture.device.GetAddressOf(), nullptr,
                capture.context.GetAddressOf());
            if (FAILED(hr)) return hr;
            ComPtr<IDXGIOutput1> output1;
            hr = output.As(&output1);
            if (FAILED(hr)) return hr;
            hr = output1->DuplicateOutput(capture.device.Get(), capture.duplication.GetAddressOf());
            if (FAILED(hr)) return hr;
            DXGI_OUTDUPL_DESC duplicationDescription{};
            capture.duplication->GetDesc(&duplicationDescription);
            if (duplicationDescription.ModeDesc.Format != DXGI_FORMAT_B8G8R8A8_UNORM)
                return DXGI_ERROR_UNSUPPORTED;
            D3D11_TEXTURE2D_DESC stagingDescription{};
            stagingDescription.Width = 96; stagingDescription.Height = 8;
            stagingDescription.MipLevels = 1; stagingDescription.ArraySize = 1;
            stagingDescription.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
            stagingDescription.SampleDesc.Count = 1;
            stagingDescription.Usage = D3D11_USAGE_STAGING;
            stagingDescription.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            hr = capture.device->CreateTexture2D(&stagingDescription, nullptr, capture.staging.GetAddressOf());
            if (FAILED(hr)) return hr;
            LARGE_INTEGER frequency{};
            if (!QueryPerformanceFrequency(&frequency)) return HRESULT_FROM_WIN32(GetLastError());
            DXGI_ADAPTER_DESC1 adapterDescription{};
            hr = adapter->GetDesc1(&adapterDescription);
            if (FAILED(hr)) return hr;
            capture.info = { frequency.QuadPart, r.left, r.top, r.right, r.bottom,
                duplicationDescription.ModeDesc.RefreshRate.Numerator,
                duplicationDescription.ModeDesc.RefreshRate.Denominator,
                static_cast<uint32_t>(description.Rotation), oi,
                (static_cast<int64_t>(adapterDescription.AdapterLuid.HighPart) << 32) |
                    static_cast<uint32_t>(adapterDescription.AdapterLuid.LowPart) };
            return S_OK;
        }
    }
    return DXGI_ERROR_NOT_FOUND;
}

extern "C" __declspec(dllexport) HRESULT __cdecl ObserverCaptureCreate(int x, int y, void** result, CaptureInfo* info)
{
    if (!result || !info) return E_POINTER;
    *result = nullptr; *info = {};
    std::unique_ptr<Capture> capture(new (std::nothrow) Capture());
    if (!capture) return E_OUTOFMEMORY;
    const HRESULT hr = Initialize(*capture, x, y);
    if (FAILED(hr)) return hr;
    *info = capture->info; *result = capture.release();
    return S_OK;
}

struct ReleaseAcquiredFrame
{
    IDXGIOutputDuplication* duplication;
    bool held = true;
    ~ReleaseAcquiredFrame() { if (held) duplication->ReleaseFrame(); }
    HRESULT Done() { held = false; return duplication->ReleaseFrame(); }
};

static void CopyRows(const uint8_t* source, uint32_t rowPitch, uint8_t* bgra)
{
    for (UINT row = 0; row < 8; ++row)
        std::memcpy(bgra + row * 96 * 4, source + row * rowPitch, 96 * 4);
}

extern "C" __declspec(dllexport) HRESULT __cdecl ObserverCaptureCpuCopyRows(const uint8_t* source,
    uint32_t sourceSize, uint32_t rowPitch, uint8_t* bgra, uint32_t bufferSize, uint32_t* frameInfoSize)
{
    if (!source || !bgra || !frameInfoSize) return E_POINTER;
    *frameInfoSize = sizeof(FrameInfo);
    if (rowPitch < 384 || rowPitch > sourceSize / 8 || bufferSize != 3072) return E_INVALIDARG;
    CopyRows(source, rowPitch, bgra);
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT __cdecl ObserverCaptureNext(void* handle, int x, int y,
    uint32_t timeoutMs, uint8_t* bgra, uint32_t bufferSize, FrameInfo* info)
{
    if (!handle || !bgra || !info) return E_POINTER;
    if (bufferSize != 96 * 8 * 4 || timeoutMs > 1000) return E_INVALIDARG;
    *info = {};
    auto& capture = *static_cast<Capture*>(handle);
    const auto& bounds = capture.info;
    if (x < bounds.left || y < bounds.top || x + 1280 > bounds.right || y + 800 > bounds.bottom)
        return E_INVALIDARG;
    DXGI_OUTDUPL_FRAME_INFO nativeInfo{};
    ComPtr<IDXGIResource> desktop;
    HRESULT hr = capture.duplication->AcquireNextFrame(timeoutMs, &nativeInfo, desktop.GetAddressOf());
    if (hr == DXGI_ERROR_WAIT_TIMEOUT) return S_FALSE;
    if (FAILED(hr)) return hr; // ACCESS_LOST is explicit; never silently recreate or fall back.
    ReleaseAcquiredFrame release{ capture.duplication.Get() };
    LARGE_INTEGER acquired{};
    QueryPerformanceCounter(&acquired);
    info->lastPresentQpc = nativeInfo.LastPresentTime.QuadPart;
    info->lastMouseQpc = nativeInfo.LastMouseUpdateTime.QuadPart;
    info->acquiredQpc = acquired.QuadPart;
    info->accumulatedFrames = nativeInfo.AccumulatedFrames;
    info->pointerOnly = nativeInfo.LastPresentTime.QuadPart == 0 ? 1u : 0u;
    info->protectedContentMaskedOut = nativeInfo.ProtectedContentMaskedOut ? 1u : 0u;
    info->rectsCoalesced = nativeInfo.RectsCoalesced ? 1u : 0u;
    if (!info->pointerOnly)
    {
        ComPtr<ID3D11Texture2D> texture;
        hr = desktop.As(&texture);
        if (FAILED(hr)) return hr;
        const UINT left = static_cast<UINT>(x - bounds.left), top = static_cast<UINT>(y - bounds.top);
        D3D11_TEXTURE2D_DESC sourceDescription{};
        texture->GetDesc(&sourceDescription);
        info->sourceWidth = sourceDescription.Width; info->sourceHeight = sourceDescription.Height;
        info->sourceFormat = static_cast<uint32_t>(sourceDescription.Format);
        info->roiLeft = left; info->roiTop = top;
        D3D11_BOX box{ left, top, 0, left + 96, top + 8, 1 };
        capture.context->CopySubresourceRegion(capture.staging.Get(), 0, 0, 0, 0, texture.Get(), 0, &box);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        hr = capture.context->Map(capture.staging.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (FAILED(hr)) return hr;
        info->rowPitch = mapped.RowPitch;
        CopyRows(static_cast<const uint8_t*>(mapped.pData), mapped.RowPitch, bgra);
        std::memcpy(&info->nativeFirstCellBgra, bgra + (4 * 96 + 1) * 4, sizeof(uint32_t));
        uint32_t hash = 2166136261u;
        for (UINT i = 0; i < 3072; ++i) hash = (hash ^ bgra[i]) * 16777619u;
        info->nativeBufferHash = hash;
        capture.context->Unmap(capture.staging.Get(), 0);
    }
    desktop.Reset();
    hr = release.Done();
    LARGE_INTEGER completed{};
    QueryPerformanceCounter(&completed);
    info->completedQpc = completed.QuadPart;
    return hr;
}

extern "C" __declspec(dllexport) void __cdecl ObserverCaptureDispose(void* handle)
{
    delete static_cast<Capture*>(handle);
}
