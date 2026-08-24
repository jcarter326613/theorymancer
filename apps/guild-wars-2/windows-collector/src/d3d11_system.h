#pragma once

#include <windows.h>

#include <string_view>

namespace theorymancer::gw2 {

// Typed overrides use this to call the original System32 export without re-entering the proxy.
FARPROC ResolveSystemD3D11Export(std::string_view export_name);

} // namespace theorymancer::gw2
