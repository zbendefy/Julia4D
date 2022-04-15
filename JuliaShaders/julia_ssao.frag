#version 430 core

in vec3 v_Position; 

out float fragmentColor;

uniform	mat4 ssaoViewProjMx;
uniform sampler2D input_depth_texture;
uniform sampler2D input_normal_texture;
uniform float u_camera_fov;
uniform int u_ssao_samples;
uniform float u_ssao_radius;
uniform vec3 engine_cameraPosition;
uniform vec3 engine_cameraDirection;
uniform float engine_camera_aspect_ratio;

#pragma include("julia_common")
#pragma include("noise")

float GetSSAO(vec3 worldPos, float worldPosDepth, vec3 normal, float sampleRadius, int offset)
{
	float shadowFactor = 1.0;

	const vec3 rel_up = vec3(0, 0, 1);
	vec3 right = normalize(cross(engine_cameraDirection, rel_up));
	vec3 up = normalize(cross(right, engine_cameraDirection));
	float ssao_value_step = 1.0 / float(u_ssao_samples);

	vec3 to_screen_origo_point = getRayDir(engine_cameraDirection, -1, -1, engine_camera_aspect_ratio, u_camera_fov);
	vec3 to_screen_1_1_corner = getRayDir(engine_cameraDirection, 1, 1, engine_camera_aspect_ratio, u_camera_fov);
	vec3 screen_origo_point = LinePlaneIntersection(engine_cameraPosition, engine_cameraPosition + to_screen_origo_point, engine_cameraPosition + engine_cameraDirection, engine_cameraDirection);
	vec3 screen_1_1_corner = LinePlaneIntersection(engine_cameraPosition, engine_cameraPosition + to_screen_1_1_corner, engine_cameraPosition + engine_cameraDirection, engine_cameraDirection);

	for(int i = 0; i < u_ssao_samples; ++i)
	{
		int ssao_sample_seed = i + 1;
	    vec3 noise = hash33(vec3(gl_FragCoord.xy * vec2(1.451, (i + 1)*3.13), float(i*2.86251))) * 2.0 - 1.0;
		noise = noise * noise * noise; //cluster sample points more towards the center to provide attenuation

		if (dot(noise, normal) < 0)
		{
			noise *= -1;  //mirror into the normal oriented hemisphere
		}

		vec3 samplePos = worldPos + noise * sampleRadius;
		vec2 samplePos_screen_coords = getScreenPosForRay(engine_cameraPosition, engine_cameraDirection, samplePos, right, up, to_screen_origo_point, to_screen_1_1_corner, screen_origo_point, screen_1_1_corner);
		
		float geometry_depth = texture(input_depth_texture, samplePos_screen_coords, 0 ).r;
		float sample_pos_depth = length(samplePos - engine_cameraPosition);

		if (geometry_depth <= sample_pos_depth && abs(geometry_depth - worldPosDepth) <= sampleRadius * 1.44 )
		{
			shadowFactor -= ssao_value_step;
		}
	}
	return shadowFactor;
}

void main()
{
	float depth = texture(input_depth_texture, v_Position.xy * 0.5 + 0.5).r;

	if(depth >= BackgroundDistanceCheck)
	{
		fragmentColor = 1.0;
		return;
	}

	vec3 rayDir = getRayDir(engine_cameraDirection, v_Position.x, v_Position.y, engine_camera_aspect_ratio, u_camera_fov);

	vec3 normal = texture(input_normal_texture, v_Position.xy * 0.5 + 0.5).rgb;
	vec3 world_pos = engine_cameraPosition + rayDir * depth;

	fragmentColor = GetSSAO(world_pos, depth, normal, u_ssao_radius, 17);
}