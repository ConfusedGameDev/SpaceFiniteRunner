// Manga "speed lines" (集中線): hard-edged wedges from the screen edges
// pointing at _Focus (the ship on screen), tips inward, the middle clear.
// Procedural — N angular cells per layer (a coarse and a fine one), and a
// hash per cell per flicker frame decides presence, position inside the
// cell, inner tip radius and width, so the pattern jumps at _FlickerRate
// like the fog's far glitch (floor(_Time.y * rate); _Time is Time.time in
// play, so pause freezes it). The wedge is a triangle: zero width at its
// tip, widening over _TaperLength, then constant to the edge of the screen.
// Edge anti-aliasing is analytic (one pixel's angular size at that radius),
// so there is no fwidth seam where the angle wraps. Blends over the
// post-processed picture (SpeedLinesFeature, AfterRenderingPostProcessing,
// before the GlitchPost feature so the death glitch corrupts the lines too)
// and never samples _BlitTexture: the feature draws it with the source-less
// Blitter.BlitTexture overload. At _Intensity 0 the pass is not enqueued;
// SpeedLines (the scene component) writes every property each frame.
Shader "Hidden/FiniteRunner/SpeedLines"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Color ("Line Color", Color) = (1, 1, 1, 1)
        _Focus ("Focus (viewport xy)", Vector) = (0.5, 0.5, 0, 0)
        _LineCount ("Coarse Line Count", Range(8, 200)) = 48
        _FineLineCount ("Fine Line Count", Range(16, 400)) = 140
        _Density ("Density", Range(0, 1)) = 0.55
        _LineWidth ("Line Width (of a cell)", Range(0, 1)) = 0.45
        _TaperLength ("Taper Length (screen heights)", Range(0.05, 1)) = 0.35
        _InnerRadius ("Inner Clear Radius fast/slow (screen heights)", Vector) = (0.18, 0.42, 0, 0)
        _InnerJitter ("Inner Radius Jitter", Range(0, 1)) = 0.3
        _FlickerRate ("Flicker Rate (steps/s)", Range(1, 60)) = 12
        _EdgeSoftness ("Edge Softness (px)", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "SpeedLines"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            float _Intensity;
            half4 _Color;
            float4 _Focus;
            float _LineCount;
            float _FineLineCount;
            float _Density;
            float _LineWidth;
            float _TaperLength;
            float4 _InnerRadius;   // x = clear radius at full speed (min), y = at start speed (max)
            float _InnerJitter;
            float _FlickerRate;
            float _EdgeSoftness;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // One layer of wedges.
            //   angle01  polar angle around the focus, in turns (0..1)
            //   r        distance from the focus, in screen heights
            //   pxTurns  one pixel's angular size at this radius, in turns (analytic AA)
            //   count    angular cells in this layer
            //   frame    quantised time
            //   seed     layer seed, so coarse and fine never share a hash
            //   inner    inner clear radius before the per-line jitter
            //   widthMul width scale (intensity, layer)
            //   presence probability a cell holds a line this frame
            float Layer(float angle01, float r, float pxTurns, float count, float frame, float seed,
                        float inner, float widthMul, float presence)
            {
                float a = angle01 * count;
                float cell = floor(a);
                float u = a - cell - 0.5;                                     // -0.5..0.5 across the cell
                float2 key = float2(cell + seed, frame);
                float present = step(1.0 - presence, Hash(key));
                float h1 = Hash(key + 17.0);
                float h2 = Hash(key + 41.0);
                float h3 = Hash(key + 73.0);

                float halfW = 0.5 * _LineWidth * widthMul * (0.5 + 0.5 * h3); // cell units, full width past the taper
                float centre = (h1 - 0.5) * (1.0 - 2.0 * halfW);               // anywhere in the cell without crossing it
                float tip = inner * (1.0 + _InnerJitter * (h2 - 0.5) * 2.0);   // per-line inner tip
                float taper = saturate((r - tip) / _TaperLength);              // 0 at the tip -> 1, then constant to the edge
                float d = abs(u - centre) - halfW * taper;                     // signed distance to the wedge edge, cell units
                float aa = max(pxTurns * count * _EdgeSoftness, 1e-4);
                return present * (1.0 - smoothstep(-aa, aa, d)) * step(tip, r);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float inner = lerp(_InnerRadius.y, _InnerRadius.x, _Intensity);   // the clear radius shrinks with speed
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 p = (uv - _Focus.xy) * float2(aspect, 1.0);                // screen-height units, focus at the origin
                float r = length(p);
                if (r < inner * (1.0 - _InnerJitter))
                    return half4(0, 0, 0, 0);                                      // the middle is always clear

                float angle01 = atan2(p.y, p.x) * (1.0 / TWO_PI) + 0.5;
                float pxTurns = 1.0 / (TWO_PI * max(r, 1e-3) * _ScreenParams.y);
                float frame = floor(_Time.y * _FlickerRate);

                float presence = _Density * _Intensity;
                float widthMul = lerp(0.5, 1.0, _Intensity);
                float coarse = Layer(angle01, r, pxTurns, _LineCount,     frame, 0.0,   inner, widthMul,       presence);
                float fine   = Layer(angle01, r, pxTurns, _FineLineCount, frame, 101.0, inner, widthMul * 0.5, presence * 0.7);
                float mask = max(coarse, fine);

                // Hard lines; only the very first ones fade in, so they never pop from nothing.
                return half4(_Color.rgb, mask * _Color.a * saturate(_Intensity * 3.0));
            }
            ENDHLSL
        }
    }
}
