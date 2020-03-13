#ifndef MYRP_SHADOWCASTER_INCLUDED
#define MYRP_SHADOWCASTER_INCLUDED


#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"



//uses the CBUFFER_START() because cbuffer UnityPerFrame { ... }; isn't supported on all platforms
CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
CBUFFER_END
//using cbuffers because thats how unitystores the data, which should make the shader mroe efficient on some graphics apis
CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
CBUFFER_END

#define UNITY_MATRIX_M unity_ObjectToWorld
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"//this include potentially redefines UNITY_MATRIX_M to be an array of matrixes that get used when gpuinstancing is happening

struct VertexInput {
	float4 vertex : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID 
};

struct VertexOutput {
	float4 pos : SV_POSITION;
};

CBUFFER_START(_ShadowCasterBuffer)
	float _ShadowBias;
CBUFFER_END



VertexOutput ShadowCasterPassVertex(VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);//used when gpu Instancing is enabled to get the proper Model Matrix

	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.vertex.xyz, 1.0));
	output.pos = mul(unity_MatrixVP, worldPos);


	//this clamps the vertexes to the minimum z plane
	//in OpenGL the z value is flipped. Or rather, in non OpenGL its flipped reverse to the intuitive understanding that the near clip plane of the camera is the low number
	#if UNITY_REVERSED_Z
		output.pos.z -= _ShadowBias;
		output.pos.z = min(output.pos.z, output.pos.w*UNITY_NEAR_CLIP_VALUE);
	#else
		output.pos.z += _ShadowBias;
		output.pos.z = max(output.pos.z, output.pos.w*UNITY_NEAR_CLIP_VALUE);
	#endif

	return output;
} 

float4 ShadowCasterPassFragment(VertexOutput input) : SV_TARGET
{
	return 0;
}


#endif // MYRP_UNLIT_INCLUDED


