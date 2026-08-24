#include "collector.h"

#include <windows.h>

#include <string>

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

} // namespace

extern "C" FARPROC WINAPI ResolveD3D11Export(DWORD ordinal) {
    if (ordinal > MAXWORD) {
        return nullptr;
    }

    return GetSystemExport(static_cast<WORD>(ordinal));
}

extern "C" void WINAPI StartCollectorDiagnosticsForD3D11Proxy() {
    theorymancer::gw2::StartCollectorDiagnostics();
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) {
    return TRUE;
}
