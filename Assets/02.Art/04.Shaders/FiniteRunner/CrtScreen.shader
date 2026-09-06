// CRT screen: the finished picture shown on a curved glass tube — the display
// the PSX console plugs into and the VHS deck plays out to, so it is the LAST
// pass of the chain. Every knob is one physical trait of a tube:
//   - CURVATURE: the glass bulges, so the picture is barrel-warped
//     (_Curvature) and the visible area is the rounded barrel silhouette of a
//     tube, black beyond it; the corners of the glass are rounded
//     (_CornerRadius). The scanlines and the mask bend WITH the picture.
//   - BEAM FOCUS: the electron beam is never a point, so each phosphor dot
//     BLEEDS sideways into its neighbours (_Bleed, in screen pixels) and the
//     focus is worst toward the edges of the tube, so the bleed, the
//     convergence error and the vignette all grow with distance from the
//     centre — the picture goes soft and smeary at the edges;
//   - CONVERGENCE: the three guns never land on exactly the same spot; the
//     red and blue images drift apart from the green toward the edges
//     (_Convergence, px at the corners);
//   - HALATION: bright areas glow into their surroundings through the glass
//     (_Glow);
//   - SCANLINES: the beam paints _ScanlineCount rows with dark gaps between
//     them (_Scanlines); bright rows bloom over the gaps;
//   - the APERTURE GRILLE: vertical red/green/blue phosphor stripes
//     _MaskScale screen pixels wide (_Mask);
//   - REFRESH FLICKER: the whole frame's brightness breathes at
//     _RefreshRate (_Flicker), keyed on a quantised clock like the far
//     glitch, the speed lines, the tape and the console, so pausing freezes
//     it;
//   - VIGNETTE: the corners of the tube are dimmer (_Vignette).
// Everything is computed in SCREEN PIXELS of the target (_ScreenParams — the
// blitter never fills _BlitTexture_TexelSize) so the grille is locked to the
// pixel grid. The pass takes a copy of the camera colour (CrtScreenFeature,
// AfterRenderingPostProcessing, appended at the END of the feature list —
// after the PsxLook and the VhsTape: the tube shows whatever the tape plays)
// and _Intensity scales every trait, the warp included, and blends the result
// over the clean picture. At _Intensity 0 the pass is not enqueued; CrtScreen
// (the scene component) writes every property each frame.
Shader "Hidden/FiniteRunner/CrtScreen"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Curvature ("Curvature", Range(0, 1)) = 0.35
        _CornerRadius ("Corner Radius (screens)", Range(0, 0.3)) = 0.08
        _Scanlines ("Scanlines", Range(0, 1)) = 0.45
        _ScanlineCount ("Scanline Count", Range(100, 1200)) = 540
        _Bleed ("Phosphor Bleed (px)", Range(0, 8)) = 2.5
        _Glow ("Halation", Range(0, 1)) = 0.3
        _Mask ("Aperture Grille", Range(0, 1)) = 0.25
        _MaskScale ("Grille Stripe Width (px)", Range(1, 4)) = 1
        _Convergence ("Convergence Error (px)", Range(0, 4)) = 1
        _Flicker ("Refresh Flicker", Range(0, 1)) = 0.25
        _RefreshRate ("Refresh Rate (Hz)", Range(24, 120)) = 60
        _Vignette ("Vignette", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "CrtScreen"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            float _Intensity;
            float _Curvature;
            float _CornerRadius;
            float _Scanlines;
            float _ScanlineCount;
            float _Bleed;
            float _Glow;
            float _Mask;
            float _MaskScale;
            float _Convergence;
            float _Flicker;
            float _RefreshRate;
            float _Vignette;

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float3 SampleRgb(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            // One tap of the beam: the three guns land at three different
            // spots (the convergence error), so each channel is read from its
            // own uv.
            float3 SampleBeam(float2 uv, float2 conv)
            {
                return float3(SampleRgb(uv - conv).r,
                              SampleRgb(uv).g,
                              SampleRgb(uv + conv).b);
            }

            // Signed distance to a rounded rectangle of half-size b and corner
            // radius r, in coordinates where the rectangle is centred.
            float RoundedRect(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float k = _Intensity;
                float3 clean = SampleRgb(uv);

                float2 res = _ScreenParams.xy;
                float2 px = 1.0 / res;
                float aspect = res.x / res.y;

                // ------------------------------------------------ the glass
                // Barrel warp: each axis bows by the square of the other, the
                // classic tube. Screen pixels whose source lands outside the
                // picture are the black glass around the barrel silhouette.
                float2 p = uv * 2.0 - 1.0;
                float curv = _Curvature * k;
                float2 warped = p * (1.0 + curv * 0.22 * p.yx * p.yx);
                float2 uvC = warped * 0.5 + 0.5;

                // How far out on the tube this pixel sits: 0 at the centre,
                // 1 at the corners. The focus, the convergence and the
                // vignette all key on it.
                float edge = saturate(dot(p, p) * 0.5);

                // Rounded corners on the visible picture, measured in the
                // warped picture's own space and corrected for aspect so the
                // corners are circular; a few pixels of soft bezel edge.
                float2 ext = float2(aspect, 1.0);
                float r = _CornerRadius * k;
                float d = RoundedRect(warped * ext, ext, r);
                float soft = 2.5 * px.y * 2.0;                                  // ~2.5 px in the -1..1 space
                float glass = 1.0 - smoothstep(-soft, soft, d);

                // ------------------------------------------------ the beam
                // Convergence error: red and blue drift away from green toward
                // the edges of the tube.
                float2 conv = float2(_Convergence * k * (0.15 + 0.85 * edge) * px.x, 0.0);

                // Phosphor bleed: a horizontal smear whose radius grows toward
                // the edges where the beam loses focus. Five taps.
                float bleed = _Bleed * k * (1.0 + 1.5 * edge) * px.x;
                float2 o1 = float2(bleed * 0.5, 0.0);
                float2 o2 = float2(bleed, 0.0);
                float3 c = SampleBeam(uvC, conv) * 0.36
                         + (SampleBeam(uvC + o1, conv) + SampleBeam(uvC - o1, conv)) * 0.24
                         + (SampleBeam(uvC + o2, conv) + SampleBeam(uvC - o2, conv)) * 0.08;

                // Halation: bright areas glow through the glass. A wide,
                // cheap cross of four taps, thresholded so blacks stay black.
                float glowR = 6.0 * k;
                float3 halo = (SampleRgb(uvC + float2( glowR,  glowR) * px)
                             + SampleRgb(uvC + float2(-glowR,  glowR) * px)
                             + SampleRgb(uvC + float2( glowR, -glowR) * px)
                             + SampleRgb(uvC + float2(-glowR, -glowR) * px)) * 0.25;
                halo = max(halo - 0.12, 0.0);
                c += halo * _Glow * k * 0.55;

                // ------------------------------------------------ the phosphors
                // Scanlines follow the warped picture, so they bend with the
                // glass. Bright rows bloom over the dark gaps.
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                float scan = 0.5 + 0.5 * cos(uvC.y * _ScanlineCount * TWO_PI);
                c *= 1.0 - _Scanlines * k * (1.0 - scan) * (1.0 - 0.6 * saturate(lum));

                // Aperture grille: R, G, B stripes locked to the screen's own
                // pixel columns, with the lost light put back so the picture
                // keeps its brightness.
                float stripe = fmod(floor(uv.x * res.x / max(_MaskScale, 1.0)), 3.0);
                float3 grille = float3(stripe < 0.5, abs(stripe - 1.0) < 0.5, stripe > 1.5);
                float m = _Mask * k;
                c *= lerp(1.0, grille, m) * (1.0 + m * 0.6);

                // ------------------------------------------------ the frame
                // Refresh flicker: the whole frame breathes at the refresh
                // rate, keyed on a quantised clock so the pause menu freezes it.
                float frame = floor(_Time.y * _RefreshRate);
                c *= 1.0 + (Hash(float2(frame, 0.5)) - 0.5) * _Flicker * k * 0.12;

                // Dimmer corners, and nothing beyond the glass.
                float vig = smoothstep(0.35, 1.6, length(p * float2(0.9, 1.0)));
                c *= 1.0 - _Vignette * k * vig;
                c *= glass;

                return half4(lerp(clean, saturate(c), k), 1.0);
            }
            ENDHLSL
        }
    }
}
