// Edy's Vehicle Physics — tire marks decal.
// Ported from a Built-in RP surface shader (Standard, decal:blend) to URP:
// PBR-lit alpha-blended decal rendered right after opaque geometry, with the
// mark's opacity taken from the vertex color alpha (TireMarksRenderer fades
// segments by writing vertex alpha).

Shader "Custom/EVP Tire Marks"
{

Properties
	{
	_Color ("Color", Color) = (1,1,1,1)
	_MainTex ("Albedo (RGB)", 2D) = "white" {}
	_Glossiness ("Smoothness", Range(0,1)) = 0.5
	_Metallic ("Metallic", Range(0,1)) = 0.0
	}

SubShader
	{
	Tags { "RenderType"="Opaque" "Queue"="Geometry+1" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
	LOD 200

	Pass
		{
		Name "ForwardLit"
		Tags { "LightMode"="UniversalForward" }

		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex vert
		#pragma fragment frag

		#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
		#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
		#pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
		#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
		#pragma multi_compile_fragment _ _SHADOWS_SOFT
		#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
		#pragma multi_compile_fog

		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

		CBUFFER_START(UnityPerMaterial)
		float4 _MainTex_ST;
		half4 _Color;
		half _Glossiness;
		half _Metallic;
		CBUFFER_END

		TEXTURE2D(_MainTex);
		SAMPLER(sampler_MainTex);

		struct Attributes
			{
			float4 positionOS : POSITION;
			float3 normalOS : NORMAL;
			float2 uv : TEXCOORD0;
			half4 color : COLOR;
			};

		struct Varyings
			{
			float4 positionCS : SV_POSITION;
			float2 uv : TEXCOORD0;
			float3 positionWS : TEXCOORD1;
			float3 normalWS : TEXCOORD2;
			half fogFactor : TEXCOORD3;
			half4 color : COLOR;
			};

		Varyings vert (Attributes input)
			{
			Varyings output;

			VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
			output.positionCS = positionInputs.positionCS;
			output.positionWS = positionInputs.positionWS;
			output.normalWS = TransformObjectToWorldNormal(input.normalOS);
			output.uv = TRANSFORM_TEX(input.uv, _MainTex);
			output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
			output.color = input.color;

			return output;
			}

		half4 frag (Varyings input) : SV_Target
			{
			half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;

			SurfaceData surfaceData = (SurfaceData)0;
			surfaceData.albedo = c.rgb;
			surfaceData.metallic = _Metallic;
			surfaceData.smoothness = _Glossiness;
			surfaceData.occlusion = half(1.0);
			surfaceData.normalTS = half3(0.0, 0.0, 1.0);

			// The mark's opacity comes from the vertex color alpha

			surfaceData.alpha = c.a * input.color.a;

			InputData inputData = (InputData)0;
			inputData.positionWS = input.positionWS;
			inputData.positionCS = input.positionCS;
			inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
			inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
			#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
			inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
			#endif
			inputData.fogCoord = input.fogFactor;
			inputData.bakedGI = SampleSH(inputData.normalWS);
			inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
			inputData.shadowMask = half4(1, 1, 1, 1);

			half4 color = UniversalFragmentPBR(inputData, surfaceData);
			color.rgb = MixFog(color.rgb, input.fogFactor);
			return color;
			}
		ENDHLSL
		}
	}
}
