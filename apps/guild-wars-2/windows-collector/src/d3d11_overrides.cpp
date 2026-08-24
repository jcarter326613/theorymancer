#include "d3d11_overrides.h"

#include <array>

namespace theorymancer::gw2 {
namespace {

struct D3D11Override {
    std::string_view export_name;
    FARPROC target;
};

// Add documented, typed wrappers here once a capture point is understood.
constexpr std::array<D3D11Override, 0> overrides{};

} // namespace

FARPROC GetD3D11Override(std::string_view export_name) {
    for (const D3D11Override& override_entry : overrides) {
        if (override_entry.export_name == export_name) {
            return override_entry.target;
        }
    }

    return nullptr;
}

} // namespace theorymancer::gw2
