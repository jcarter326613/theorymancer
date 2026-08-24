#include <windows.h>

#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <string>

extern "C" FARPROC WINAPI ResolveD3D11Export(DWORD ordinal);

namespace {

std::wstring GetEnvironmentVariableValue(const wchar_t* name) {
    const DWORD required_length = GetEnvironmentVariableW(name, nullptr, 0);
    if (required_length == 0) {
        return {};
    }

    std::wstring value(required_length, L'\0');
    if (GetEnvironmentVariableW(name, value.data(), required_length) == 0) {
        return {};
    }
    value.pop_back();
    return value;
}

} // namespace

int wmain() {
    const std::filesystem::path test_app_data =
        std::filesystem::temp_directory_path() / L"theorymancer-d3d11-missing-export-test" /
        std::to_wstring(GetCurrentProcessId());
    const std::wstring original_app_data = GetEnvironmentVariableValue(L"LOCALAPPDATA");
    std::error_code error;
    std::filesystem::remove_all(test_app_data, error);
    std::filesystem::create_directories(test_app_data, error);
    if (error || !SetEnvironmentVariableW(L"LOCALAPPDATA", test_app_data.c_str())) {
        std::wcerr << L"Could not prepare an isolated LOCALAPPDATA directory.\n";
        return 1;
    }

    const FARPROC export_address = ResolveD3D11Export(0);
    SetEnvironmentVariableW(L"LOCALAPPDATA", original_app_data.empty() ? nullptr : original_app_data.c_str());
    if (export_address != nullptr) {
        std::wcerr << L"Ordinal zero unexpectedly resolved from the system D3D11 DLL.\n";
        return 1;
    }

    const std::filesystem::path log_path = test_app_data / L"Theorymancer" / L"guild-wars-2-collector.log";
    std::ifstream log(log_path);
    const std::string log_contents(std::istreambuf_iterator<char>(log), {});
    std::filesystem::remove_all(test_app_data, error);

    if (log_contents.find("system_d3d11_export_missing ordinal=0 name=<unknown>") == std::string::npos) {
        std::cerr << "The missing system export was not recorded in the diagnostic log.\n";
        return 1;
    }

    return 0;
}
