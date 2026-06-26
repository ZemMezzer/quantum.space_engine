Shader "SpaceEngine/Streaming/Star Corona"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (1, 0.70, 0.25, 1)
        _Intensity ("Intensity", Range(0, 16)) = 2
        _RimPower ("Rim Power", Range(0.1, 8)) = 2.15
        _TurbulenceScale ("Turbulence Scale", Range(4, 160)) = 30
        _ShellDisplacement ("Shell Displacement", Range(0, 0.12)) = 0.035
        _FlowSpeed ("Flow Speed", Range(0, 4)) = 0.2
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
            Name "StarCorona"
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
                float _RimPower;
                float _TurbulenceScale;
                float _ShellDisplacement;
                float _FlowSpeed;
                float _Seed;
                float _SurfaceTime;
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
                float shellNoise : TEXCOORD3;
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
                for (int octave = 0; octave < 3; octave++)
                {
                    sum += ValueNoise(p) * amplitude;
                    p = p * 2.07 + float3(11.9, 31.3, 7.7);
                    amplitude *= 0.5;
                }

                return sum / 0.875;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float time = _SurfaceTime * _FlowSpeed;
                float3 direction = normalize(input.positionOS.xyz);
                float shellNoise = Fbm(
                    direction * _TurbulenceScale +
                    float3(time * 0.22, -time * 0.15, time * 0.11) +
                    _Seed * 19.7);

                float shellOffset = (shellNoise - 0.5) *
                                    _ShellDisplacement;
                float3 positionOS = input.positionOS.xyz *
                                    (1.0 + shellOffset);

                output.positionCS = TransformObjectToHClip(positionOS);
                output.positionWS = TransformObjectToWorld(positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = direction;
                output.shellNoise = shellNoise;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);
                float3 normal = normalize(input.normalWS);

                float grazing = saturate(1.0 - abs(dot(normal, viewDirection)));
                float rim = pow(grazing, _RimPower);

                float time = _SurfaceTime * _FlowSpeed;
                float3 direction = normalize(input.directionOS);
                float coronaNoise = Fbm(
                    direction * (_TurbulenceScale * 1.85) +
                    float3(-time * 0.55, time * 0.31, time * 0.44) +
                    _Seed * 71.3);

                float filamentNoise = Fbm(
                    direction * (_TurbulenceScale * 4.2) +
                    float3(time * 0.92, time * 0.18, -time * 0.67) +
                    _Seed * 137.9);

                float filaments = smoothstep(0.62, 0.91, filamentNoise);
                float turbulence = lerp(0.40, 1.46, coronaNoise);
                float shellVariation = lerp(0.62, 1.32, input.shellNoise);

                float brightness =
                    rim * turbulence * shellVariation *
                    (0.72 + filaments * 0.88) * _Intensity;

                return half4(_BaseColor.rgb * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
