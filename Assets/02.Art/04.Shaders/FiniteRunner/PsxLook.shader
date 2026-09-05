// PSX look: the finished picture as a PlayStation-1 console would have put
// it out. Three faults of that hardware, all in ONE pass:
//   - PIXELATION: the frame is resolved at a virtual resolution
//     (_TargetHeight rows, square cells, nearest sampling). Everything below
//     is computed per virtual pixel ("cell") in SOURCE PIXELS of the target
//     (_ScreenParams), never in uv, so the grid is anchored at pixel (0,0)
//     and cannot drift.
//   - WOBBLE: the PS1 snapped every vertex to an integer screen grid and
//     mapped textures affinely, so polygon edges jittered and textures swam.
//     There are no vertices in a post pass, so this is a screen-space stand-in:
//     the picture is cut into coarse blocks (_WobbleBlock cells), keyed on a
//     half-octave DEPTH BAND from the depth buffer so block seams follow
//     geometry rather than the screen, and every cell of a (block, band)
//     samples the source with the SAME offset: a whole-pixel jitter re-rolled
//     at _JitterRate (the vertex snap) plus a sub-pixel drift that grows
//     through the step and resets at the next (the affine swim). A surface
//     therefore moves as a rigid piece: silhouettes dance, interiors hold —
//     the PS1 tell. A near-only depth guard (the fog's rule) keeps a far cell
//     from smearing a much nearer surface into itself; the sky is one quad
//     and never moves. Without a depth texture (_HasDepth 0) the wobble keys
//     on screen blocks alone.
//   - COLOUR: _ColorBits per channel (5 = the 15-bit framebuffer) quantised
//     in GAMMA space (the project renders linear; quantising linear crushes
//     the shadows) under a 4x4 Bayer dither indexed by the cell, one dot per
//     virtual pixel, locked to the grid.
// The clock is quantised (floor(_Time.y * _JitterRate)) like the far glitch,
// the speed lines and the VHS tape, so pausing freezes the jitter. The pass
// takes a copy of the camera colour (PsxLookFeature,
// AfterRenderingPostProcessing, inserted AFTER the GlitchPost feature and
// BEFORE the VhsTape: the console shows the glitch, the tape records the
// console). _Intensity scales the wobble, the swim and the dither and blends
// the result over the clean picture. At _Intensity 0 the pass is not
// enqueued; PsxLook (the scene component) writes every knob each frame and
// the feature writes _HasDepth.
Shader "Hidden/FiniteRunner/PsxLook"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 0
        _TargetHeight ("Virtual Resolution (rows)", Range(120, 480)) = 240
        _ColorBits ("Colour Bits per Channel", Range(3, 8)) = 5
        _Dither ("Dither", Range(0, 1)) = 0.7
        _Wobble ("Vertex Wobble (virtual px)", Range(0, 3)) = 1
        _WobbleBlock ("Wobble Block (virtual px)", Range(4, 64)) = 16
        _WobbleDepthFalloff ("Wobble Depth Falloff (1/m)", Range(0, 0.05)) = 0
        _Swim ("Texture Swim (virtual px)", Range(0, 2)) = 0.6
        _JitterRate ("Jitter Rate (steps/s)", Range(1, 60)) = 15
        _HasDepth ("Has Depth (feature-written)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "PsxLook"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl" // LinearToSRGB / SRGBToLinear

            #pragma vertex Vert
            #pragma fragment Frag

            float _Intensity;
            float _TargetHeight;
            float _ColorBits;
            float _Dither;
            float _Wobble;
            float _WobbleBlock;
            float _WobbleDepthFalloff;
            float _Swim;
            float _JitterRate;
            float _HasDepth;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float3 SampleNearest(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, saturate(uv)).rgb;
            }

            // Linear eye depth under a uv, and whether it is the sky (far plane).
            float EyeDepth(float2 uv, out float isSky)
            {
                float raw = SampleSceneDepth(saturate(uv));
                isSky = step(0.9999, Linear01Depth(raw, _ZBufferParams));
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            // 4x4 Bayer threshold for integer cell coords, no table:
            // M2(x, y) = (2x + 3y) mod 4;  M4 = 4 * M2(low bits) + M2(high bits).
            // Returns 1/32 .. 31/32, mean 0.5.
            float Bayer4(float2 p)
            {
                float2 q = fmod(abs(p), 4.0);
                float2 lo = fmod(q, 2.0);
                float2 hi = floor(q * 0.5);
                float mLo = fmod(2.0 * lo.x + 3.0 * lo.y, 4.0);
                float mHi = fmod(2.0 * hi.x + 3.0 * hi.y, 4.0);
                return (4.0 * mLo + mHi + 0.5) / 16.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float k = _Intensity;

                // ------------------------------------------------ the pixel grid
                float2 res = _ScreenParams.xy;
                float cell = max(1.0, floor(res.y / _TargetHeight + 0.5));   // integer source px per virtual px
                float2 cellId = floor(uv * res / cell);
                float2 cellCtr = (cellId + 0.5) * cell;                        // source px
                float2 uvCell = cellCtr / res;

                // ------------------------------------------------ the wobble
                float frame = floor(_Time.y * _JitterRate);
                float phase = frac(_Time.y * _JitterRate);

                float eye = 1.0, isSky = 0.0, dq = 0.0;
                if (_HasDepth > 0.5)
                {
                    eye = EyeDepth(uvCell, isSky);
                    dq = floor(log2(max(eye, 0.05)) * 2.0);                   // half-octave depth band
                }

                float2 blockId = floor((cellId + dq * 5.0) / max(_WobbleBlock, 1.0));
                float2 key = float2(blockId.x + dq * 37.0, blockId.y + frame * 0.73);

                // The vertex snap: whole virtual pixels, re-rolled every step.
                float falloff = exp(-eye * _WobbleDepthFalloff);
                float2 jit = (float2(Hash(key), Hash(key + 11.0)) - 0.5) * 2.0 * _Wobble * k * falloff;
                jit = round(jit);

                // The affine swim: a sub-pixel drift through the step, reset at the next.
                float2 dir = (float2(Hash(key + 23.0), Hash(key + 41.0)) - 0.5) * 2.0;
                float2 swim = dir * phase * _Swim * k;

                float2 off = (jit + swim) * (1.0 - isSky);                     // the sky is one quad
                float2 uvSrc = (cellCtr + off * cell) / res;

                // Near-only depth guard: a cell may erode a nearer silhouette by
                // a pixel (that is the jitter) but never pull a MUCH nearer
                // surface into a far block.
                if (_HasDepth > 0.5 && any(off != 0.0))
                {
                    float skyD;
                    float eyeD = EyeDepth(uvSrc, skyD);
                    if (eyeD < eye * 0.5) uvSrc = uvCell;
                }

                float3 clean = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float3 c = SampleNearest(uvSrc);

                // ------------------------------------------------ the colour
                float levels = exp2(floor(_ColorBits)) - 1.0;
                float d = (Bayer4(cellId) - 0.5) * _Dither * k;
                float3 g = LinearToSRGB(saturate(c));
                g = floor(g * levels + 0.5 + d) / levels;
                c = SRGBToLinear(saturate(g));

                return half4(lerp(clean, c, k), 1.0);
            }
            ENDHLSL
        }
    }
}
