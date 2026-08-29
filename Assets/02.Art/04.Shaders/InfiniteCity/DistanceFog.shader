// Distance fog + "far glitch" for the police-escape city, as ONE depth-based
// full-screen pass (run by DistanceFogFeature at BeforeRenderingPostProcessing,
// samples _BlitTexture + _CameraDepthTexture). Both effects key on the same
// radial distance from the camera: the fog blends a near→far colour ramp in
// over _FogStart.._FogEnd, and from _GlitchStart the image tears, drops
// macroblocks to the far fog colour and splits its channels — the far city
// dissolves into signal noise before it vanishes into the haze, which is what
// hides the block streaming (CityStreamer) and makes the fog read cyberpunk
// rather than misty. Displaced samples are depth-guarded so a near surface
// (the car, the building beside it) is never pulled into the smear. Runs
// BEFORE post-processing on purpose: bloom and tonemapping then treat the fog
// as scene light (keep HDR fog colours below 1 unless the glow is wanted). At
// _Intensity 0 the feature does not even enqueue the pass; DistanceFog (the
// scene component) writes every property from its settings asset each frame.
Shader "Hidden/PoliceEscape/DistanceFog"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 0
        _FogStart ("Fog Start (m)", Float) = 120
        _FogEnd ("Fog End (m)", Float) = 480
        _FogDensity ("Fog Density", Range(0.5, 6)) = 2.5
        [HDR] _FogColorNear ("Fog Color Near", Color) = (0.07, 0.05, 0.14, 1)
        [HDR] _FogColorFar ("Fog Color Far", Color) = (0.30, 0.09, 0.42, 1)
        _SkyFogAmount ("Sky Fog", Range(0, 1)) = 0.85
        _HeightFalloff ("Height Falloff (1/m)", Float) = 0
        _HeightBase ("Height Base (m)", Float) = 0
        _GlitchStart ("Glitch Start (m)", Float) = 300
        _GlitchStrength ("Glitch Strength", Range(0, 1)) = 0.6
        _GlitchRate ("Glitch Rate (steps/s)", Range(1, 60)) = 12
        _SliceStrength ("Slice Displacement", Range(0, 0.4)) = 0.08
        _BlockAmount ("Block Dropout", Range(0, 1)) = 0.5
        _ColorSplit ("RGB Split", Range(0, 0.05)) = 0.008
        _ScanlineStrength ("Scanlines", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "DistanceFog"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            float _Intensity;
            float _FogStart;
            float _FogEnd;
            float _FogDensity;
            half4 _FogColorNear;
            half4 _FogColorFar;
            float _SkyFogAmount;
            float _HeightFalloff;
            float _HeightBase;
            float _GlitchStart;
            float _GlitchStrength;
            float _GlitchRate;
            float _SliceStrength;
            float _BlockAmount;
            float _ColorSplit;
            float _ScanlineStrength;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half3 SampleSource(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            // Radial distance from the camera to the surface under a pixel,
            // its world height, and whether the pixel is sky (far plane).
            float SceneDistance(float2 uv, out float height, out float isSky)
            {
                float raw = SampleSceneDepth(saturate(uv));
                isSky = step(0.9999, Linear01Depth(raw, _ZBufferParams));
            #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
            #endif
                float3 positionWS = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                height = positionWS.y;
                return distance(positionWS, _WorldSpaceCameraPos);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                if (_Intensity <= 0.001)
                    return half4(SampleSource(uv), 1); // clean feed

                float height, isSky;
                float dist = SceneDistance(uv, height, isSky);

                // ---- fog: exp² over the start..end band, optional height falloff,
                // the sky gets its own fixed share so a horizon still exists.
                float x = saturate((dist - _FogStart) / max(1.0, _FogEnd - _FogStart));
                float d = _FogDensity * x;
                float fog = 1.0 - exp(-d * d);
                fog *= exp(-_HeightFalloff * max(0.0, height - _HeightBase));
                fog = lerp(fog, _SkyFogAmount, isSky) * _Intensity;
                half3 fogColor = lerp(_FogColorNear.rgb, _FogColorFar.rgb, lerp(x, 1.0, isSky));

                // ---- far glitch: ramps from _GlitchStart to full at the fog end.
                float g = saturate((dist - _GlitchStart) / max(1.0, _FogEnd - _GlitchStart)) * _GlitchStrength * _Intensity;
                g *= lerp(1.0, _SkyFogAmount, isSky); // the sky only glitches as much as it fogs

                // Time quantized into discrete corrupt frames: glitches jump
                // rather than slide (same trick as GlitchPost).
                float t = floor(_Time.y * _GlitchRate);

                // Coarse + fine row tears; how many rows tear grows with g.
                float2 uvG = uv;
                float rowA = floor(uv.y * 22.0);
                float gateA = step(1.0 - 0.4 * g, Hash(float2(rowA, t)));
                uvG.x += gateA * (Hash(float2(rowA, t + 7.0)) - 0.5) * 2.0 * _SliceStrength * g;
                float rowB = floor(uv.y * 96.0);
                float randB = Hash(float2(rowB, t + 3.0));
                float gateB = step(1.0 - 0.3 * g, randB);
                uvG.x += gateB * (randB - 0.5) * _SliceStrength * 0.7 * g;

                // Depth guard: a torn row must never smear a NEAR surface across
                // the screen — if the displaced sample is closer than the glitch
                // start, keep the pixel's own column.
                if (any(uvG != uv))
                {
                    float heightG, skyG;
                    float distG = SceneDistance(uvG, heightG, skyG);
                    if (distG < _GlitchStart) uvG = uv;
                }

                // RGB split, wobbling per corrupt frame.
                float split = _ColorSplit * g * (0.4 + 0.6 * Hash(float2(t, 1.0)));
                half3 color;
                color.r = SampleSource(uvG + float2(split, 0.0)).r;
                color.g = SampleSource(uvG).g;
                color.b = SampleSource(uvG - float2(split, 0.0)).b;

                // Scanline flicker on the glitching band only.
                float scan = 0.5 + 0.5 * sin(uv.y * 780.0 + _Time.y * 6.0);
                color *= 1.0 - _ScanlineStrength * g * 0.35 * scan;

                // Fog in, then macroblock dropouts: cells that "have not
                // streamed in yet" snap to the far fog colour.
                color = lerp(color, fogColor, fog);
                float2 blockId = floor(uv * float2(18.0, 10.0));
                float dropout = step(1.0 - 0.25 * _BlockAmount * g, Hash(blockId + t * 0.731));
                color = lerp(color, _FogColorFar.rgb, dropout * saturate(g * 2.0));

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
