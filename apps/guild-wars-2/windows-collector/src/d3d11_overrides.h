#pragma once

#include <windows.h>

#include <string_view>

namespace theorymancer::gw2 {

// Returns a typed interception target for a documented export, if one is registered.
FARPROC GetD3D11Override(std::string_view export_name);

} // namespace theorymancer::gw2
