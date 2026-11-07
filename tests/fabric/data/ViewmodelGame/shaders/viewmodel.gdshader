shader_type spatial;

uniform float fov = 60.0;
uniform vec4 tint : source_color = vec4(0.8, 0.7, 0.2, 1.0);

void vertex() {
	float scale = 1.0 / tan(fov * 0.5 * PI / 180.0);
	PROJECTION_MATRIX[0][0] = scale / (VIEWPORT_SIZE.x / VIEWPORT_SIZE.y);
	PROJECTION_MATRIX[1][1] = -scale;
	POSITION = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX.xyz, 1.0);
	POSITION.z = mix(POSITION.z, 0, -2);
}

void fragment() {
	ALBEDO = tint.rgb;
}
