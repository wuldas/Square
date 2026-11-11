#version 450

layout(set = 0, binding = 0) uniform sampler2D uTexture;
struct GradientStop { float offset; uint rgba; };
layout(std430, set = 0, binding = 1) readonly buffer GradientStops {
    GradientStop stops[];
};
layout(push_constant) uniform Material {
    layout(offset = 64) vec4 rowX;
    layout(offset = 80) vec4 rowY;
    layout(offset = 96) uvec4 info; // mode, spread (Pad=0/Reflect=1/Repeat=2), offset, count
} material;
layout(location = 0) in vec2 fragUV;
layout(location = 1) in vec4 fragColor;
layout(location = 0) out vec4 outColor;

vec4 stopColor(uint index) { return unpackUnorm4x8(stops[material.info.z + index].rgba); }
float stopOffset(uint index) { return stops[material.info.z + index].offset; }

vec4 gradient(float t) {
    uint count = material.info.w;
    if (count == 0u) return vec4(0.0);
    if (isnan(t) || isinf(t)) t = 0.0;
    if (material.info.y == 2u) t = t - floor(t);
    else if (material.info.y == 1u) {
        t = mod(abs(t), 2.0);
        if (t > 1.0) t = 2.0 - t;
    } else t = clamp(t, 0.0, 1.0);

    // Original order is significant at duplicate stops: first upper, last lower.
    uint minimum = 0u, maximum = 0u, lower = 0u, upper = 0u;
    bool hasLower = false, hasUpper = false;
    for (uint i = 0u; i < count; ++i) {
        float offset = stopOffset(i);
        if (offset < stopOffset(minimum)) minimum = i;
        if (offset >= stopOffset(maximum)) maximum = i;
        if (offset < t && (!hasLower || offset >= stopOffset(lower))) { lower = i; hasLower = true; }
        if (offset >= t && (!hasUpper || offset < stopOffset(upper))) { upper = i; hasUpper = true; }
    }
    if (t <= stopOffset(minimum)) return stopColor(minimum);
    if (t >= stopOffset(maximum)) return stopColor(maximum);
    if (!hasLower) lower = minimum;
    if (!hasUpper) upper = maximum;
    float span = stopOffset(upper) - stopOffset(lower);
    float amount = span > 0.0 ? clamp((t - stopOffset(lower)) / span, 0.0, 1.0) : 0.0;
    return mix(stopColor(lower), stopColor(upper), amount);
}

void main() {
    vec4 sampled = texture(uTexture, fragUV);
    if (material.info.x == 3u) {
        // Offscreen attachments contain premultiplied RGB. Existing pipeline blends straight RGB.
        vec3 straight = sampled.a > 0.0 ? sampled.rgb / sampled.a : vec3(0.0);
        outColor = vec4(straight * fragColor.rgb, sampled.a * fragColor.a * material.rowX.w);
    } else if (material.info.x == 1u || material.info.x == 2u) {
        vec3 position = vec3(gl_FragCoord.xy, 1.0);
        float x = dot(material.rowX.xyz, position);
        float t = material.info.x == 1u ? x : length(vec2(x, dot(material.rowY.xyz, position)));
        outColor = gradient(t) * sampled * fragColor;
    } else {
        outColor = sampled * fragColor;
    }
}
