﻿#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED


#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
 


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

CBUFFER_START(UnityPerCamera)
float3 _WorldSpaceCameraPos;
CBUFFER_END


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
	float4x4 _WorldToShadowMatrices[MAX_VISIBLE_LIGHTS];
	float4 _ShadowData[MAX_VISIBLE_LIGHTS];
	float4 _ShadowMapSize;
	float4 _GlobalShadowData;
CBUFFER_END

TEXTURE2D_SHADOW(_ShadowMap);
SAMPLER_CMP(sampler_ShadowMap);


float HardShadowAttenuation(float4 shadowPos)
{
	//return shadowPos.z;//abs(frac(shadowPos.z)) + 1000;
	return SAMPLE_TEXTURE2D_SHADOW(
		_ShadowMap, sampler_ShadowMap, shadowPos.xyz); //round(shadowPos.z - SAMPLE_TEXTURE2D(_ShadowMap, sampler_ShadowMap, shadowPos.xy).r);
}

float SoftShadowAttenuation(float4 shadowPos)
{
	real tentWeights[9];
	real2 tentUVs[9];
	SampleShadow_ComputeSamples_Tent_5x5(_ShadowMapSize, shadowPos.xy, tentWeights, tentUVs);
	float attenuation = 0;
	for (int i = 0; i < 9; i++)
	{
		attenuation += tentWeights[i] * SAMPLE_TEXTURE2D_SHADOW(
			_ShadowMap, sampler_ShadowMap, float3(tentUVs[i].xy, shadowPos.z)
		);
	}
	return attenuation;
}

float DistanceToCameraSqr(float3 worldPos) {
	float3 cameraToFragment = worldPos - _WorldSpaceCameraPos;
	return dot(cameraToFragment, cameraToFragment);
}

float ShadowAttenuation(int index, float3 worldPos)
{
#if !defined(_SHADOWS_HARD) && !defined(_SHADOWS_SOFT)
	return 1.0;
#endif
	if (_ShadowData[index].x <= 0 || DistanceToCameraSqr(worldPos) > _GlobalShadowData.y)
		return 1.0;

	float4 shadowPos = mul(_WorldToShadowMatrices[index], float4(worldPos, 1.0));
	shadowPos.xyz /= shadowPos.w;
	shadowPos.xy = saturate(shadowPos.xy);
	shadowPos.xy = shadowPos.xy *_GlobalShadowData.x + _ShadowData[index].zw;//this is where tiling is calculated
	float attenuation;
	
#ifdef _SHADOWS_HARD
	#ifdef _SHADOWS_SOFT
		if (_ShadowData[index].y <= 0)//if hard shadows else if soft shadows
			attenuation = HardShadowAttenuation(shadowPos);
		else
			attenuation = SoftShadowAttenuation(shadowPos);
	#else
		attenuation = HardShadowAttenuation(shadowPos);
	#endif
#else
	attenuation = SoftShadowAttenuation(shadowPos);
#endif

	return max(lerp(1, attenuation, _ShadowData[index].x),0);
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
	diffuse *= shadowAttenuation* spotFade*rangeFade/distanceSqr;
	return  diffuse * lightColor;
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
	float3 vertexLighting : TEXCOORD2;

	UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput LitPassVertex(VertexInput input) {
	VertexOutput output;
	UNITY_SETUP_INSTANCE_ID(input);//used when gpu Instancing is enabled to get the proper Model Matrix
	UNITY_TRANSFER_INSTANCE_ID(input, output);//this is used so that the fragment can use instanced variables


	float4 worldPos = mul(UNITY_MATRIX_M, float4(input.vertex.xyz, 1.0));
	output.worldPos = worldPos.xyz;
	output.pos = mul(unity_MatrixVP, worldPos);
	output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);

	output.vertexLighting = 0;

#ifdef _VERTEX_SECONDARY_LIGHTS
	for (int i = 4; i < min(unity_LightData.y, 8); i++) {
		int lightIndex = unity_LightIndices[1][i - 4];
		output.vertexLighting +=
			max(DiffuseLight(lightIndex, output.normal, output.worldPos,1),0);
	}
#endif
	return output;
} 

float4 LitPassFragment(VertexOutput input) : SV_TARGET
{
	UNITY_SETUP_INSTANCE_ID(input);
	input.normal = normalize(input.normal);
	float3 albedo = UNITY_ACCESS_INSTANCED_PROP (PerInstance,_Color).rgb;


	float3 diffuseLight = input.vertexLighting;

#ifndef _VERTEX_SECONDARY_LIGHTS
	int i0 = 0;
	int i1 = 0;
	for (int j = 0; i1 < unity_LightData.y; j++)
	{
		for (int i = 0; (i<4 && i1 <unity_LightData.y); i++) 
		{
			i0 = i + j * 4;
			i1 = i0 + 1;
#else
	int j = 0;
		for (int i = 0; i < min(unity_LightData.y, 4); i++)
		{
			int i0 = i;
#endif
			int lightIndex = unity_LightIndices[j][i];
			float shadowAttenuation = ShadowAttenuation(lightIndex, input.worldPos);
			diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos.xyz, shadowAttenuation);
		}
#ifndef _VERTEX_SECONDARY_LIGHTS
	}
#endif

float3 color = diffuseLight*albedo;

	return float4(color, 1.0);
}


#endif // MYRP_UNLIT_INCLUDED


