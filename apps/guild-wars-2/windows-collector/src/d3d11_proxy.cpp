#include "collector.h"
#include "d3d11_overrides.h"
#include "d3d11_proxy_exports.h"
#include "d3d11_system.h"

#include <windows.h>

#include <string>
#include <string_view>

namespace {

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

FARPROC GetSystemExport(WORD ordinal) {
    InitOnceExecuteOnce(&system_d3d11_once, LoadSystemD3D11, nullptr, nullptr);
    return system_d3d11 == nullptr ? nullptr : GetProcAddress(system_d3d11, MAKEINTRESOURCEA(ordinal));
}

FARPROC GetSystemExport(std::string_view export_name) {
    if (export_name.empty()) {
        return nullptr;
    }

    InitOnceExecuteOnce(&system_d3d11_once, LoadSystemD3D11, nullptr, nullptr);
    const std::string export_name_copy(export_name);
    return system_d3d11 == nullptr ? nullptr : GetProcAddress(system_d3d11, export_name_copy.c_str());
}

} // namespace

namespace theorymancer::gw2 {

FARPROC ResolveSystemD3D11Export(std::string_view export_name) {
    return GetSystemExport(export_name);
}

} // namespace theorymancer::gw2

extern "C" FARPROC WINAPI ResolveD3D11Export(DWORD ordinal) {
    const auto export_name = theorymancer::gw2::GetD3D11ProxyExportName(ordinal);
    const FARPROC override_target = theorymancer::gw2::GetD3D11Override(export_name);
    if (override_target != nullptr) {
        return override_target;
    }

    if (ordinal > MAXWORD) {
        theorymancer::gw2::ReportMissingSystemD3D11Export(export_name, ordinal);
        return nullptr;
    }

    const FARPROC export_address = GetSystemExport(static_cast<WORD>(ordinal));
    if (export_address == nullptr) {
        theorymancer::gw2::ReportMissingSystemD3D11Export(export_name, ordinal);
    }

    return export_address;
}

extern "C" void WINAPI StartCollectorDiagnosticsForD3D11Proxy() {
    theorymancer::gw2::StartCollectorDiagnostics();
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) {
    return TRUE;
}
