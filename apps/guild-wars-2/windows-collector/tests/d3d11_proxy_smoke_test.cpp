#include <windows.h>
#include <d3d11.h>

#include <filesystem>
#include <iostream>
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

std::wstring GetModulePath(HMODULE module) {
    wchar_t path[MAX_PATH]{};
    const DWORD length = GetModuleFileNameW(module, path, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) {
        return {};
    }

    return path;
}

} // namespace

int wmain(int argument_count, wchar_t* arguments[]) {
    if (argument_count != 2) {
        std::wcerr << L"Usage: d3d11_proxy_smoke_test <path-to-proxy-d3d11.dll>\n";
        return 2;
    }

    const std::filesystem::path expected_proxy_path = std::filesystem::weakly_canonical(arguments[1]);
    HMODULE proxy = LoadLibraryW(expected_proxy_path.c_str());
    if (proxy == nullptr) {
        std::wcerr << L"Could not load the proxy: " << GetLastError() << L'\n';
        return 1;
    }

    const std::filesystem::path loaded_proxy_path = std::filesystem::weakly_canonical(GetModulePath(proxy));
    if (loaded_proxy_path != expected_proxy_path) {
        std::wcerr << L"LoadLibrary resolved the system D3D11 DLL instead of the supplied proxy.\n";
        return 1;
    }

    const auto create_device = reinterpret_cast<CreateDevice>(GetProcAddress(proxy, "D3D11CreateDevice"));
    if (create_device == nullptr) {
        std::wcerr << L"The proxy does not export D3D11CreateDevice.\n";
        return 1;
    }

    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    const HRESULT result = create_device(
        nullptr,
        D3D_DRIVER_TYPE_WARP,
        nullptr,
        0,
        nullptr,
        0,
        D3D11_SDK_VERSION,
        &device,
        nullptr,
        &context);

    if (context != nullptr) {
        context->Release();
    }
    if (device != nullptr) {
        device->Release();
    }
    FreeLibrary(proxy);

    if (FAILED(result)) {
        std::wcerr << L"D3D11CreateDevice through the proxy failed: 0x" << std::hex
                   << static_cast<unsigned long>(result) << L'\n';
        return 1;
    }

    return 0;
}
