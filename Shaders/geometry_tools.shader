vec3 ProjectPointOnPlane(vec3 point_on_plane, vec3 plane_normal, vec3 point)
{
    vec3 v = point - point_on_plane;
    float dist = dot(v, plane_normal);
    return point - dist * plane_normal.xyz;
}

vec3 LinePlaneIntersection(vec3 line_p0, vec3 line_p1, vec3 point_on_plane, vec3 plane_normal)
{
    const float epsilon = 1e-6f;

    vec3 u = line_p1 - line_p0;
    float d = dot(plane_normal, u);

    if(abs(d) > epsilon)
    {
        vec3 w = line_p0 - point_on_plane;
        float fac = -dot(plane_normal, w) / d;
        return line_p0 + u * fac;
    }

    return vec3(0.0); //TODO signal no intersection
}

vec3 ProjectPointOnLine(vec3 line_p0, vec3 line_p1, vec3 point)
{
    vec3 AB = line_p1 - line_p0;
    vec3 AP = point - line_p0;

    return line_p0 + (dot(AP, AB) / dot(AB, AB)) * AB;
}



vec2 intersectBox(vec3 rayPos, vec3 rayDir, vec3 box_min, vec3 box_max) {
	vec3 t_min = (box_min - rayPos) / rayDir;
	vec3 t_max = (box_max - rayPos) / rayDir;
	vec3 t1 = min(t_min, t_max);
	vec3 t2 = max(t_min, t_max);
	float t_near = max(max(t1.x, t1.y), t1.z);
	float t_far = min(min(t2.x, t2.y), t2.z);
	return vec2(t_near, t_far);
}

vec2 intersectSphere(vec3 rayPos, vec3 dir, vec3 pos, float radius) {
	vec3 op = pos - rayPos;
	float b = dot(op, dir);
	float det = b * b - dot(op, op) + radius * radius;
	if (det < 0.) return vec2(-1.0, -1.0);

	det = sqrt(det);
	float t1 = b - det;
	float t2 = b + det;

	return vec2( min(t1, t2), max(t1, t2) );
}

float intersectPlane(vec3 rayPos, vec3 rayDir, vec3 plane_normal, float plane_distance) {
	return -(dot(rayPos, plane_normal) + plane_distance) / dot(rayDir, plane_normal);
}

float intersectTriangle(vec3 rayPos, in vec3 rayDir, in vec3 v0, in vec3 v1, in vec3 v2 )
{
    vec3 v1v0 = v1 - v0;
    vec3 v2v0 = v2 - v0;
    vec3 rov0 = rayPos - v0;

#if 0
    float d = 1.0/determinant(mat3(v1v0, v2v0, -rayDir ));
    float u =   d*determinant(mat3(rov0, v2v0, -rayDir ));
    float v =   d*determinant(mat3(v1v0, rov0, -rayDir ));
    float t =   d*determinant(mat3(v1v0, v2v0, rov0));
#else
    vec3  n = cross( v1v0, v2v0 );
    vec3  q = cross( rov0, rayDir );
    float d = 1.0/dot( rayDir, n );
    float u = d*dot( -q, v2v0 );
    float v = d*dot(  q, v1v0 );
    float t = d*dot( -n, rov0 );
#endif    

    if( u<0.0 || v<0.0 || (u+v)>1.0 ) t = -1.0;
    
    return t;
}

vec3 sign_nonzero(vec3 x)
{
    return step(0.0, x) * 2.0 - 1.0;
}
