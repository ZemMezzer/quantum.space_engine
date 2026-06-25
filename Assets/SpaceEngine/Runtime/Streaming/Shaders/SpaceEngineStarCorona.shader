Shader "SpaceEngine/Streaming/Star Corona"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (1, 0.65, 0.25, 1)
        _Intensity ("Intensity", Range(0, 16)) = 2
        _RimPower ("Rim Power", Range(0.1, 8)) = 2.2
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
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
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
                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);

                float rim = pow(
                    saturate(1.0 - abs(dot(
                        normalize(input.normalWS),
                        viewDirection))),
                    _RimPower);

                float time = _SurfaceTime * _FlowSpeed;
                float noise = Hash31(
                    normalize(input.directionOS) * 31.7 +
                    float3(time, -time * 0.43, time * 0.17) +
                    _Seed * 19.0);

                float turbulence = lerp(0.65, 1.35, noise);
                float brightness = rim * turbulence * _Intensity;

                return half4(_BaseColor.rgb * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
