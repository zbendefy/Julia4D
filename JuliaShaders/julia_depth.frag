#version 430 core

in vec3 v_Position; 
layout(location = 0) out float out_linear_depth;
layout(location = 1) out vec3 out_normals;

uniform vec3 engine_cameraPosition;
uniform vec3 engine_cameraDirection;
uniform float engine_camera_aspect_ratio;
uniform float u_camera_fov;
uniform int u_resolution_i;
uniform float u_resolution_f;
uniform float normedVoxelSize;
uniform float u_xray_percent;
uniform int u_smoothing;

layout (std430, binding = 2) readonly buffer voxels
{
  uint VoxelData[ ];
};

#ifdef USE_AS
layout (std430, binding = 3) readonly buffer voxels_as
{
  uint VoxelAS[ ];
};
#endif

#pragma include("julia_common")

bool TestSubBlock(uint subBlockData, uint subBlockIndex)
{
	return (subBlockData & (1u << subBlockIndex)) != 0u;
}

int GetBlockArrayIndex(ivec3 block_indices, ivec3 blockCount)
{	
	return block_indices.x * blockCount.y * blockCount.z + block_indices.y * blockCount.z + block_indices.z;
}

uint ResolveSubBlockIndex(uvec3 subBlockIndices)
{
	return subBlockIndices.x * BlockSizeY * BlockSizeZ + subBlockIndices.y * BlockSizeZ + subBlockIndices.z;
}

uint GetSubBlockId(ivec3 voxel_indices)
{
	ivec3 sub_block_indices;
	sub_block_indices = voxel_indices % ivec3(BlockSizeX, BlockSizeY, BlockSizeZ);
	return ResolveSubBlockIndex(sub_block_indices);
}

float GetBlockDataForTrilinear(ivec3 subBlockIndices, int baseBlockIndex, uint baseBlockData, ivec3 blockCount)
{
	int newBlockIndex = baseBlockIndex;

	if (subBlockIndices.x >= BlockSizeX)
	{
		subBlockIndices.x -= BlockSizeX;
		newBlockIndex += blockCount.y * blockCount.z;
	}
	else if (subBlockIndices.x < 0)
	{
		subBlockIndices.x += BlockSizeX;
		newBlockIndex -= blockCount.y * blockCount.z;
	}

	if (subBlockIndices.y >= BlockSizeY)
	{
		subBlockIndices.y -= BlockSizeY;
		newBlockIndex += blockCount.z;
	}
	else if (subBlockIndices.y < 0)
	{
		subBlockIndices.y += BlockSizeY;
		newBlockIndex -= blockCount.z;
	}

	if (subBlockIndices.z >= BlockSizeZ)
	{
		subBlockIndices.z -= BlockSizeZ;
		++newBlockIndex;
	}
	else if (subBlockIndices.z < 0)
	{
		subBlockIndices.z += BlockSizeZ;
		--newBlockIndex;
	}

	if (newBlockIndex != baseBlockIndex)
	{
		baseBlockData = VoxelData[newBlockIndex]; //TODO: redundant lookups when multiple vertices are crossing block boundary
	}

	return TestSubBlock(baseBlockData, ResolveSubBlockIndex( subBlockIndices )) ? 1.0 : 0.0;
}

bool TestPointTrilinear(vec3 normedPos, int blockIndex, uint blockData, vec3 blockCount, vec3 physicalBlockSizesNormed)
{
	const vec3 normedSubBlock = mod( normedPos, physicalBlockSizesNormed ) * blockCount; //scale subblock coordinates to between 0.0 and 1.0
	const ivec3 subBlockIndices = ivec3(normedSubBlock * vec3(BlockSizeX, BlockSizeY, BlockSizeZ));

	const vec3 normedSubVoxelCoords = mod(normedPos, normedVoxelSize) / normedVoxelSize;
	ivec3 subBlockIndicesModifier = ivec3(sign(normedSubVoxelCoords - vec3(0.5)));
	
	const float x0y0z0 = TestSubBlock(blockData, ResolveSubBlockIndex(subBlockIndices)) ? 1.0 : 0.0;
	const float x1y0z0 = GetBlockDataForTrilinear(subBlockIndices + ivec3(subBlockIndicesModifier.x,0,0), blockIndex, blockData, ivec3(blockCount));
	const float x0y1z0 = GetBlockDataForTrilinear(subBlockIndices + ivec3(0,subBlockIndicesModifier.y, 0), blockIndex, blockData, ivec3(blockCount));
	const float x1y1z0 = GetBlockDataForTrilinear(subBlockIndices + ivec3(subBlockIndicesModifier.xy,0), blockIndex, blockData, ivec3(blockCount));
	const float x0y0z1 = GetBlockDataForTrilinear(subBlockIndices + ivec3(0,0,subBlockIndicesModifier.z), blockIndex, blockData, ivec3(blockCount));
	const float x1y0z1 = GetBlockDataForTrilinear(subBlockIndices + ivec3(subBlockIndicesModifier.x,0,subBlockIndicesModifier.z), blockIndex, blockData, ivec3(blockCount));
	const float x0y1z1 = GetBlockDataForTrilinear(subBlockIndices + ivec3(0,subBlockIndicesModifier.yz), blockIndex, blockData, ivec3(blockCount));
	const float x1y1z1 = GetBlockDataForTrilinear(subBlockIndices + ivec3(subBlockIndicesModifier.xyz), blockIndex, blockData, ivec3(blockCount));
	
	vec3 lerpFactor = vec3(
		normedSubVoxelCoords.x > 0.5 ? (normedSubVoxelCoords.x - 0.5) : (0.5 - normedSubVoxelCoords.x),
		normedSubVoxelCoords.y > 0.5 ? (normedSubVoxelCoords.y - 0.5) : (0.5 - normedSubVoxelCoords.y),
		normedSubVoxelCoords.z > 0.5 ? (normedSubVoxelCoords.z - 0.5) : (0.5 - normedSubVoxelCoords.z)
	);
	
	vec4 zlerp = mix(vec4(x0y0z0, x1y0z0, x0y1z0, x1y1z0), vec4(x0y0z1, x1y0z1, x0y1z1, x1y1z1), lerpFactor.z);
	vec2 ylerp = mix(zlerp.xy, zlerp.zw, lerpFactor.y);
	float xyz = mix(ylerp.x, ylerp.y, lerpFactor.x);

	return xyz > 0.5;
}

struct CachedBlock
{
	uint data;
	int id;
};

vec3 CalculateTDelta(vec3 voxel_size, vec3 dir)
{
	float xtheta = acos(dot(normalize(vec3(0, dir.yz)), dir));
	float ytheta = acos(dot(normalize(vec3(dir.x, 0, dir.z)), dir));
	float ztheta = acos(dot(normalize(vec3(dir.xy, 0)), dir));

	return voxel_size / sin(vec3(xtheta, ytheta, ztheta));
}

vec3 CalculateTmax(vec3 initial_position, vec3 voxel_size, vec3 pos, vec3 dir, ivec3 step)
{
	//snap initial_position to grid
	if(step.x == 1)
	{
		initial_position.x += voxel_size.x - mod(initial_position.x, voxel_size.x);
	}
	else if (step.x == -1)
	{
		initial_position.x -= mod(initial_position.x, voxel_size.x);
	}
	if(step.y == 1)
	{
		initial_position.y += voxel_size.y - mod(initial_position.y, voxel_size.y);
	}
	else if (step.y == -1)
	{
		initial_position.y -= mod(initial_position.y, voxel_size.y);
	}
	if(step.z == 1)
	{
		initial_position.z += voxel_size.z - mod(initial_position.z, voxel_size.z);
	}
	else if (step.z == -1)
	{
		initial_position.z -= mod(initial_position.z, voxel_size.z);
	}

	vec3 ret;
	ret.x = intersectPlane(pos, dir, vec3(1, 0, 0), -initial_position.x);
	ret.y = intersectPlane(pos, dir, vec3(0, 1, 0), -initial_position.y);
	ret.z = intersectPlane(pos, dir, vec3(0, 0, 1), -initial_position.z);

	return ret;
}

void raytrace_step(inout vec3 tMax, inout ivec3 voxel_indices, ivec3 step, vec3 tDelta)
{
	if(tMax.x < tMax.y)
	{
		if(tMax.x < tMax.z)
		{
			tMax.x += tDelta.x;
			voxel_indices.x += step.x;
		}
		else
		{
			tMax.z += tDelta.z;
			voxel_indices.z += step.z;
		}
	}
	else
	{
		if(tMax.y < tMax.z)
		{
			tMax.y += tDelta.y;
			voxel_indices.y += step.y;
		}
		else
		{
			tMax.z += tDelta.z;
			voxel_indices.z += step.z;
		}
	}
}

bool rayTraceInternal(inout ivec3 voxel_indices, vec3 ray_origin, vec3 ray_dir, vec3 initial_position, int voxel_resolution, float xray_percentage, uint src_buffer_id)
{
	vec3 voxel_size = vec3(FractalRenderSize / float(voxel_resolution));
	const ivec3 blockCount = ivec3( voxel_resolution / BlockSizeX, voxel_resolution / BlockSizeY, voxel_resolution / BlockSizeZ);
	//const vec3 physicalBlockSizesNormed = vec3(1.0) / blockCount;
	uint max_xray_voxel_z_index = uint(float(voxel_resolution) * xray_percentage);

	ivec3 step = ivec3(sign(ray_dir));
	vec3 tMax = CalculateTmax(initial_position, voxel_size, ray_origin, ray_dir, step);

	vec3 tDelta = CalculateTDelta(voxel_size, ray_dir);

	CachedBlock cached_block;
	cached_block.data = 0u;
	cached_block.id = -1;

	while(all(greaterThanEqual(voxel_indices, ivec3(0))) && all(lessThan(voxel_indices, ivec3(voxel_resolution))))
	{
		ivec3 block_indices = voxel_indices / ivec3(BlockSizeX, BlockSizeY, BlockSizeZ);
		ivec3 sub_block_indices = voxel_indices % ivec3(BlockSizeX, BlockSizeY, BlockSizeZ);
		int current_block_id = GetBlockArrayIndex(block_indices, blockCount);

		if (cached_block.id != current_block_id)
		{
			cached_block.id = current_block_id;
			switch (src_buffer_id)
			{
#ifdef USE_AS
				case 1:
					cached_block.data = VoxelAS[current_block_id]; //TODO somehow pass the buffer in an other way instead of this...
					break;
#endif
				default:
					cached_block.data = VoxelData[current_block_id];
					break;
			}
		}

		if(cached_block.data != 0)
		{
			bool hitResult = false;
			if ( u_smoothing == 1 && src_buffer_id == 0){
				//hitResult = TestPointTrilinear(normedPos, int(lastBlockId), lastBlockData, blockCount, physicalBlockSizesNormed );
			} else {
				hitResult = TestSubBlock(cached_block.data, GetSubBlockId(voxel_indices));
			}
			if (hitResult)
			{
				return true; 
			}
		}

		raytrace_step(tMax, voxel_indices, step, tDelta);

		if(voxel_indices.z >= max_xray_voxel_z_index)
		{
			return false;
		}
	}
	return false;
}

//uses https://www.researchgate.net/publication/2611491_A_Fast_Voxel_Traversal_Algorithm_for_Ray_Tracing
float rayTrace(vec3 ray_origin, vec3 ray_dir, out vec3 normal)
{
	float x_ray = u_xray_percent;
	
#ifdef USE_AS
	float as_adjusted_xray_percentage = ceil(u_xray_percent * AS_Size) / float(AS_Size);
	x_ray = as_adjusted_xray_percentage;
#endif

	const vec3 boxMin = vec3(FractalRenderSize*-0.5);
	const vec3 boxMax = vec3(FractalRenderSize*0.5, FractalRenderSize*0.5, (FractalRenderSize*-0.5) + x_ray * FractalRenderSize);
	vec2 intersect = intersectBox(ray_origin, ray_dir, boxMin, boxMax );

	//if we are inside the box, still allow rendering
	if ( all( lessThan( ray_origin, boxMax ) ) && all( greaterThan( ray_origin, boxMin ) ) )
	{
		intersect.x = 0.0;
	}

	if (intersect.x >= 0.0 && intersect.x < intersect.y)
	{
		intersect.x += Epsilon;
		vec3 initial_position = ray_origin + intersect.x * ray_dir;
		vec3 voxel_coords_normalized = (initial_position / FractalRenderSize) + 0.5;

#ifdef USE_AS
		if(u_resolution_i >= AS_Size * BlockSizeX)  //skip using AcceleraionStructure where resolution is lower than the AS
		{
			ivec3 as_voxel_indices = ivec3(voxel_coords_normalized * AS_Size);
			bool as_hit = rayTraceInternal(as_voxel_indices, ray_origin, ray_dir, initial_position, AS_Size, as_adjusted_xray_percentage, 1);
			if(!as_hit)
			{
				return BackgroundDistanceWrite;
			}
			
			{
				vec3 as_voxel_size = vec3(FractalRenderSize / AS_Size);
				vec3 as_voxel_min = boxMin + as_voxel_size * as_voxel_indices;
				vec3 as_voxel_max = boxMin + as_voxel_size * (as_voxel_indices + ivec3(1));
				vec2 as_voxel_intersect = intersectBox(ray_origin, ray_dir, as_voxel_min, as_voxel_max );
				//return as_voxel_intersect.x;  // Uncomment to Visualize Acceleration Structure
				initial_position = ray_origin + ray_dir * (as_voxel_intersect.x + Epsilon);
			}

			voxel_coords_normalized = (initial_position / FractalRenderSize) + 0.5;
		}
#endif

		ivec3 voxel_indices = ivec3(voxel_coords_normalized * u_resolution_f);
		bool hit = rayTraceInternal(voxel_indices, ray_origin, ray_dir, initial_position, u_resolution_i, x_ray, 0);

		if (hit)
		{
			vec3 voxel_size = vec3(FractalRenderSize / u_resolution_f);
			vec3 voxel_min = boxMin + voxel_size * voxel_indices;
			vec3 voxel_max = boxMin + voxel_size * (voxel_indices + ivec3(1));
			vec2 voxel_intersect = intersectBox(ray_origin, ray_dir, voxel_min, voxel_max );

			vec3 voxel_center_worldpos = voxel_min * 0.5 + voxel_max * 0.5;
			vec3 hit_point_worldpos = ray_origin + ray_dir * voxel_intersect.x;
			vec3 spherical_normal = hit_point_worldpos - voxel_center_worldpos;
			vec3 normal_signs = sign(spherical_normal);
			int largest_idx = abs(spherical_normal[1]) > abs(spherical_normal[0]) ? 1 : 0;
			if(abs(spherical_normal[2]) > abs(spherical_normal[largest_idx]))
				largest_idx = 2;
			normal = vec3(0.0);
			normal[largest_idx] = normal_signs[largest_idx];

			return voxel_intersect.x;
		}
	}

	return BackgroundDistanceWrite;
}

vec3 getGridBackground(vec3 dir)
{
	vec3 background = dir.xyz*0.2+0.5;
	vec3 gridColor = vec3(0.0, 1.0, 0.0);
	if (fract(dir.x *10.0) < 0.1)
		background += gridColor;
	if (fract(dir.y *10.0) < 0.1)
		background += gridColor;
	if (fract(dir.z *10.0) < 0.1)
		background += gridColor;
	return background;
}

void main() 
{
	vec3 normal;
	vec3 dir = getRayDir(engine_cameraDirection, v_Position.x, v_Position.y, engine_camera_aspect_ratio, u_camera_fov);

	out_linear_depth = rayTrace(engine_cameraPosition, dir, normal);
	out_normals = normal;
}
