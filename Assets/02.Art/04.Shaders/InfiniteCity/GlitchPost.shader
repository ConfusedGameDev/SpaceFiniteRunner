// Fullscreen digital-glitch post effect for the police-escape city:
// horizontal slice displacement, RGB channel split, block corruption,
// acid-color mangling and scanlines — all scaled by a single _Intensity
// so the GlitchController can drive it from game events. At intensity 0
// the pass returns the source untouched, so the shared renderer feature
// is safe to leave on for every scene.
// Used by a Full Screen Pass Renderer Feature (samples _BlitTexture).
Shader "Hidden/PoliceEscape/GlitchPost"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 0
        _SliceStrength ("Slice Displacement", Range(0, 0.4)) = 0.12
        _ColorSplit ("RGB Split", Range(0, 0.05)) = 0.012
        _BlockAmount ("Block Corruption", Range(0, 1)) = 0.5
        _ColorMangle ("Color Mangle", Range(0, 1)) = 0.7
        _ScanlineStrength ("Scanlines", Range(0, 1)) = 0.35
        _GlitchRate ("Glitch Rate (steps/s)", Range(1, 60)) = 14
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "GlitchPost"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            float _Intensity;
            float _SliceStrength;
            float _ColorSplit;
            float _BlockAmount;
            float _ColorMangle;
            float _ScanlineStrength;
            float _GlitchRate;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half3 SampleSource(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float gi = _Intensity;
                if (gi <= 0.001)
                    return half4(SampleSource(uv), 1); // clean feed — effectively a passthrough blit

                // Time quantized into discrete "corrupt frames": glitches jump
                // rather than slide, which is what sells the digital look.
                float t = floor(_Time.y * _GlitchRate);

                // Coarse slice displacement: only some rows tear, and how many
                // grows with intensity.
                float rowA = floor(uv.y * 22.0);
                float randA = Hash(float2(rowA, t));
                float gateA = step(1.0 - 0.4 * gi, randA);
                uv.x += gateA * (Hash(float2(rowA, t + 7.0)) - 0.5) * 2.0 * _SliceStrength * gi;

                // Fine slices layered on top for the thin jitter lines.
                float rowB = floor(uv.y * 96.0);
                float randB = Hash(float2(rowB, t + 3.0));
                float gateB = step(1.0 - 0.3 * gi, randB);
                uv.x += gateB * (randB - 0.5) * _SliceStrength * 0.7 * gi;

                // Block corruption: rectangular cells occasionally re-source
                // their pixels from a shifted spot, like copied macroblocks.
                float2 blockId = floor(uv * float2(18.0, 10.0));
                float randBlock = Hash(blockId + t * 0.731);
                if (randBlock > 1.0 - 0.25 * _BlockAmount * gi)
                {
                    float2 shift = float2(Hash(blockId + t + 11.0), Hash(blockId + t + 23.0)) - 0.5;
                    uv = frac(uv + shift * 0.35 * gi);
                }

                // RGB split, wobbling per corrupt frame.
                float split = _ColorSplit * gi * (0.4 + 0.6 * Hash(float2(t, 1.0)));
                half3 color;
                color.r = SampleSource(uv + float2(split, 0.0)).r;
                color.g = SampleSource(uv).g;
                color.b = SampleSource(uv - float2(split, 0.0)).b;

                // Acid-color mangle on torn rows: channel-rotated, oversaturated
                // — the green/magenta smear from the reference frame.
                float mangle = max(gateA, gateB) * step(0.45, Hash(float2(rowA + rowB, t + 31.0)));
                half3 mangled = saturate(half3(color.g, color.b, color.r) * 1.8 - 0.15);
                color = lerp(color, mangled, mangle * _ColorMangle * gi);

                // Scanlines + a faint rolling brightness band.
                float scan = 0.5 + 0.5 * sin(input.texcoord.y * 780.0 + _Time.y * 6.0);
                color *= 1.0 - _ScanlineStrength * gi * 0.35 * scan;

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
