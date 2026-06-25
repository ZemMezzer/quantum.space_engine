Shader "SpaceEngine/Streaming/Galaxy Proxy"
{
    Properties
    {
        _GalaxyColor ("Galaxy Color", Color) = (0.52, 0.66, 1.0, 1.0)
        _GalaxyShape ("Galaxy Shape", Vector) = (0, 2, 0.25, 0.3)
        _GalaxyStructure ("Galaxy Structure", Vector) = (0.12, 0.45, 0.65, 0.08)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "GalaxyProxy"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 galaxyColor : TEXCOORD1;
                float4 galaxyShape : TEXCOORD2;
                float4 galaxyStructure : TEXCOORD3;
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GalaxyColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GalaxyShape)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GalaxyStructure)
            UNITY_INSTANCING_BUFFER_END(Props)

            float Gaussian(float distanceValue, float width)
            {
                float normalized = distanceValue / max(0.0001, width);
                return exp(-0.5 * normalized * normalized);
            }

            float SmoothCloud(float2 samplePosition, float phase)
            {
                float coarse = sin(samplePosition.x * 7.7 +
                                   samplePosition.y * 3.1 + phase);
                float medium = sin(samplePosition.x * -12.2 +
                                   samplePosition.y * 8.4 + phase * 1.7);
                float fine = sin(samplePosition.x * 20.5 +
                                 samplePosition.y * 17.3 + phase * 0.63);
                return saturate(0.60 + coarse * 0.19 + medium * 0.14 +
                                fine * 0.07);
            }

            float SpiralArmDensity(
                float2 samplePosition,
                float sampleRadius,
                float armCount,
                float tightness)
            {
                if (sampleRadius < 0.075)
                    return 0.0;

                float angle = atan2(samplePosition.y, samplePosition.x);
                float spiral = angle - tightness *
                    log(max(sampleRadius * 6.0, 1.0));
                float crest = 0.5 + 0.5 * cos(spiral * armCount);
                float arm = smoothstep(0.68, 0.95, crest);
                return arm * smoothstep(0.08, 0.24, sampleRadius);
            }

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.galaxyColor = UNITY_ACCESS_INSTANCED_PROP(
                    Props, _GalaxyColor);
                output.galaxyShape = UNITY_ACCESS_INSTANCED_PROP(
                    Props, _GalaxyShape);
                output.galaxyStructure = UNITY_ACCESS_INSTANCED_PROP(
                    Props, _GalaxyStructure);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 localPosition = input.uv * 2.0 - 1.0;
                float type = input.galaxyShape.x;
                float armCount = max(1.0, input.galaxyShape.y);
                float tightness = max(0.0, input.galaxyShape.z);
                float axisRatio = max(0.12, input.galaxyShape.w);
                float coreRadius = max(0.025, input.galaxyStructure.x);
                float barLength = max(0.001, input.galaxyStructure.y);
                float ringRadius = input.galaxyStructure.z;
                float ringWidth = max(0.01, input.galaxyStructure.w);

                bool flatGalaxy = type < 2.0 ||
                                  (type > 2.5 && type < 4.0) ||
                                  (type > 4.5 && type < 5.5);
                float2 diskPosition = localPosition;
                if (flatGalaxy)
                    diskPosition.y /= axisRatio;

                float diskRadius = length(diskPosition);
                if (diskRadius > 1.02)
                    discard;

                float edge = 1.0 - smoothstep(0.72, 1.02, diskRadius);
                float core = Gaussian(diskRadius, coreRadius) * 1.22;
                float disk = exp(-diskRadius * 2.8) * edge;
                float arms = SpiralArmDensity(
                    diskPosition,
                    diskRadius,
                    armCount,
                    tightness);
                float cloud = SmoothCloud(
                    diskPosition,
                    armCount * 0.71 + tightness * 2.17);

                float density = 0.0;
                float youngStars = 0.0;

                if (type < 0.5)
                {
                    density = disk * (0.22 + arms * 0.88) * cloud + core;
                    youngStars = disk * arms * cloud;
                }
                else if (type < 1.5)
                {
                    float2 barScale = float2(
                        max(0.04, barLength),
                        max(0.018, coreRadius * 0.50));
                    float bar = exp(-dot(
                        diskPosition / barScale,
                        diskPosition / barScale) * 1.80);
                    density = max(
                        disk * (0.20 + arms * 0.82) * cloud,
                        bar * 0.74) + core;
                    youngStars = disk * arms * cloud;
                }
                else if (type < 2.5)
                {
                    float ellipticalRadius = length(localPosition);
                    density = exp(-ellipticalRadius * 2.4) * edge +
                              Gaussian(ellipticalRadius, coreRadius) * 1.4;
                }
                else if (type < 3.5)
                {
                    density = disk * (0.70 + cloud * 0.26) + core * 1.1;
                }
                else if (type < 4.5)
                {
                    float clumps = SmoothCloud(localPosition * 1.45, 3.7);
                    density = Gaussian(diskRadius, 0.60) *
                              (0.24 + clumps * 0.80);
                    youngStars = density * clumps;
                }
                else if (type < 5.5)
                {
                    float ring = Gaussian(abs(diskRadius - ringRadius),
                                          ringWidth);
                    density = ring * (0.72 + cloud * 0.45) * edge +
                              disk * 0.10 + core * 0.58;
                    youngStars = ring * cloud;
                }
                else
                {
                    float clumps = SmoothCloud(localPosition * 1.33, 6.2);
                    density = lerp(
                        disk + core,
                        Gaussian(diskRadius, 0.66) *
                            (0.22 + clumps * 0.85),
                        0.62);
                    youngStars = density * clumps * 0.55;
                }

                float fade = saturate(input.galaxyColor.a);
                density = saturate(density) * fade;
                youngStars = saturate(youngStars) * fade;

                float3 diskColor = input.galaxyColor.rgb;
                float3 coreColor = lerp(
                    diskColor,
                    float3(1.0, 0.72, 0.36),
                    type < 2.5 ? 0.74 : 0.42);
                float3 youngColor = lerp(
                    diskColor,
                    float3(0.25, 0.72, 1.0),
                    0.72);

                float3 color = diskColor * density * 0.72 +
                               coreColor * core * fade * 1.05 +
                               youngColor * youngStars * 0.36;
                float alpha = saturate(density * 0.80 +
                                       core * fade * 0.26);

                if (alpha < 0.002)
                    discard;

                // Premultiplied alpha for Blend One OneMinusSrcAlpha.
                return half4(color * alpha * 1.20, alpha);
            }
            ENDHLSL
        }
    }
}
