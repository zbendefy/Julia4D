
#pragma include("geometry_tools")

#define FractalRenderSize 4.0
#define FractalRenderHalfSize (FractalRenderSize * 0.5)

#define Epsilon 1e-5

#define BackgroundDistanceWrite 1000.0
#define BackgroundDistanceCheck (BackgroundDistanceWrite * 0.9)

vec3 getRayDir(vec3 dir, float screenX, float screenY, float aspectRatio, float camera_fov)
{
	const vec3 rel_up = vec3(0, 0, 1);
	vec3 right = normalize(cross(dir, rel_up));
	vec3 up = normalize(cross(right, dir));

	return normalize(dir + right * screenX * aspectRatio * camera_fov + up * screenY * camera_fov);
}

vec2 getScreenPosForRay(vec3 camera_pos, vec3 camera_dir, vec3 pos, vec3 right, vec3 up, vec3 to_screen_origo_point, vec3 to_screen_1_1_corner, vec3 screen_origo_point, vec3 screen_1_1_corner)
{
	vec3 screen_projected = LinePlaneIntersection(camera_pos, pos, camera_pos + camera_dir, camera_dir);

	vec3 y_projected = ProjectPointOnLine(screen_origo_point, screen_origo_point + up, screen_projected);
	vec3 x_projected = ProjectPointOnLine(screen_origo_point, screen_origo_point + right, screen_projected);

	vec3 x_max = ProjectPointOnLine(screen_origo_point, screen_origo_point + right, screen_1_1_corner);
	vec3 y_max = ProjectPointOnLine(screen_origo_point, screen_origo_point + up, screen_1_1_corner);

	return vec2(length(x_projected - screen_origo_point) / length(x_max - screen_origo_point), length(y_projected - screen_origo_point) / length(y_max - screen_origo_point));
}

bool GetJulia4D(vec4 coords, int iterations)
{
    const vec2 C = coords.xy;
    vec2 Zn = coords.zw;

    for (int i = 0; i < iterations; ++i)
    {
        const vec2 xy2 = Zn * Zn;
        if (xy2.x + xy2.y > 4.0)
        { 
            return false; //divergent, not part of the set
        }
        
        Zn.y = (Zn.y * Zn.x * 2.0) + C.y; 
        Zn.x = (xy2.x - xy2.y) + C.x; 
    }

    return true; //part of the set (likely)
}

vec4 GetJuliaCoords(vec3 spatial_coords, float time_coord, int time_coord_index)
{
  switch(time_coord_index)
  {
    case 0:
      return vec4(time_coord, spatial_coords.xyz);
    case 1:
      return vec4(spatial_coords.x, time_coord, spatial_coords.yz);
    case 2:
      return vec4(spatial_coords.xy, time_coord, spatial_coords.z);
    case 3:
    default:
      return vec4(spatial_coords.xyz, time_coord);
  }
}