#version 430 core

#define SKYBOX_BACKGROUND 1

in vec3 v_Position; 
out vec4 fragmentColor;

uniform vec3 engine_cameraPosition;
uniform vec3 engine_cameraDirection;
uniform float engine_camera_aspect_ratio;
uniform float u_camera_fov;
uniform int u_resolution_i;
uniform int u_resolution_i_zdiv;
uniform int u_resolution_i_sq_zdiv;
uniform float u_resolution_f;
uniform float bitindex_mod_Factor;
uniform float u_xray_percent;
uniform vec3 u_lightDir;
uniform float u_ssao_strength;

uniform sampler2D mat_textureDiffuse;
uniform sampler2D mat_textureNormal;
uniform sampler2D ssaoTexture;
#if SKYBOX_BACKGROUND
uniform samplerCube skybox_background;
#endif

#pragma include("julia_common")
#pragma include("color_tools")

vec3 Shade(vec3 pos, vec3 rayDir, vec3 normal, float depth, float ssao)
{
	ssao = 1.0 - ((1.0 - ssao) * u_ssao_strength);

	float shading_half_lambert = dot(u_lightDir, normal) * 0.25 + 0.75;
	vec3 color = hsv2rgb(vec3(pos.y * 0.5 + 0.5, 0.4, 1.0)) * 0.95;

	float specular = pow(max(0.0, dot(reflect(-u_lightDir, normal), -rayDir)), 128) * 0.15;

	return color * shading_half_lambert * ssao + vec3(specular);
}

void main() 
{
	vec3 rayDir = getRayDir(engine_cameraDirection, v_Position.x, v_Position.y, engine_camera_aspect_ratio, u_camera_fov);
	vec2 uv = v_Position.xy * 0.5 + 0.5;
	float depth = texture(mat_textureDiffuse, uv).r;
	vec3 normal = texture(mat_textureNormal, uv).rgb;

	if ( depth >= BackgroundDistanceCheck)
	{
		const vec3 boxMax = vec3(2.0, 2.0, -2.0 + u_xray_percent * 4.0);
		vec2 intersect = intersectBox(engine_cameraPosition, rayDir, vec3(-2.0), boxMax );
		if ( all( lessThan( engine_cameraPosition, boxMax ) ) && all( greaterThan( engine_cameraPosition, vec3(-2.0) ) ) ){
			intersect.x = 0.0;
		}
		if (intersect.x >= 0.0 && intersect.x < intersect.y)
		{
			fragmentColor = vec4( 0.1, 0.1, 0.1, 1.0);
		}
		else
		{
#if SKYBOX_BACKGROUND
			fragmentColor = texture(skybox_background, rayDir.xyz);
#else
			fragmentColor = vec4( rayDir.xyz * 0.1 + 0.1, 1.0);
#endif
		}
		return;
	}

	vec3 worldPos = engine_cameraPosition + rayDir * depth;
	
	float ssao_value = texture(ssaoTexture, uv).r;
	vec3 color = Shade(worldPos, rayDir, normal, depth, ssao_value);

	fragmentColor = vec4(color, 1.0);
}









