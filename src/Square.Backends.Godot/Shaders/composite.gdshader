shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

void fragment() {
    vec4 source = texture(TEXTURE, UV);
    COLOR = vec4(source.rgb * COLOR.rgb, source.a * COLOR.a);
}
