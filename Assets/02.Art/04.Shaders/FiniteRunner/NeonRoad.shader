// The runner's road: a black slab with neon painted across it — hot orange
// edge bands and thin blue lane lines, each with a soft glow spilling inward
// (the synthwave highway). Procedural in MESH space, no texture: the
// decorator stamps every road-kit piece so one local axis spans the width
// (_LateralAxis: the slab's is X, the barrier channel's is Z), so the lateral
// coordinate is (positionOS.axis − _MeshCenter) / _MeshHalfWidth, −1 at one
// edge of the LANE and +1 at the other, whatever the piece is scaled to (the
// track width is a debug knob). The constants are per MESH, so a material per
// kit piece: the road slab (road-straight_v1: its flat centre spans X ±55 m,
// then banked shoulders — the edge line sits on that crease and the shoulders
// beyond it stay dark) and the barrier channel (a unit cube).
// Nothing varies along the track on purpose: pieces are stamped longer than
// their spacing and overlap on bends, and only a lateral-only pattern stays
// seamless through the overlap. Colours are HDR — the scene's Bloom turns
// anything above 1 into the glow. Unlit, opaque, both faces (the slab has a
// skirt), with its own DepthOnly / DepthNormals passes so the prepasses the
// fog and SSR ask for see the road. A kit piece with WALLS (the barrier
// channel) would light its whole wall face, since a wall stands exactly at
// the edge: _WallStrip > 0 confines the neon on vertical faces to a strip
// under the wall's top (_WallTop, local Y), the rest of the wall stays
// asphalt-black — the light line of a synthwave highway, not an orange plank.
Shader "FiniteRunner/NeonRoad"
{
    Properties
    {
        _BaseColor ("Asphalt", Color) = (0.01, 0.01, 0.018, 1)

        [Header(Edges)]
        [HDR] _EdgeColor ("Edge Neon", Color) = (4, 1.1, 0.12, 1)
        _EdgeWidth ("Edge Width (fraction of half width)", Range(0, 1)) = 0.05
        _EdgeGlow ("Edge Glow Reach", Range(0.001, 1)) = 0.18
        _EdgeGlowStrength ("Edge Glow Strength", Range(0, 2)) = 0.3

        [Header(Outer line)]
        _OuterOffset ("Outer Line Offset (fraction of half width)", Range(1, 2)) = 1.48
        _OuterStrength ("Outer Line Strength (of the edge neon, 0 = none)", Range(0, 1)) = 0
        _OuterWidth ("Outer Line Width (fraction of half width)", Range(0, 0.2)) = 0.01

        [Header(Lane lines)]
        [HDR] _LineColor ("Lane Neon", Color) = (0.5, 2, 6, 1)
        _LaneOffset ("Lane Line Offset (fraction of half width)", Range(0, 1)) = 0.34
        _LineWidth ("Lane Line Width (fraction of half width)", Range(0, 0.2)) = 0.012
        _LineGlow ("Lane Glow Reach", Range(0.001, 1)) = 0.1
        _LineGlowStrength ("Lane Glow Strength", Range(0, 2)) = 0.25
        [Toggle] _CenterLine ("Centre Line", Float) = 0

        [Header(Pulse)]
        _PulseSpeed ("Pulse Speed", Range(0, 10)) = 1.5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.12

        [Header(Mesh)]
        [Enum(Z, 0, X, 1)] _LateralAxis ("Lateral Axis", Float) = 0
        _MeshCenter ("Mesh Lateral Centre (local axis)", Float) = 0
        _MeshHalfWidth ("Mesh Half Width (local axis)", Float) = 0.5
        _WallTop ("Wall Top (local Y)", Float) = 0
        _WallStrip ("Wall Neon Strip Height (local Y, 0 = whole wall)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EdgeColor;
            float _EdgeWidth;
            float _EdgeGlow;
            float _EdgeGlowStrength;
            float _OuterOffset;
            float _OuterStrength;
            float _OuterWidth;
            half4 _LineColor;
            float _LaneOffset;
            float _LineWidth;
            float _LineGlow;
            float _LineGlowStrength;
            float _CenterLine;
            float _PulseSpeed;
            float _PulseAmount;
            float _LateralAxis;
            float _MeshCenter;
            float _MeshHalfWidth;
            float _WallTop;
            float _WallStrip;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "NeonRoad"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float lateral     : TEXCOORD0; // −1..1 across the piece
                float fogFactor   : TEXCOORD1;
                float2 wall       : TEXCOORD2; // x = 1 on a vertical face, y = local Y (the strip is per pixel)
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                float across = _LateralAxis > 0.5 ? input.positionOS.x : input.positionOS.z;
                o.lateral = (across - _MeshCenter) / max(1e-4, _MeshHalfWidth);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                o.wall = float2(step(abs(input.normalOS.y), 0.5), input.positionOS.y);
                return o;
            }

            // A band |u| ∈ [centre − width, centre + width] with one pixel of
            // analytic anti-aliasing, plus its glow: an exponential falloff
            // from the band's outer edge, so the neon bleeds onto the asphalt
            // even before Bloom.
            void Band(float au, float aa, float centre, float width, float glowReach, float glowStrength,
                      out float mask, out float halo)
            {
                float d = abs(au - centre) - width;
                mask = 1.0 - smoothstep(-aa, aa, d);
                halo = exp(-max(0.0, d) / glowReach) * glowStrength * (1.0 - mask);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float au = abs(input.lateral);
                float aa = fwidth(au) + 1e-4;

                float edgeMask, edgeHalo;
                Band(au, aa, 1.0, _EdgeWidth, _EdgeGlow, _EdgeGlowStrength, edgeMask, edgeHalo);
                // Beyond the line (a banked shoulder) is asphalt again: the
                // band is centred on the lane edge, its glow spills both ways.

                // A dimmer second line along the top of a banked shoulder
                // (past the crease), so the bank's slope reads against the sky.
                if (_OuterStrength > 0.0)
                {
                    float oMask, oHalo;
                    Band(au, aa, _OuterOffset, _OuterWidth, _EdgeGlow, _EdgeGlowStrength, oMask, oHalo);
                    edgeMask = max(edgeMask, oMask * _OuterStrength);
                    edgeHalo = max(edgeHalo, oHalo * _OuterStrength);
                }

                float laneMask, laneHalo;
                Band(au, aa, _LaneOffset, _LineWidth, _LineGlow, _LineGlowStrength, laneMask, laneHalo);
                if (_CenterLine > 0.5)
                {
                    float cMask, cHalo;
                    Band(au, aa, 0.0, _LineWidth, _LineGlow, _LineGlowStrength, cMask, cHalo);
                    laneMask = max(laneMask, cMask);
                    laneHalo = max(laneHalo, cHalo);
                }

                float pulse = 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed);
                // Vertical faces only (a floor keeps the flat rule): lit within
                // _WallStrip under the top, dark below. Off when _WallStrip is 0.
                float below = _WallTop - input.wall.y;
                float strip = _WallStrip > 0.0
                    ? lerp(1.0, 1.0 - smoothstep(0.0, _WallStrip, below), input.wall.x)
                    : 1.0;
                edgeMask *= strip;
                edgeHalo *= strip;
                laneMask *= strip;
                laneHalo *= strip;

                half3 color = _BaseColor.rgb;
                color += _LineColor.rgb * laneHalo;
                color = lerp(color, _LineColor.rgb * pulse, laneMask);
                color += _EdgeColor.rgb * edgeHalo;
                color = lerp(color, _EdgeColor.rgb * pulse, edgeMask);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half DepthFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings NormalsVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 NormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
