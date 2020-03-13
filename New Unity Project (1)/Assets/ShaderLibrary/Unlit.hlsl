#ifndef MYRP_UNLIT_INCLUDED
#define MYRP_UNLIT_INCLUDED


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


UNITY_INSTANCING_BUFFER_START (PerInstance)//this defines colors in a way that gpu instancing will be maintained
	UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)
//CBUFFER_START(UnityPerMaterial)
///	float4 _Color;
//CBUFFER_END





		struct VertexInput {
			float4 vertex : POSITION;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct VertexOutput {
			float4 pos : SV_POSITION;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		VertexOutput UnlitPassVertex(VertexInput input) {
			VertexOutput output;
			UNITY_SETUP_INSTANCE_ID(input);//used when gpu Instancing is enabled to get the proper Model Matrix
			UNITY_TRANSFER_INSTANCE_ID(input, output);//this is used so that the fragment can use instanced variables

			float4 worldPos = mul(UNITY_MATRIX_M, float4(input.vertex.xyz, 1.0));
			output.pos = mul(unity_MatrixVP, worldPos);

			return output;
		} 

		float4 UnlitPassFragment(VertexOutput input) : SV_TARGET{
			UNITY_SETUP_INSTANCE_ID(input);
			return UNITY_ACCESS_INSTANCED_PROP (PerInstance,_Color);
		}


#endif // MYRP_UNLIT_INCLUDED


