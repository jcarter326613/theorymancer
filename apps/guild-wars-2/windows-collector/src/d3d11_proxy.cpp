#include "collector.h"

#include <windows.h>
#include <d3d11.h>

#include <string>

namespace {

using CreateDevice = HRESULT(WINAPI*)(
    IDXGIAdapter*,
    D3D_DRIVER_TYPE,
    HMODULE,
    UINT,
    const D3D_FEATURE_LEVEL*,
    UINT,
    UINT,
    ID3D11Device**,
    D3D_FEATURE_LEVEL*,
    ID3D11DeviceContext**);

using CreateDeviceAndSwapChain = HRESULT(WINAPI*)(
    IDXGIAdapter*,
    D3D_DRIVER_TYPE,
    HMODULE,
    UINT,
    const D3D_FEATURE_LEVEL*,
    UINT,
    UINT,
    const DXGI_SWAP_CHAIN_DESC*,
    IDXGISwapChain**,
    ID3D11Device**,
    D3D_FEATURE_LEVEL*,
    ID3D11DeviceContext**);

INIT_ONCE system_d3d11_once = INIT_ONCE_STATIC_INIT;
HMODULE system_d3d11 = nullptr;

BOOL CALLBACK LoadSystemD3D11(PINIT_ONCE, PVOID, PVOID*) {
    wchar_t system_directory[MAX_PATH]{};
    const UINT length = GetSystemDirectoryW(system_directory, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) {
        return FALSE;
    }

    const std::wstring system_dll = std::wstring(system_directory) + L"\\d3d11.dll";
    system_d3d11 = LoadLibraryW(system_dll.c_str());
    return system_d3d11 != nullptr;
}

FARPROC GetSystemExport(const char* name) {
    InitOnceExecuteOnce(&system_d3d11_once, LoadSystemD3D11, nullptr, nullptr);
    return system_d3d11 == nullptr ? nullptr : GetProcAddress(system_d3d11, name);
}

} // namespace

extern "C" HRESULT WINAPI D3D11CreateDevice(
    IDXGIAdapter* adapter,
    D3D_DRIVER_TYPE driver_type,
    HMODULE software,
    UINT flags,
    const D3D_FEATURE_LEVEL* feature_levels,
    UINT feature_levels_count,
    UINT sdk_version,
    ID3D11Device** device,
    D3D_FEATURE_LEVEL* feature_level,
    ID3D11DeviceContext** immediate_context) {
    theorymancer::gw2::StartCollectorDiagnostics();

    const auto create_device = reinterpret_cast<CreateDevice>(GetSystemExport("D3D11CreateDevice"));
    if (create_device == nullptr) {
        return E_FAIL;
    }

    return create_device(adapter, driver_type, software, flags, feature_levels, feature_levels_count,
                         sdk_version, device, feature_level, immediate_context);
}

extern "C" HRESULT WINAPI D3D11CreateDeviceAndSwapChain(
    IDXGIAdapter* adapter,
    D3D_DRIVER_TYPE driver_type,
    HMODULE software,
    UINT flags,
    const D3D_FEATURE_LEVEL* feature_levels,
    UINT feature_levels_count,
    UINT sdk_version,
    const DXGI_SWAP_CHAIN_DESC* swap_chain_description,
    IDXGISwapChain** swap_chain,
    ID3D11Device** device,
    D3D_FEATURE_LEVEL* feature_level,
    ID3D11DeviceContext** immediate_context) {
    theorymancer::gw2::StartCollectorDiagnostics();

    const auto create_device_and_swap_chain =
        reinterpret_cast<CreateDeviceAndSwapChain>(GetSystemExport("D3D11CreateDeviceAndSwapChain"));
    if (create_device_and_swap_chain == nullptr) {
        return E_FAIL;
    }

    return create_device_and_swap_chain(adapter, driver_type, software, flags, feature_levels,
                                         feature_levels_count, sdk_version, swap_chain_description,
                                         swap_chain, device, feature_level, immediate_context);
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) {
    return TRUE;
}
