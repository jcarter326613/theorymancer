#pragma once

#include <cstdint>
#include <string_view>

namespace theorymancer::gw2 {

void StartCollectorDiagnostics();
void ReportMissingSystemD3D11Export(std::string_view export_name, std::uint32_t ordinal);

} // namespace theorymancer::gw2
