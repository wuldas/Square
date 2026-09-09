shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

uniform bool parent_enabled = false;
uniform sampler2D parent_mask : filter_nearest, repeat_disable;
uniform vec4 parent_rect;
uniform vec2 mask_origin;

varying vec2 global_position;

void vertex() {
    global_position = VERTEX + mask_origin;
}

void fragment() {
    float coverage = 1.0;
    if (parent_enabled) {
        vec2 size = max(parent_rect.zw, vec2(0.000001));
        vec2 uv = (global_position - parent_rect.xy) / size;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0))))
            coverage = 0.0;
        else
            coverage = texture(parent_mask, uv).a;
    }
    COLOR = vec4(coverage, coverage, coverage, coverage);
}
