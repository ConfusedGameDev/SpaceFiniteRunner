// Edy's Vehicle Physics — tire marks debug view.
// Ported from a Built-in RP surface shader to URP. Visualizes the tire mark
// strip's vertex color alpha (the mark's fade value) as grayscale — unlit on
// purpose: it is a data view, not a material.

Shader "Custom/EVP Tire Marks Debug"
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
	Tags { "RenderType"="Opaque" "Queue"="Geometry+1" "RenderPipeline"="UniversalPipeline" }
	LOD 200

	Pass
		{
		Name "Unlit"
		Tags { "LightMode"="UniversalForward" }

		HLSLPROGRAM
		#pragma vertex vert
		#pragma fragment frag

		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		CBUFFER_START(UnityPerMaterial)
		float4 _MainTex_ST;
		half4 _Color;
		half _Glossiness;
		half _Metallic;
		CBUFFER_END

		struct Attributes
			{
			float4 positionOS : POSITION;
			half4 color : COLOR;
			};

		struct Varyings
			{
			float4 positionCS : SV_POSITION;
			half4 color : COLOR;
			};

		Varyings vert (Attributes input)
			{
			Varyings output;
			output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
			output.color = input.color;
			return output;
			}

		half4 frag (Varyings input) : SV_Target
			{
			return half4(input.color.aaa, 1.0);
			}
		ENDHLSL
		}
	}
}
