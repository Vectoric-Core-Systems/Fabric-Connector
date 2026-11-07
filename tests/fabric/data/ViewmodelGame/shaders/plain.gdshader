shader_type spatial;

uniform vec4 tint : source_color = vec4(0.2, 0.4, 0.9, 1.0);

void vertex() {
	VERTEX += NORMAL * 0.01;
}

void fragment() {
	ALBEDO = tint.rgb;
}
