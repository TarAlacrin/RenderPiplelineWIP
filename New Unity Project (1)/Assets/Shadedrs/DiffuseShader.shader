Shader "DitheredPipeline/Diffuse"
{
    Properties
    {
		_Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM

			#pragma target 3.5
			
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling
			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _SHADOWS_HARD
			#pragma multi_compile _ _VERTEX_SECONDARY_LIGHTS

			#pragma vertex LitPassVertex
			#pragma fragment LitPassFragment
			#include "../ShaderLibrary/Lit.hlsl"


			ENDHLSL
        }

		Pass
		{
			Tags{
				"LightMode" = "ShadowCaster"
			}

			HLSLPROGRAM

			#pragma target 3.5
			
			#pragma multi_compile_instancing
			#pragma instancing_options assumeuniformscaling
			
			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment
			
			#include "../ShaderLibrary/ShadowCaster.hlsl"

			ENDHLSL
		}
    }
}
