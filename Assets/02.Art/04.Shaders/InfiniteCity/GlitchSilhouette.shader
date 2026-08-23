// Occluded-silhouette glitch: drawn by the GlitchSilhouetteFeature as an
// override material with ZTest Greater, so it only appears where the car is
// BEHIND other geometry — the normally visible car keeps its real materials.
// Horizontal band tearing in the vertex stage (time-quantized so it stutters
// like a broken signal), two-color band slicing, scanlines and alpha flicker
// in the fragment stage. All knobs are material properties.
Shader "PoliceEscape/GlitchSilhouette"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.15, 0.9, 1.0, 0.85)
        _SecondaryColor ("Glitch Color", Color) = (1.0, 0.25, 0.6, 0.85)
        _GlitchStrength ("Glitch Strength", Range(0, 0.2)) = 0.05
        _GlitchSpeed ("Glitch Speed", Range(1, 60)) = 16
        _BandDensity ("Band Density (per meter)", Range(1, 60)) = 12
        _TearChance ("Tear Chance", Range(0, 1)) = 0.3
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.35
        _FlickerMinAlpha ("Flicker Min Alpha", Range(0, 1)) = 0.6
        // Greater = only where occluded (the whole point); exposed for debugging.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        // Pass 0 — stencil mark: tag every pixel where the car itself is the
        // visible surface (depth-equal re-render, no color). The silhouette
        // pass below skips those pixels, so the car never glitches against
        // its own parts (wheels behind the body) — only against real
        // occluders like buildings. Bit 64 keeps clear of URP's stencil bits.
        Pass
        {
            Name "GlitchStencilMark"
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex VertMask
            #pragma fragment FragMask
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AttributesMask
            {
                float4 positionOS : POSITION;
            };

            struct VaryingsMask
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsMask VertMask(AttributesMask input)
            {
                VaryingsMask output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 FragMask(VaryingsMask input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Pass 1 — the glitch silhouette, only where the stencil bit is clear.
        Pass
        {
            Name "GlitchSilhouette"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Back
            Stencil
            {
                Ref 64
                ReadMask 64
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SecondaryColor;
                float _GlitchStrength;
                float _GlitchSpeed;
                float _BandDensity;
                float _TearChance;
                float _ScanlineStrength;
                float _FlickerMinAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float band : TEXCOORD0;
            };

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Time quantized to steps so the tearing stutters instead of swimming.
                float tick = floor(_Time.y * _GlitchSpeed);
                float band = floor(positionWS.y * _BandDensity);

                output.positionCS = TransformWorldToHClip(positionWS);

                // Only some horizontal bands tear each tick, by a random amount.
                float tears = step(1.0 - _TearChance, Hash(float2(band + 31.0, tick)));
                float offset = (Hash(float2(band, tick)) - 0.5) * 2.0;
                output.positionCS.x += offset * tears * _GlitchStrength * output.positionCS.w;

                output.band = band;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float tick = floor(_Time.y * _GlitchSpeed);

                // Random bands flip to the secondary color — RGB-split feel on the cheap.
                half4 color = lerp(_BaseColor, _SecondaryColor, step(0.65, Hash(float2(input.band, tick + 17.0))));

                // Pixel-row scanlines off SV_POSITION.
                float scanline = 1.0 - _ScanlineStrength * step(0.5, frac(input.positionCS.y * 0.25));

                // Whole-silhouette flicker.
                float flicker = lerp(_FlickerMinAlpha, 1.0, Hash(float2(tick, 3.7)));

                return half4(color.rgb * scanline, color.a * flicker);
            }
            ENDHLSL
        }
    }
}
