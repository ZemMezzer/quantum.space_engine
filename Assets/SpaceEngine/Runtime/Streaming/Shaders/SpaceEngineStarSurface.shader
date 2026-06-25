Shader "SpaceEngine/Streaming/Star Surface"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (1, 0.75, 0.35, 1)
        [HDR] _SurfaceColor ("Surface Color", Color) = (1, 0.85, 0.45, 1)
        [HDR] _HotColor ("Hot Color", Color) = (1, 1, 0.85, 1)
        [HDR] _SpotColor ("Spot Color", Color) = (0.25, 0.04, 0.01, 1)
        _GranulationScale ("Granulation Scale", Range(4, 160)) = 52
        _SpotScale ("Spot Scale", Range(2, 40)) = 18
        _FlowSpeed ("Flow Speed", Range(0, 4)) = 0.2
        _Seed ("Seed", Range(0, 1)) = 0
        _SurfaceTime ("Surface Time", Float) = 0
        _Intensity ("Intensity", Range(0, 16)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "StarSurface"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SurfaceColor;
                half4 _HotColor;
                half4 _SpotColor;
                float _GranulationScale;
                float _SpotScale;
                float _FlowSpeed;
                float _Seed;
                float _SurfaceTime;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 directionOS : TEXCOORD2;
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i + float3(0, 0, 0));
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);

                float y0 = lerp(x00, x10, f.y);
                float y1 = lerp(x01, x11, f.y);

                return lerp(y0, y1, f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    sum += Noise3(p) * amplitude;
                    p = p * 2.07 + 17.17;
                    amplitude *= 0.5;
                }

                return sum;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = normalize(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _SurfaceTime * _FlowSpeed;
                float3 direction = normalize(input.directionOS);

                float3 granulationPosition =
                    direction * _GranulationScale +
                    float3(time, time * 0.31, -time * 0.19) +
                    _Seed * 31.73;

                float coarse = Fbm(granulationPosition);
                float fine = Fbm(granulationPosition * 4.7 + 9.1);
                float granulation = saturate(coarse * 0.72 + fine * 0.54);

                float spotNoise = Fbm(
                    direction * _SpotScale +
                    float3(-time * 0.07, time * 0.11, time * 0.05) +
                    _Seed * 91.19);

                float spots = smoothstep(0.64, 0.82, spotNoise);

                float3 colour = lerp(
                    _SurfaceColor.rgb,
                    _HotColor.rgb,
                    granulation);

                colour = lerp(colour, _SpotColor.rgb, spots * 0.86);

                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);

                float limb = pow(
                    saturate(1.0 - abs(dot(
                        normalize(input.normalWS),
                        viewDirection))),
                    1.6);

                colour += _HotColor.rgb * limb * 0.48;
                colour = lerp(_BaseColor.rgb, colour, 0.92);

                return half4(colour * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
