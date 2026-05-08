#include "render/Bar.h"

#include <algorithm>

#include "render/Glyphs.h"

std::string hp_bar(int current, int maximum, int width) {
    if (width <= 0) return {};
    if (maximum <= 0) maximum = 1;
    int clamped = std::max(0, std::min(current, maximum));
    int filled_chars = (clamped * width) / maximum;
    if (clamped > 0 && filled_chars == 0) filled_chars = 1;
    std::string out;
    out.reserve(static_cast<size_t>(width) * 3);
    for (int i = 0; i < filled_chars; ++i) out += glyphs::kFullBlock;
    for (int i = filled_chars; i < width; ++i) out += glyphs::kEmptyBlock;
    return out;
}
