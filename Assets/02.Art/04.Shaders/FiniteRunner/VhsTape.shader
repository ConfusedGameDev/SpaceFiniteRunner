// VHS tape: the post-processed picture as if it had been recorded on a worn
// cassette and played back on a consumer deck. The look is built the way the
// format actually degrades a signal, so each knob maps to one real fault:
//   - the colour (chroma) is recorded at a fraction of the luma bandwidth, so
//     it SMEARS sideways (_ChromaBleed) and TRAILS the luma by a few pixels
//     (_ChromaLag) — the colour fringes on every hard edge;
//   - the luma itself is a little soft vertically (_LumaSoftness);
//   - every scanline is read off the tape with its own timing error, so rows
//     JITTER sideways and the whole frame wobbles slowly (_Jitter);
//   - a TRACKING band — a strip of torn, noisy, colourless rows — crawls down
//     the picture (_Tracking, _TrackingSpeed, _TrackingHeight); a burst from
//     gameplay makes it big and bright;
//   - the head switch at the bottom of the frame skews the last rows and
//     fills them with noise (_HeadSwitch, _HeadSwitchHeight);
//   - tape NOISE: fine grain, sparse white dropout dashes, brightness flicker
//     (_Noise);
//   - the CRT's SCANLINES darken alternate rows (_Scanlines, _ScanlineCount);
//   - the whole thing is WASHED out — lifted blacks, tired colour (_Wash) —
//     and darker at the corners (_Vignette).
// Everything random is keyed on a quantised clock (floor(_Time.y *
// _FrameRate)) like the far glitch and the speed lines, so the noise steps
// at tape frame rate rather than shimmering per frame, and pausing (timeScale
// 0 freezes _Time) freezes the tape. The pass takes a copy of the camera
// colour (VhsTapeFeature, AfterRenderingPostProcessing, appended AFTER the
// GlitchPost feature: the tape is the recording medium, so the death glitch
// is on the tape too) and _Intensity both scales every fault and blends the
// result over the clean picture. At _Intensity 0 the pass is not enqueued;
// VhsTape (the scene component) writes every property each frame.
Shader "Hidden/FiniteRunner/VhsTape"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 0
        _ChromaBleed ("Chroma Bleed (px)", Range(0, 40)) = 14
        _ChromaLag ("Chroma Lag (px)", Range(0, 12)) = 3
        _LumaSoftness ("Luma Softness (px)", Range(0, 4)) = 0.8
        _Jitter ("Line Jitter (px)", Range(0, 12)) = 2
        _Tracking ("Tracking Band Strength", Range(0, 1)) = 0.35
        _TrackingSpeed ("Tracking Band Speed (screens/s)", Range(0, 2)) = 0.12
        _TrackingHeight ("Tracking Band Height (screens)", Range(0, 0.5)) = 0.06
        _HeadSwitch ("Head Switch Noise", Range(0, 1)) = 0.5
        _HeadSwitchHeight ("Head Switch Height (screens)", Range(0, 0.2)) = 0.035
        _Noise ("Tape Noise", Range(0, 1)) = 0.3
        _Scanlines ("Scanlines", Range(0, 1)) = 0.3
        _ScanlineCount ("Scanline Count", Range(100, 1200)) = 480
        _Wash ("Washed Out", Range(0, 1)) = 0.35
        _Vignette ("Vignette", Range(0, 1)) = 0.35
        _FrameRate ("Tape Frame Rate (steps/s)", Range(1, 60)) = 24
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "VhsTape"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            float _Intensity;
            float _ChromaBleed;
            float _ChromaLag;
            float _LumaSoftness;
            float _Jitter;
            float _Tracking;
            float _TrackingSpeed;
            float _TrackingHeight;
            float _HeadSwitch;
            float _HeadSwitchHeight;
            float _Noise;
            float _Scanlines;
            float _ScanlineCount;
            float _Wash;
            float _Vignette;
            float _FrameRate;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Smooth 1-D value noise, for the slow wobble and the tracking band's tear profile.
            float Noise1(float x)
            {
                float i = floor(x);
                float f = x - i;
                f = f * f * (3.0 - 2.0 * f);
                return lerp(Hash(float2(i, 0.37)), Hash(float2(i + 1.0, 0.37)), f);
            }

            float3 SampleRgb(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            // NTSC YIQ: the split the tape itself makes — luma on one carrier, the two chroma axes on another.
            float3 ToYiq(float3 rgb)
            {
                return float3(dot(rgb, float3(0.299, 0.587, 0.114)),
                              dot(rgb, float3(0.596, -0.274, -0.322)),
                              dot(rgb, float3(0.211, -0.523, 0.312)));
            }

            float3 ToRgb(float3 yiq)
            {
                return float3(yiq.x + 0.956 * yiq.y + 0.621 * yiq.z,
                              yiq.x - 0.272 * yiq.y - 0.647 * yiq.z,
                              yiq.x - 1.106 * yiq.y + 1.703 * yiq.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 clean = SampleRgb(uv);

                float k = _Intensity;
                float2 px = float2(_ScreenParams.z - 1.0, _ScreenParams.w - 1.0);   // one pixel in uv
                float frame = floor(_Time.y * _FrameRate);
                float t = frame / max(_FrameRate, 1.0);                             // tape time, stepping at tape rate

                // ---------------------------------------------------- geometry
                // Rows are read off the tape one at a time: each row group has
                // its own timing error (the jitter), and the whole frame sways
                // (the wobble). Row groups are two scanlines tall so the jitter
                // reads as a tape fault, not as pixel noise.
                float rowGroup = floor(uv.y * _ScreenParams.y * 0.5);
                float jitter = (Hash(float2(rowGroup, frame)) - 0.5) * 2.0 * _Jitter;
                float wobble = (Noise1(uv.y * 4.0 + t * 1.3) - 0.5) * 2.0 * _Jitter * 1.5;
                float shiftPx = (jitter + wobble) * k;

                // Tracking band: a strip crawling down the frame (y-up uv, so it
                // moves toward 0). Inside it the rows tear sideways with a
                // noisy profile, the luma fills with streaks and the colour
                // drops out. Strength also widens the band, so a gameplay
                // burst reads as a big tracking error, not a thin line.
                float bandY = 1.0 - frac(t * _TrackingSpeed + 0.31);
                float bandHalf = _TrackingHeight * (0.5 + 0.7 * _Tracking);
                float band = 1.0 - smoothstep(0.0, bandHalf, abs(uv.y - bandY));
                band *= _Tracking * k;
                float tear = (Noise1(uv.y * 90.0 + t * 7.0) - 0.5) * 2.0;
                shiftPx += band * tear * 60.0;

                // Head switch: the last rows of the frame are read as the heads
                // swap, so they skew hard to one side and fill with noise.
                float head = 1.0 - smoothstep(0.0, max(_HeadSwitchHeight, 1e-4), uv.y);
                head = head * head * _HeadSwitch * k;
                shiftPx += head * (18.0 + 10.0 * Noise1(t * 9.0)) * (1.0 + 0.6 * (Hash(float2(rowGroup, frame + 5.0)) - 0.5));

                float2 uvT = uv + float2(shiftPx * px.x, 0.0);

                // ---------------------------------------------------- signal
                // Luma: a touch of vertical softness (the tape's own resolution).
                float soft = _LumaSoftness * k * px.y;
                float3 lumaSrc = SampleRgb(uvT) * 0.5
                               + SampleRgb(uvT + float2(0.0, soft)) * 0.25
                               + SampleRgb(uvT - float2(0.0, soft)) * 0.25;
                float y = ToYiq(lumaSrc).x;

                // Chroma: recorded at a fraction of the luma bandwidth, so it is
                // smeared sideways over _ChromaBleed pixels and trails the luma
                // by _ChromaLag — the colour fringes on every hard edge.
                float2 lagUv = uvT + float2(_ChromaLag * k * px.x, 0.0);
                float bleed = _ChromaBleed * k * px.x / 3.0;
                float3 chromaSrc = SampleRgb(lagUv) * 0.22
                                 + (SampleRgb(lagUv + float2(bleed, 0.0)) + SampleRgb(lagUv - float2(bleed, 0.0))) * 0.17
                                 + (SampleRgb(lagUv + float2(bleed * 2.0, 0.0)) + SampleRgb(lagUv - float2(bleed * 2.0, 0.0))) * 0.12
                                 + (SampleRgb(lagUv + float2(bleed * 3.0, 0.0)) + SampleRgb(lagUv - float2(bleed * 3.0, 0.0))) * 0.10;
                float2 iq = ToYiq(chromaSrc).yz;

                // ---------------------------------------------------- faults
                float noise = _Noise * k;

                // Tracking band: luma streaks, colour dropout.
                float streak = Hash(float2(rowGroup, frame + 11.0));
                y = lerp(y, y * 0.6 + streak * 0.7, band);
                iq *= 1.0 - band * 0.9;

                // Head switch rows: mostly noise.
                float headNoise = Hash(float2(rowGroup + floor(uv.x * 24.0) * 7.0, frame + 3.0));
                y = lerp(y, y * 0.5 + headNoise * 0.5, head);
                iq *= 1.0 - head * 0.7;

                // Tape noise: fine grain, sparse white dropout dashes, and the
                // whole frame's brightness flickering a little.
                float grain = Hash(uv * _ScreenParams.xy + frame * 0.73) - 0.5;
                y += grain * noise * 0.12;
                float2 dashCell = float2(floor(uv.x * 70.0 + Hash(float2(rowGroup, frame)) * 3.0), rowGroup);
                float dropout = step(1.0 - noise * 0.012, Hash(dashCell + frame * 1.37));
                y = lerp(y, 0.95, dropout);
                iq *= 1.0 - dropout;
                y *= 1.0 + (Hash(float2(frame, 0.5)) - 0.5) * 0.08 * noise;

                // Washed out: lifted blacks, crushed whites, tired colour.
                float wash = _Wash * k;
                y = lerp(y, y * 0.82 + 0.10, wash);
                iq *= 1.0 - wash * 0.4;

                float3 tape = ToRgb(float3(y, iq));

                // CRT scanlines and the corner darkening of an old tube.
                float scan = 0.5 + 0.5 * cos(uv.y * _ScanlineCount * TWO_PI);
                tape *= 1.0 - _Scanlines * k * 0.5 * (1.0 - scan);
                float2 centred = (uv - 0.5) * 2.0;                                  // elliptical, following the screen
                float vig = smoothstep(0.55, 1.35, length(centred * float2(0.9, 1.0)));
                tape *= 1.0 - _Vignette * k * vig;

                return half4(lerp(clean, saturate(tape), k), 1.0);
            }
            ENDHLSL
        }
    }
}
