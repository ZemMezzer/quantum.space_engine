Shader "SpaceEngine/Streaming/Plasma Additive"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (1, 0.62, 0.18, 1)
        _Intensity ("Intensity", Range(0, 20)) = 2
        _FlowSpeed ("Flow Speed", Range(0, 8)) = 0.5
        _TurbulenceScale ("Turbulence Scale", Range(2, 96)) = 18
        _GasSoftness ("Gas Core Softness", Range(0.25, 5)) = 1.65
        _FlickerStrength ("Flicker Strength", Range(0, 1)) = 0.3
        _Seed ("Seed", Range(0, 1)) = 0
        _SurfaceTime ("Surface Time", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ProminenceGas"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Intensity;
                float _FlowSpeed;
                float _TurbulenceScale;
                float _GasSoftness;
                float _FlickerStrength;
                float _Seed;
                float _SurfaceTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 gasData : TEXCOORD1;
                float4 color : COLOR;
                float3 normalWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);

                float n000 = Hash31(cell + float3(0, 0, 0));
                float n100 = Hash31(cell + float3(1, 0, 0));
                float n010 = Hash31(cell + float3(0, 1, 0));
                float n110 = Hash31(cell + float3(1, 1, 0));
                float n001 = Hash31(cell + float3(0, 0, 1));
                float n101 = Hash31(cell + float3(1, 0, 1));
                float n011 = Hash31(cell + float3(0, 1, 1));
                float n111 = Hash31(cell + float3(1, 1, 1));

                float x00 = lerp(n000, n100, local.x);
                float x10 = lerp(n010, n110, local.x);
                float x01 = lerp(n001, n101, local.x);
                float x11 = lerp(n011, n111, local.x);
                float y0 = lerp(x00, x10, local.y);
                float y1 = lerp(x01, x11, local.y);

                return lerp(y0, y1, local.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;

                [unroll]
                for (int octave = 0; octave < 4; octave++)
                {
                    sum += ValueNoise(p) * amplitude;
                    p = p * 2.04 + float3(13.7, 29.1, 5.3);
                    amplitude *= 0.5;
                }

                return sum / 0.9375;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float time = _SurfaceTime * _FlowSpeed;
                float phase = input.uv2.x * 31.0 +
                              input.uv2.y * 17.0 +
                              _Seed * 29.0;

                float plumeNoise = Fbm(float3(
                    input.uv.x * _TurbulenceScale * 0.36 - time * 0.75,
                    input.uv.y * 5.2 + time * 0.18,
                    phase));

                float edgeDistance = abs(input.uv.y * 2.0 - 1.0);
                float displacement = (plumeNoise - 0.5) *
                                     (0.004 + 0.010 * (1.0 - edgeDistance));

                float3 positionOS = input.positionOS.xyz +
                                    input.normalOS * displacement;

                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = input.uv;
                output.gasData = input.uv2;
                output.color = input.color;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(positionOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _SurfaceTime * _FlowSpeed;
                float phase = input.gasData.x * 31.0 +
                              input.gasData.y * 17.0 +
                              _Seed * 29.0;

                float edgeDistance = abs(input.uv.y * 2.0 - 1.0);
                float softCore = pow(
                    saturate(1.0 - edgeDistance),
                    _GasSoftness);

                float startFade = smoothstep(0.0, 0.075, input.uv.x);
                float endFade = 1.0 - smoothstep(0.87, 1.0, input.uv.x);
                float attachmentFade = startFade * endFade;

                float turbulence = Fbm(float3(
                    input.uv.x * _TurbulenceScale - time * 1.15,
                    input.uv.y * 7.5 + time * 0.24,
                    phase));

                float fineTurbulence = Fbm(float3(
                    input.uv.x * _TurbulenceScale * 2.8 + time * 0.72,
                    input.uv.y * 15.0 - time * 0.31,
                    phase + 7.3));

                float brokenGas = smoothstep(0.31, 0.82, turbulence);
                float filaments = smoothstep(0.60, 0.93, fineTurbulence);
                float density = softCore * attachmentFade *
                                (0.24 + 0.76 * brokenGas) *
                                (0.64 + 0.56 * filaments);

                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);
                float sheetFacing = 0.56 + 0.44 *
                    (1.0 - abs(dot(
                        normalize(input.normalWS),
                        viewDirection)));

                float flicker = 1.0 + sin(
                    time * 3.4 +
                    input.uv.x * 12.7 +
                    phase) * _FlickerStrength;

                float brightness = density * sheetFacing *
                                   max(0.0, flicker) *
                                   _Intensity *
                                   (0.72 + input.color.g * 0.44);

                return half4(_BaseColor.rgb * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
