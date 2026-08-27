// Vertex-color character shader for URP: base look is vertex colors × tint
// (low-poly kit characters carry their palette in vertex colors, so it works
// with no textures at all), simply lit by the main light + ambient SH.
// Optional blocks, each behind a material toggle:
//   ALBEDO   — a texture multiplied over the vertex colors.
//   NOISE    — a scrolling noise texture that modulates emission and the
//              hologram's transparency (static-flicker).
//   EMISSION — the color picker is a MASK, not a wash: only the parts of the
//              character whose VERTEX COLOR matches the picked color emit
//              (pick red → the red parts glow red), × strength, feeding
//              bloom. Tolerance sets how close a match has to be; the HDR
//              intensity is ignored for matching, so boosting the glow never
//              changes which parts light up.
//   HOLOGRAM — half-transparent (Hologram Alpha) with CRT scanlines rolling
//              vertically; the SIGN of Scanline Speed picks up vs down.
// The hologram toggle needs a blend/queue swap a shader can't do alone, so
// the CustomEditor below (CharacterColorShaderGUI) flips _SrcBlend/_DstBlend/
// _ZWrite, the render queue and shadow casting whenever it changes — always
// edit these materials through the inspector, not by raw keyword.
Shader "FiniteRunner/SH_CharacterColor"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)

        [Toggle(_ALBEDO_ON)] _UseAlbedo ("Use Albedo Texture", Float) = 0
        _BaseMap ("Albedo (optional)", 2D) = "white" {}

        [Toggle(_NOISE_ON)] _UseNoise ("Use Noise Texture", Float) = 0
        _NoiseMap ("Noise (optional)", 2D) = "gray" {}
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.5
        _NoiseScroll ("Noise Scroll (xy = uv/sec)", Vector) = (0.1, 0.07, 0, 0)

        [Toggle(_EMISSION_ON)] _UseEmission ("Emission Enabled", Float) = 0
        [HDR] _EmissionColor ("Emission Color (masks by vertex color)", Color) = (1, 1, 1, 1)
        _EmissionStrength ("Emission Multiplier", Range(0, 10)) = 1
        _EmissionTolerance ("Emission Mask Tolerance", Range(0.01, 1)) = 0.25

        [Toggle(_HOLOGRAM_ON)] _UseHologram ("Hologram Mode", Float) = 0
        _HoloAlpha ("Hologram Alpha", Range(0, 1)) = 0.5
        _ScanlineCount ("Scanline Count", Range(10, 400)) = 120
        _ScanlineSpeed ("Scanline Speed (+up / -down)", Range(-5, 5)) = 1
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.6

        // Managed by CharacterColorShaderGUI from the hologram toggle.
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // One CBUFFER shared by every pass — identical layouts keep the SRP Batcher happy.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _NoiseMap_ST;
            half _NoiseStrength;
            float4 _NoiseScroll;
            half4 _EmissionColor;
            half _EmissionStrength;
            half _EmissionTolerance;
            half _HoloAlpha;
            float _ScanlineCount;
            float _ScanlineSpeed;
            half _ScanlineStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALBEDO_ON
            #pragma shader_feature_local_fragment _NOISE_ON
            #pragma shader_feature_local_fragment _EMISSION_ON
            #pragma shader_feature_local_fragment _HOLOGRAM_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR; // meshes without vertex colors read as white on supported platforms
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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tint = input.color * _BaseColor;
                half3 albedo = tint.rgb;
                #if _ALBEDO_ON
                    albedo *= SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                #endif

                // Noise resolves to 1 when off, so every lerp below collapses to "no effect".
                half noise = 1.0;
                #if _NOISE_ON
                    float2 noiseUv = input.uv * _NoiseMap_ST.xy + _NoiseMap_ST.zw + _Time.y * _NoiseScroll.xy;
                    noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseUv).r;
                #endif

                float3 normalWS = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 lighting = SampleSH(normalWS);
                lighting += mainLight.color * mainLight.shadowAttenuation
                            * saturate(dot(normalWS, mainLight.direction));
                half3 color = albedo * lighting;

                #if _EMISSION_ON
                    // The picked color is a mask against the VERTEX color:
                    // only matching parts emit. Matching uses the picker's
                    // chromaticity (max-normalized), so the HDR intensity
                    // scales the glow without moving the mask.
                    half brightest = max(_EmissionColor.r, max(_EmissionColor.g, _EmissionColor.b));
                    half3 matchTarget = _EmissionColor.rgb / max(brightest, 0.0001);
                    half matchDistance = distance(input.color.rgb, matchTarget);
                    half emissionMask = 1.0 - smoothstep(_EmissionTolerance * 0.5, _EmissionTolerance, matchDistance);
                    color += _EmissionColor.rgb * _EmissionStrength * emissionMask * lerp(1.0, noise, _NoiseStrength);
                #endif

                half alpha = tint.a;
                #if _HOLOGRAM_ON
                    // Screen-space lines — a CRT rolls over the image, not the mesh.
                    float screenY = input.positionCS.y / _ScreenParams.y;
                    half scan = 0.5 + 0.5 * sin((screenY * _ScanlineCount - _Time.y * _ScanlineSpeed) * TWO_PI);
                    alpha *= _HoloAlpha * lerp(1.0, scan, _ScanlineStrength) * lerp(1.0, noise, _NoiseStrength);
                    // Half the strength in the color: the lines read as brightness
                    // banding too, not only as holes in the transparency.
                    color *= lerp(1.0, scan, _ScanlineStrength * 0.5);
                #endif

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                Varyings output;
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    CustomEditor "ConfusedGameDev.FiniteRunner.EditorTools.CharacterColorShaderGUI"
}
