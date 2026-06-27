Shader "SpaceEngine/Streaming/Star LOD 1 Light"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (1, 0.72, 0.32, 1)
        _Intensity ("Intensity", Range(0, 32)) = 8
        _Opacity ("Opacity", Range(0, 1)) = 1
        _RayStrength ("Ray Strength", Range(0, 3)) = 0.65
        _RayCount ("Ray Count", Range(4, 32)) = 8
        _DiscRadius ("Disc Radius", Range(0.001, 1)) = 0.1
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
            Name "StarLod1Light"
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
                float _Opacity;
                float _RayStrength;
                float _RayCount;
                float _DiscRadius;
                float _Seed;
                float _SurfaceTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 radialCoordinate = input.uv * 2.0 - 1.0;
                float radius = length(radialCoordinate);
                float discRadius = max(_DiscRadius, 0.001);

                // The actual luminous sphere is rendered by the LOD 1 mesh.
                // This billboard contributes only the optical glare outside it:
                // forward-scattering halo, bloom source and diffraction spikes.
                float outsideDisc = max(radius - discRadius, 0.0) /
                                    discRadius;

                float nearHalo = exp(-outsideDisc * 1.85);
                float farHalo = exp(-outsideDisc * 0.54) * 0.16;
                float coreBleed = exp(-pow(radius / (discRadius * 1.75), 2.0)) *
                                  0.34;

                float angle = atan2(radialCoordinate.y, radialCoordinate.x);
                float spikeFrequency = max(2.0, floor(_RayCount * 0.5));
                float phase = angle * spikeFrequency + _Seed * 31.0 +
                              _SurfaceTime * 0.004;

                // abs(cos) generates the requested number of bilateral spikes.
                float thinSpike = pow(abs(cos(phase)), 120.0);
                float broadSpike = pow(abs(cos(phase)), 18.0);
                float spikeFalloff = exp(-outsideDisc * 0.78) *
                                     saturate(1.0 - radius * 0.64);

                float rays = (thinSpike * 1.25 + broadSpike * 0.14) *
                             spikeFalloff * _RayStrength;

                // Fade square corners without a hard circular cut-off.
                float outerFade = 1.0 - smoothstep(0.86, 1.42, radius);
                float brightness = (nearHalo * 0.76 + farHalo + coreBleed + rays) *
                                   outerFade * _Intensity * _Opacity;

                return half4(_BaseColor.rgb * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
