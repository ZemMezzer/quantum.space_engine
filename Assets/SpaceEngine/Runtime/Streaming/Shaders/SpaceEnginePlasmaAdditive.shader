Shader "SpaceEngine/Streaming/Plasma Additive"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (1, 0.55, 0.18, 1)
        _Intensity ("Intensity", Range(0, 16)) = 2
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 0.5
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
            Name "PlasmaAdditive"
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
                float _PulseSpeed;
                float _Seed;
                float _SurfaceTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _SurfaceTime * _PulseSpeed;
                float pulse = 0.72 + 0.28 * sin(
                    time + input.uv.x * 13.0 + input.uv.y * 7.0 +
                    _Seed * 31.0);

                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);

                float fresnel = pow(
                    saturate(1.0 - abs(dot(
                        normalize(input.normalWS),
                        viewDirection))),
                    0.65);

                float brightness = (0.55 + fresnel * 0.9) *
                                   pulse * _Intensity;

                return half4(_BaseColor.rgb * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
