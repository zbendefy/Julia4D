#version 430 core

layout (location = 0) in vec3 a_Position;        
layout (location = 1) in vec3 a_Normal;
layout (location = 2) in vec2 a_Texcoord;       
layout (location = 3) in vec3 a_Tangent;

out vec3 v_Position;

void main()                        
{
	v_Position = a_Position;
	gl_Position = vec4(a_Position, 1.0);
} 

