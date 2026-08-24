#include "collector.h"

#include <windows.h>

#include <filesystem>
#include <fstream>
#include <mutex>
#include <string>

namespace theorymancer::gw2 {
namespace {

std::once_flag diagnostics_once;

std::filesystem::path GetLogPath() {
    wchar_t local_app_data[MAX_PATH]{};
    const DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", local_app_data, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) {
        return {};
    }

    return std::filesystem::path(local_app_data) / L"Theorymancer" / L"guild-wars-2-collector.log";
}

void WriteDiagnostics() {
    const std::filesystem::path log_path = GetLogPath();
    if (log_path.empty()) {
        return;
    }

    std::error_code error;
    std::filesystem::create_directories(log_path.parent_path(), error);
    if (error) {
        return;
    }

    wchar_t executable_path[MAX_PATH]{};
    const DWORD executable_length = GetModuleFileNameW(nullptr, executable_path, MAX_PATH);
    if (executable_length == 0 || executable_length >= MAX_PATH) {
        return;
    }

    std::ofstream log(log_path, std::ios::app);
    if (!log) {
        return;
    }

    log << "collector_loaded process_id=" << GetCurrentProcessId()
        << " executable=" << std::filesystem::path(executable_path).string() << '\n';
}

} // namespace

void StartCollectorDiagnostics() {
    std::call_once(diagnostics_once, WriteDiagnostics);
}

} // namespace theorymancer::gw2
