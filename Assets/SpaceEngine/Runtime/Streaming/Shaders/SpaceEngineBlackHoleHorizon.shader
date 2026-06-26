Shader "SpaceEngine/Streaming/Black Hole Horizon"
{
    Properties
    {
        [HDR] _PhotonRingColor ("Photon Ring Color", Color) = (1, 0.32, 0.06, 1)
        _PhotonRingIntensity ("Photon Ring Intensity", Range(0, 4)) = 0.7
        _Seed ("Seed", Range(0, 1)) = 0
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
            Name "EventHorizon"
            Tags { "LightMode" = "UniversalForward" }

            // The horizon replaces the already-rendered sky with black while
            // still writing depth so the front half of an accretion disk can
            // remain visible and the rear half is hidden by the sphere.
            Blend One Zero
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _PhotonRingColor;
                float _PhotonRingIntensity;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);

                // A narrow, optically thin photon ring. This is deliberately
                // restrained for diskless holes; their actual visible light
                // comes from the lensed background, not self-emission.
                float rim = 1.0 - saturate(abs(dot(normalWS, viewDirection)));
                float photonRing = smoothstep(0.86, 0.995, rim);
                photonRing = pow(photonRing, 1.35);

                float flicker = 0.96 + 0.04 * sin(
                    _Seed * 67.0 +
                    atan2(normalWS.z, normalWS.x) * 9.0);

                float3 ringColor = _PhotonRingColor.rgb *
                                   photonRing *
                                   _PhotonRingIntensity *
                                   flicker;

                return half4(ringColor, 1.0);
            }
            ENDHLSL
        }
    }
}
