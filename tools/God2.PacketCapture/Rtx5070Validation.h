#pragma once

#include "Core.h"

namespace god2 {

// Validation-only entrypoint. It is intentionally absent from the GUI and
// does not alter capture, analysis, or adaptive production dispatch behavior.
int RunRtx5070Validation(const fs::path& package_root);

} // namespace god2
