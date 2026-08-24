#include "d3d11_overrides.h"
#include "d3d11_proxy_exports.h"

#include <iostream>

int wmain() {
    for (const theorymancer::gw2::D3D11ProxyExport& export_entry : theorymancer::gw2::kD3D11ProxyExports) {
        if (theorymancer::gw2::GetD3D11Override(export_entry.name) != nullptr) {
            std::cerr << "The initial D3D11 override registry must be empty.\n";
            return 1;
        }
    }

    return 0;
}
