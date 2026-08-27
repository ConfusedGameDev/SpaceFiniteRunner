// Edy's Vehicle Physics — alpha-blended particles that cast shadows (smoke, dust).
// Ported from Built-in RP to URP. The main pass is the classic 2x-tint alpha-blended
// particle with optional soft-particle depth fade (needs the URP Depth Texture; toggle
// per material). The ShadowCaster pass keeps the original's dithered semi-transparent
// shadows, but Built-in's _DitherMaskLOD 3D texture does not exist in URP, so the same
// effect is produced with a procedural 4x4 Bayer threshold: particle alpha scaled by
// _ShadowRange decides what fraction of the dither pattern's texels cast.

Shader "Custom/EVP Particles Alpha Blended Shadows"
{

Properties
	{
	_TintColor ("Tint Color", Color) = (0.5,0.5,0.5,0.5)
	_MainTex ("Particle Texture", 2D) = "white" {}
	[Toggle(_SOFTPARTICLES_ON)] _SoftParticlesEnabled ("Soft Particles", Float) = 0
	_InvFade ("Soft Particles Factor", Range(0.01,3.0)) = 1.0
	_ShadowRange ("Shadow Range", Range(0.0, 1.0)) = 0.49
	_ShadowBoost ("Shadow Boost", Range(0.0, 10.0)) = 1
	}

SubShader
	{
	Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "RenderPipeline"="UniversalPipeline" }

	HLSLINCLUDE
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

	CBUFFER_START(UnityPerMaterial)
	float4 _MainTex_ST;
	half4 _TintColor;
	half _SoftParticlesEnabled;
	half _InvFade;
	half _ShadowRange;
	half _ShadowBoost;
	CBUFFER_END

	TEXTURE2D(_MainTex);
	SAMPLER(sampler_MainTex);
	ENDHLSL

	Pass
		{
		Name "ForwardUnlit"
		Tags { "LightMode"="UniversalForward" }

		Blend SrcAlpha OneMinusSrcAlpha
		ColorMask RGB
		Cull Off
		ZWrite Off

		HLSLPROGRAM
		#pragma vertex vert
		#pragma fragment frag
		#pragma shader_feature_local_fragment _SOFTPARTICLES_ON
		#pragma multi_compile_fog

		#if defined(_SOFTPARTICLES_ON)
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
		#endif

		struct Attributes
			{
			float4 positionOS : POSITION;
			half4 color : COLOR;
			float2 uv : TEXCOORD0;
			};

		struct Varyings
			{
			float4 positionCS : SV_POSITION;
			half4 color : COLOR;
			float2 uv : TEXCOORD0;
			half fogFactor : TEXCOORD1;
			#if defined(_SOFTPARTICLES_ON)
			float4 projPos : TEXCOORD2;		// xy/w = screen uv, z = view depth
			#endif
			};

		Varyings vert (Attributes input)
			{
			Varyings output;

			VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
			output.positionCS = positionInputs.positionCS;

			#if defined(_SOFTPARTICLES_ON)
			output.projPos = positionInputs.positionNDC;
			output.projPos.z = -positionInputs.positionVS.z;
			#endif

			output.color = input.color;
			output.uv = TRANSFORM_TEX(input.uv, _MainTex);
			output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

			return output;
			}

		half4 frag (Varyings input) : SV_Target
			{
			#if defined(_SOFTPARTICLES_ON)
			float2 screenUV = input.projPos.xy / input.projPos.w;
			float sceneZ = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
			float partZ = input.projPos.z;
			half fade = saturate(_InvFade * (sceneZ - partZ));
			input.color.a *= fade;
			#endif

			half4 col = half(2.0) * input.color * _TintColor * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
			col.rgb = MixFog(col.rgb, input.fogFactor);
			return col;
			}
		ENDHLSL
		}

	//  Shadow rendering pass
	Pass
		{
		Name "ShadowCaster"
		Tags { "LightMode"="ShadowCaster" }

		ZWrite On
		ZTest LEqual
		ColorMask 0
		Cull Off

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex vertShadowCaster
		#pragma fragment fragShadowCaster
		#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

		float3 _LightDirection;
		float3 _LightPosition;

		struct Attributes
			{
			float4 positionOS : POSITION;
			float3 normalOS : NORMAL;
			half4 color : COLOR;
			float2 uv : TEXCOORD0;
			};

		struct Varyings
			{
			float4 positionCS : SV_POSITION;
			float2 uv : TEXCOORD0;
			half4 color : COLOR;
			};

		Varyings vertShadowCaster (Attributes input)
			{
			Varyings output;

			float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
			float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

			#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
			float3 lightDirectionWS = normalize(_LightPosition - positionWS);
			#else
			float3 lightDirectionWS = _LightDirection;
			#endif

			float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

			#if UNITY_REVERSED_Z
			positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
			#else
			positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
			#endif

			output.positionCS = positionCS;
			output.uv = TRANSFORM_TEX(input.uv, _MainTex);
			output.color = input.color;

			return output;
			}

		half4 fragShadowCaster (Varyings input) : SV_Target
			{
			half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a
				* _TintColor.a * input.color.a * _ShadowBoost;

			// 4x4 Bayer matrix as an ordered-dither threshold: alpha (scaled by
			// _ShadowRange) is the fraction of pixels that cast, matching the old
			// _DitherMaskLOD 4x4x16 lookup.

			static const half dither[16] =
				{
				 0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
				12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
				 3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
				15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
				};

			uint2 pixel = uint2(input.positionCS.xy) & 3;
			half threshold = dither[pixel.y * 4 + pixel.x];

			clip(saturate(alpha * half(0.9375)) * _ShadowRange - threshold - half(0.001));

			return 0;
			}
		ENDHLSL
		}
	}
}
