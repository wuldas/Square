shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform int brush_kind = 0;
uniform vec4 solid_color = vec4(1.0);
uniform vec2 gradient_start;
uniform vec2 gradient_end;
uniform vec2 gradient_center;
uniform float gradient_radius = 1.0;
uniform int gradient_spread = 0;
uniform sampler2D gradient_stops : filter_nearest, repeat_disable;
uniform int gradient_stop_count = 0;
uniform bool clip_enabled = false;
uniform vec4 clip_rect;
uniform bool mask_enabled = false;
uniform sampler2D mask_texture : filter_nearest, repeat_disable;
uniform vec4 mask_rect;
uniform bool coverage_enabled = false;
uniform sampler2D coverage_texture : filter_nearest, repeat_disable;
uniform vec4 coverage_rect;

varying vec2 square_position;

void vertex() {
    square_position = VERTEX;
}

float spread_value(float value) {
    if (gradient_spread == 1) {
        value = abs(mod(value, 2.0));
        return value > 1.0 ? 2.0 - value : value;
    }
    if (gradient_spread == 2) return value - floor(value);
    return clamp(value, 0.0, 1.0);
}

vec4 sample_gradient(float value) {
    if (gradient_stop_count <= 0) return vec4(0.0);
    value = spread_value(value);
    vec4 first_color = texelFetch(gradient_stops, ivec2(0, 0), 0);
    vec4 last_color = texelFetch(gradient_stops, ivec2(2 * gradient_stop_count - 2, 0), 0);
    float first_offset = texelFetch(gradient_stops, ivec2(1, 0), 0).r;
    float last_offset = texelFetch(gradient_stops, ivec2(2 * gradient_stop_count - 1, 0), 0).r;
    if (value <= first_offset) return first_color;
    if (value >= last_offset) return last_color;
    for (int i = 0; i < gradient_stop_count - 1; i++) {
        vec4 lower = texelFetch(gradient_stops, ivec2(i * 2, 0), 0);
        vec4 upper = texelFetch(gradient_stops, ivec2(i * 2 + 2, 0), 0);
        float lower_offset = texelFetch(gradient_stops, ivec2(i * 2 + 1, 0), 0).r;
        float upper_offset = texelFetch(gradient_stops, ivec2(i * 2 + 3, 0), 0).r;
        if (value <= upper_offset) {
            float span = max(upper_offset - lower_offset, 0.000001);
            return mix(lower, upper, clamp((value - lower_offset) / span, 0.0, 1.0));
        }
    }
    return last_color;
}

void fragment() {
    if (clip_enabled && (square_position.x < clip_rect.x || square_position.y < clip_rect.y ||
        square_position.x > clip_rect.z || square_position.y > clip_rect.w)) {
        discard;
    }
    if (mask_enabled) {
        vec2 mask_size = max(mask_rect.zw, vec2(0.000001));
        vec2 mask_uv = (square_position - mask_rect.xy) / mask_size;
        if (any(lessThan(mask_uv, vec2(0.0))) || any(greaterThan(mask_uv, vec2(1.0))))
            discard;
    }
    vec4 source = solid_color;
    if (brush_kind == 1) {
        vec2 delta = gradient_end - gradient_start;
        float denominator = max(dot(delta, delta), 0.000001);
        source = sample_gradient(dot(square_position - gradient_start, delta) / denominator);
    } else if (brush_kind == 2) {
        float radius = max(gradient_radius, 0.000001);
        source = sample_gradient(distance(square_position, gradient_center) / radius);
    } else if (brush_kind == 3) {
        source = texture(TEXTURE, UV);
    } else if (brush_kind == 4) {
        float coverage = texture(TEXTURE, UV).a;
        source = vec4(solid_color.rgb, solid_color.a * coverage);
    }

    float coverage = COLOR.a;
    if (mask_enabled)
        coverage *= texture(mask_texture, (square_position - mask_rect.xy) / max(mask_rect.zw, vec2(0.000001))).a;
    if (coverage_enabled)
        coverage *= texture(coverage_texture, (square_position - coverage_rect.xy) / max(coverage_rect.zw, vec2(0.000001))).a;
    float alpha = source.a * coverage;
    COLOR = vec4(source.rgb * alpha, alpha);
}
