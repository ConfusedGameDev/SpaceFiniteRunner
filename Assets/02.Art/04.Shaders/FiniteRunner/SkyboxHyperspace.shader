// The runner's sky once Light Speed is reached: the scene's own
// latitude-longitude panorama (the Skybox/Panoramic look, copied onto this
// material by the HyperspaceSky driver) crossfaded by _Blend into a
// hyperspace tunnel — cyan / aqua / green streaks rushing out of a dark teal
// vanishing point along _TunnelDir (the ship's heading, world space).
//
// The tunnel is procedural: each view direction is mapped onto three nested
// cylinders round the axis (radius = parallax layer). Around the axis the
// cylinder is cut into _Columns angular columns, along it into cells of
// _CellLength; a hashed cell holds one streak or none (_Density). _Scroll is
// the distance flown, accumulated by the driver so a speed change never jumps
// the pattern. In front of the camera streaks come at you, behind they
// recede, so the look-back camera sees the same tunnel leaving.
Shader "Skybox/FiniteRunner/Hyperspace"
{
    Properties
    {
        [Header(Panorama)]
        [NoScaleOffset] _MainTex ("Panorama (lat-long)", 2D) = "black" {}
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1
        _Tint ("Tint", Color) = (0.5, 0.5, 0.5, 0.5)
        _Rotation ("Rotation", Range(0, 360)) = 0

        [Header(Hyperspace)]
        _Blend ("Blend (0 sky, 1 hyperspace)", Range(0, 1)) = 0
        _TunnelDir ("Tunnel axis (world)", Vector) = (0, 0, 1, 0)
        _Scroll ("Scroll (distance flown)", Float) = 0
        _Flash ("Flash", Range(0, 4)) = 0
        _BackgroundColor ("Background", Color) = (0.01, 0.05, 0.06, 1)
        _CoreColor ("Vanishing point", Color) = (0, 0.01, 0.015, 1)
        [HDR] _ColorA ("Streak cyan", Color) = (0.2, 1.6, 1.5, 1)
        [HDR] _ColorB ("Streak aqua", Color) = (0.35, 1.1, 1.8, 1)
        [HDR] _ColorC ("Streak green", Color) = (0.4, 1.8, 0.5, 1)
        _GreenShare ("Green share", Range(0, 1)) = 0.2
        _Brightness ("Brightness", Range(0, 8)) = 1.6
        _Columns ("Columns", Range(8, 256)) = 96
        _Density ("Density", Range(0, 1)) = 0.35
        _CellLength ("Cell length", Range(0.1, 8)) = 1.2
        _StreakLength ("Streak length", Range(0.05, 1)) = 0.45
        _StreakWidth ("Streak width", Range(0.05, 1)) = 0.35
        _CoreRadius ("Dark core (sin of angle)", Range(0, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half _Exposure;
                half4 _Tint;
                float _Rotation;
                float _Blend;
                float4 _TunnelDir;
                float _Scroll;
                float _Flash;
                half4 _BackgroundColor;
                half4 _CoreColor;
                half4 _ColorA;
                half4 _ColorB;
                half4 _ColorC;
                float _GreenShare;
                float _Brightness;
                float _Columns;
                float _Density;
                float _CellLength;
                float _StreakLength;
                float _StreakWidth;
                float _CoreRadius;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz; // the skybox mesh is unrotated: object = world direction
                return output;
            }

            // Skybox/Panoramic's latitude-longitude lookup, with its _Rotation
            // and a gradient fix so the seam does not drop to the smallest mip.
            half3 Panorama(float3 direction)
            {
                float angle = _Rotation * PI / 180.0;
                float s = sin(angle), c = cos(angle);
                float3 d = normalize(float3(c * direction.x - s * direction.z, direction.y, s * direction.x + c * direction.z));
                float2 uv = float2(0.5, 1.0) - float2(atan2(d.z, d.x) * (0.5 / PI), acos(clamp(d.y, -1.0, 1.0)) / PI);
                float2 dx = ddx(uv), dy = ddy(uv);
                dx.x -= round(dx.x);
                dy.x -= round(dy.x);
                half3 tex = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, dx, dy).rgb;
                #ifdef UNITY_COLORSPACE_GAMMA
                    half colorSpaceDouble = 2.0;
                #else
                    half colorSpaceDouble = 4.59479380;
                #endif
                return tex * _Tint.rgb * colorSpaceDouble * _Exposure;
            }

            float4 Hash4(float3 p)
            {
                float4 q = frac(float4(p.xyzx) * float4(0.1031, 0.1030, 0.0973, 0.1099));
                q += dot(q, q.wzxy + 33.33);
                return frac((q.xxyz + q.yzzw) * q.zywx);
            }

            // One cylinder layer of streaks. phi01 = angle round the axis in
            // turns, depth = distance along the axis on this layer's cylinder.
            half3 StreakLayer(float phi01, float depth, float layer)
            {
                float column = phi01 * _Columns;
                float columnId = floor(column);
                float across = frac(column) - 0.5;

                float along = depth / _CellLength;
                float rowId = floor(along);
                float within = frac(along);

                float4 h = Hash4(float3(columnId, rowId, layer * 17.0));
                if (h.x > _Density) return 0;

                // A streak: a slice of the cell along the axis, soft across it.
                float span = _StreakLength * (0.5 + h.y);
                float start = h.z * max(0.0, 1.0 - span);
                float t = (within - start) / span; // 0 tail (far end) .. 1 head (near end)
                if (t < 0.0 || t > 1.0) return 0;
                float taper = smoothstep(0.0, 0.35, t) * smoothstep(1.0, 0.85, t);
                float width = _StreakWidth * (0.5 + 0.5 * h.w) * 0.5;
                float core = saturate(1.0 - abs(across + (h.y - 0.5) * 0.4) / max(width, 1e-3));

                half3 color = h.w < _GreenShare ? _ColorC.rgb : lerp(_ColorA.rgb, _ColorB.rgb, h.y);
                return color * taper * core * core * (0.55 + 0.9 * h.z);
            }

            half3 Hyperspace(float3 direction)
            {
                float3 d = normalize(direction);
                float3 axis = normalize(_TunnelDir.xyz + float3(0, 0, 1e-5));
                float3 helper = abs(axis.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 side = normalize(cross(helper, axis));
                float3 up = cross(axis, side);

                float cosine = dot(d, axis);
                float sine = sqrt(saturate(1.0 - cosine * cosine));
                float phi01 = atan2(dot(d, up), dot(d, side)) / (2.0 * PI) + 0.5;
                float away = abs(cosine) / max(sine, 1e-4); // depth along a unit cylinder
                float travel = cosine >= 0.0 ? _Scroll : -_Scroll; // ahead: toward you; behind: receding

                // Background: dark teal, darker into the vanishing point.
                float open = smoothstep(_CoreRadius * 0.5, _CoreRadius * 3.0 + 0.2, sine);
                half3 color = lerp(_CoreColor.rgb, _BackgroundColor.rgb, open);

                half3 streaks = 0;
                streaks += StreakLayer(phi01, away * 1.0 + travel, 0.0);
                streaks += StreakLayer(frac(phi01 + 0.37), away * 1.7 + travel * 1.0, 1.0) * 0.8;
                streaks += StreakLayer(frac(phi01 + 0.71), away * 2.9 + travel * 1.0, 2.0) * 0.6;

                // Deep in the tunnel the streaks thin out into the core's dark.
                float fade = smoothstep(_CoreRadius, _CoreRadius + 0.25, sine);
                color += streaks * _Brightness * fade * (1.0 + _Flash);
                color += _Flash * _ColorA.rgb * 0.15 * open;
                return color;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 sky = _Blend < 1.0 ? Panorama(input.direction) : 0;
                half3 warp = _Blend > 0.0 ? Hyperspace(input.direction) : 0;
                return half4(lerp(sky, warp, saturate(_Blend)), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
