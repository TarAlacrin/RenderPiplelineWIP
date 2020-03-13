﻿#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED


#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"



//uses the CBUFFER_START() because cbuffer UnityPerFrame { ... }; isn't supported on all platforms
CBUFFER_START(UnityPerFrame)
	float4x4 unity_MatrixVP;
CBUFFER_END
//using cbuffers because thats how unitystores the data, which should make the shader mroe efficient on some graphics apis
CBUFFER_START(UnityPerDraw)
	float4x4 unity_ObjectToWorld;
float4 unity_LightData;
float4 unity_LightIndices[2];
CBUFFER_END

#define UNITY_MATRIX_M unity_ObjectToWorld
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"//this include potentially redefines UNITY_MATRIX_M to be an array of matrixes that get used when gpuinstancing is happening


UNITY_INSTANCING_BUFFER_START (PerInstance)//this defines colors in a way that gpu instancing will be maintained
	UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)
//CBUFFER_START(UnityPerMaterial)
///	float4 _Color;
//CBUFFER_END

#define MAX_VISIBLE_LIGHTS 16

CBUFFER_START(_LightBuffer)
	float4 _VisibleLightColors[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightDirectionsOrPositions [MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHTS];
	float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHTS];
	CBUFFER_END

CBUFFER_START(_ShadowBuffer)
	float4x4 _WorldToShadowMatrix;
CBUFFER_END

TEXTURE2D_SHADOW(_ShadowMap);
SAMPLER_CMP(sampler_ShadowMap);


float ShadowAttenuation(float3 worldPos)
{
	float4 shadowPos = mul(_WorldToShadowMatrix, float4(worldPos, 1.0));
	shadowPos.xyz /= shadowPos.w;

	return SAMPLE_TEXTURE2D_SHADOW(_ShadowMap, sampler_ShadowMap, shadowPos.xyz);
}

float3 DiffuseLight (int index, float3 normal, float3 worldPos, float shadowAttenuation) {
	float3 lightColor = _VisibleLightColors[index].rgb;
	float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
	float4 lightAttenuation = _VisibleLightAttenuations[index];
	float3 spotDirection = _VisibleLightSpotDirections[index].xyz;

	float3 lightVector = lightPositionOrDirection.xyz - worldPos.xyz*lightPositionOrDirection.w;//w = 1 in case of point light, w =0 in case of directional

	float3 lightDirection = normalize(lightVector);

	float diffuse = saturate(dot(normal, lightDirection));

	//Lightfalloff with distance calcs
	float lightVectorMagSqrd = dot(lightVector, lightVector);
	float rangeFade = lightVectorMagSqrd * lightAttenuation.x;
	rangeFade = saturate(1.0 - rangeFade * rangeFade);
	rangeFade *= rangeFade;

	//spotLight
	float spotFade = dot(spotDirection, lightDirection);
	spotFade = saturate(spotFade *lightAttenuation.z + lightAttenuation.w);
	spotFade *= spotFade;

	float distanceSqr = max(lightVectorMagSqrd, 0.00001);
	diffuse *= shadowAttenuation*spotFade*rangeFade/distanceSqr;

	return shadowAttenuation; //diffuse * lightColor;
}


struct VertexInput {
	float4 vertex : POSITION;
	float3 normal : NORMAL;

	UNITY_VERTEX_INPUT_INSTANCE_ID 
};

struct VertexOutput {
	float4 pos : SV_POSITION;
	float3 normal : TEXCOORD0;
	float3 worldPos : TEXCOORD1;

	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput LitPassVertex(VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);//used when gpu Instancing is enabled to get the proper Model Matrix
	UNITY_TRANSFER_INSTANCE_ID(input, output);//this is used so that the fragment can use instanced variables

	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);

	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.vertex.xyz, 1.0));
	output.worldPos = worldPos.xyz;
	output.pos = mul(unity_MatrixVP, worldPos);
	return output;
} 

float4 LitPassFragment(VertexOutput input) : SV_TARGET
{
	UNITY_SETUP_INSTANCE_ID(input);
	input.normal = normalize(input.normal);
	float3 albedo = UNITY_ACCESS_INSTANCED_PROP (PerInstance,_Color).rgb;


	float3 diffuseLight = 0;
	int i0 = 0;
	for (int j = 0; i0 < unity_LightData.y; j++)
	{
		for (int i = 0; (i<4 && i0<unity_LightData.y); i++) 
		{
			i0 = i + j * 4;
			//int bigI = floor(i / 4);
			//int lilI = fmod(i, 4);
			int lightIndex = unity_LightIndices[j][i];
			float shadowAttenuation = ShadowAttenuation(input.worldPos);
			diffuseLight += shadowAttenuation;//DiffuseLight(lightIndex, input.normal, input.worldPos.xyz, shadowAttenuation);
		}
	}

	float3 color = diffuseLight * albedo;

	return float4(color, 1.0);
}


#endif // MYRP_UNLIT_INCLUDED

