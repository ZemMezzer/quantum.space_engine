Shader "SpaceEngine/Streaming/Black Hole Horizon"
{
    Properties
    {
        [HDR] _PhotonRingColor ("Photon Ring Color", Color) = (0.92, 0.94, 1.0, 1)
        _PhotonRingIntensity ("Photon Ring Intensity", Range(0, 4)) = 0.16
        _ApparentShadowScale ("Apparent Shadow Scale", Range(1, 4)) = 1.0
        _PhotonRingWidth ("Photon Ring Width (Pixels)", Range(0.25, 4)) = 0.85
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
                float _ApparentShadowScale;
                float _PhotonRingWidth;
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
                float3 shadowPositionOS = input.positionOS.xyz *
                                          _ApparentShadowScale;
                output.positionCS = TransformObjectToHClip(shadowPositionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(shadowPositionOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);

                float silhouette = abs(dot(normalWS, viewDirection));
                float pixelWidth = max(fwidth(silhouette), 0.00001);
                float photonRing = 1.0 - smoothstep(
                    pixelWidth * _PhotonRingWidth,
                    pixelWidth * (_PhotonRingWidth + 1.10),
                    silhouette);

                float3 ringColor = _PhotonRingColor.rgb * photonRing *
                                   _PhotonRingIntensity;
                return half4(ringColor, 1.0);
            }
            ENDHLSL
        }
    }
}
