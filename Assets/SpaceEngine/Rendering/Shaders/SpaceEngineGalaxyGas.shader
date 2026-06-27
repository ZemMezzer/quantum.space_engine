Shader "SpaceEngine/Streaming/Galaxy Gas Volume"
{
    Properties
    {
        [HDR] _CoreColor ("Core Color", Color) = (1.0, 0.76, 0.42, 1.0)
        [HDR] _DiskColor ("Disk Color", Color) = (0.46, 0.56, 0.82, 1.0)
        [HDR] _NebulaColor ("Nebula Color", Color) = (0.20, 0.46, 1.0, 1.0)
        [HDR] _HaloColor ("Halo Color", Color) = (0.10, 0.18, 0.36, 1.0)
        _Brightness ("Brightness", Range(0, 8)) = 1
        _Opacity ("Opacity", Range(0, 4)) = 1.25
        _DustStrength ("Dust Strength", Range(0, 2)) = 0.9
        _RaymarchSteps ("Raymarch Steps", Range(8, 96)) = 24
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Background-100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "GalaxyGasVolume"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_RAYMARCH_STEPS 96

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _DiskColor;
                half4 _NebulaColor;
                half4 _HaloColor;
                float4 _CameraGalaxyPosition;
                float4x4 _WorldToGalaxyShape;
                float _RaymarchSteps;
                float _VolumeRadius;
                float _GalaxyType;
                float _CoreRadius;
                float _DiskThickness;
                float _DiskRadiusMultiplier;
                float _SpiralArmCount;
                float _SpiralArmTightness;
                float _BarLength;
                float _Ellipticity;
                float _RingRadius;
                float _RingWidth;
                float _Irregularity;
                float _GasDensity;
                float _Brightness;
                float _Opacity;
                float _DustStrength;
                float _Seed;
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

            struct GalaxyDensitySample
            {
                float density;
                float disk;
                float core;
                float nebula;
                float noise;
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float InterleavedGradientNoise(float2 pixelCoord)
            {
                float magic = dot(pixelCoord, float2(0.06711056, 0.00583715));
                return frac(52.9829189 * frac(magic));
            }

            float SmoothNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 fraction = frac(p);
                fraction = fraction * fraction * (3.0 - 2.0 * fraction);

                float n000 = Hash31(cell + float3(0.0, 0.0, 0.0));
                float n100 = Hash31(cell + float3(1.0, 0.0, 0.0));
                float n010 = Hash31(cell + float3(0.0, 1.0, 0.0));
                float n110 = Hash31(cell + float3(1.0, 1.0, 0.0));
                float n001 = Hash31(cell + float3(0.0, 0.0, 1.0));
                float n101 = Hash31(cell + float3(1.0, 0.0, 1.0));
                float n011 = Hash31(cell + float3(0.0, 1.0, 1.0));
                float n111 = Hash31(cell + float3(1.0, 1.0, 1.0));

                float n00 = lerp(n000, n100, fraction.x);
                float n10 = lerp(n010, n110, fraction.x);
                float n01 = lerp(n001, n101, fraction.x);
                float n11 = lerp(n011, n111, fraction.x);
                float n0 = lerp(n00, n10, fraction.y);
                float n1 = lerp(n01, n11, fraction.y);
                return lerp(n0, n1, fraction.z);
            }

            float Fbm3(float3 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                value += amplitude * SmoothNoise(p);
                p = p * 2.02 + float3(7.13, -5.71, 3.19);
                amplitude *= 0.5;
                value += amplitude * SmoothNoise(p);
                p = p * 2.02 + float3(-3.77, 4.61, 8.23);
                amplitude *= 0.5;
                value += amplitude * SmoothNoise(p);
                return value;
            }

            float GalaxyNoise(float3 position)
            {
                float3 warp = float3(
                    SmoothNoise(position * 1.15 + float3(13.1, 7.3, -5.4) + _Seed * 9.0),
                    SmoothNoise(position * 1.15 + float3(-3.8, 11.7, 4.2) - _Seed * 13.0),
                    SmoothNoise(position * 1.15 + float3(8.5, -9.4, 2.6) + _Seed * 17.0));

                warp = (warp - 0.5) * 0.10;
                float3 warpedPosition = position + warp;

                float macro = Fbm3(warpedPosition * 2.2 + _Seed * 11.0);
                float detail = Fbm3(warpedPosition * 4.8 - _Seed * 23.0);
                return saturate(macro * 0.72 + detail * 0.28);
            }

            float ExponentialFalloff(float distanceValue, float scale)
            {
                return exp(-max(0.0, distanceValue) / max(0.0001, scale));
            }

            float Gaussian(float distanceValue, float width)
            {
                float normalized = distanceValue / max(0.0001, width);
                return exp(-0.5 * normalized * normalized);
            }

            float SpiralArmFactor(float3 position, float planarRadius)
            {
                if (planarRadius < _CoreRadius)
                    return 0.0;

                float angle = atan2(position.z, position.x);
                float logarithmicRadius = log(max(1.0, planarRadius / max(0.0001, _CoreRadius)));
                float phase = angle - _SpiralArmTightness * logarithmicRadius;
                float wave = 0.5 + 0.5 * cos(phase * _SpiralArmCount);
                return wave * wave * wave;
            }

            GalaxyDensitySample EvaluateGalaxyDensity(float3 position)
            {
                GalaxyDensitySample densitySample;
                densitySample.density = 0.0;
                densitySample.disk = 0.0;
                densitySample.core = 0.0;
                densitySample.nebula = 0.0;
                densitySample.noise = 0.0;

                float planarRadius = length(position.xz);
                float sphericalRadius = length(position);
                float diskRadius = max(0.05, _DiskRadiusMultiplier);
                float diskThickness = max(0.0025, _DiskThickness);
                float core = Gaussian(sphericalRadius, _CoreRadius);
                float noise = GalaxyNoise(position);
                densitySample.noise = noise;

                if (_GalaxyType < 0.5)
                {
                    if (planarRadius > diskRadius)
                        return densitySample;

                    float disk = ExponentialFalloff(planarRadius, 0.42) *
                                 ExponentialFalloff(abs(position.y), diskThickness * 0.50);
                    float arms = SpiralArmFactor(position, planarRadius);
                    densitySample.disk = disk * (0.18 + arms * 0.82);
                    densitySample.core = core * 0.95;
                    densitySample.nebula = disk * arms * smoothstep(0.46, 0.82, noise);
                }
                else if (_GalaxyType < 1.5)
                {
                    if (planarRadius > diskRadius)
                        return densitySample;

                    float disk = ExponentialFalloff(planarRadius, 0.42) *
                                 ExponentialFalloff(abs(position.y), diskThickness * 0.50);
                    float arms = SpiralArmFactor(position, planarRadius);
                    float halfBarLength = max(0.005, _BarLength * 0.5);
                    float barThickness = max(0.003, _CoreRadius * 0.45);
                    float barDistance = length(float3(
                        position.x / halfBarLength,
                        position.z / barThickness,
                        position.y / diskThickness));
                    float bar = exp(-2.0 * barDistance * barDistance);
                    densitySample.disk = max(disk * (0.18 + arms * 0.82), bar);
                    densitySample.core = core;
                    densitySample.nebula = disk * arms * smoothstep(0.48, 0.84, noise);
                }
                else if (_GalaxyType < 2.5)
                {
                    float ellipsoidRadius = length(float3(
                        position.x,
                        position.y / max(0.05, _Ellipticity),
                        position.z));
                    if (ellipsoidRadius > diskRadius)
                        return densitySample;

                    densitySample.core = Gaussian(ellipsoidRadius, _CoreRadius) * 1.2;
                    densitySample.disk = ExponentialFalloff(ellipsoidRadius, 0.35) * 0.60;
                }
                else if (_GalaxyType < 3.5)
                {
                    if (planarRadius > diskRadius)
                        return densitySample;

                    densitySample.disk = ExponentialFalloff(planarRadius, 0.38) *
                                         ExponentialFalloff(abs(position.y), diskThickness * 0.45) * 0.85;
                    densitySample.core = Gaussian(sphericalRadius, _CoreRadius * 1.35);
                    densitySample.nebula = densitySample.disk * smoothstep(0.54, 0.90, noise) * 0.28;
                }
                else if (_GalaxyType < 4.5)
                {
                    if (sphericalRadius > diskRadius)
                        return densitySample;

                    float broadCloud = Gaussian(sphericalRadius, 0.55);
                    float clumps = smoothstep(0.52, 0.86, noise);
                    densitySample.disk = broadCloud * (0.18 + clumps * (0.80 + _Irregularity));
                    densitySample.core = core * 0.45;
                    densitySample.nebula = broadCloud * clumps;
                }
                else if (_GalaxyType < 5.5)
                {
                    if (planarRadius > diskRadius)
                        return densitySample;

                    float ring = Gaussian(abs(planarRadius - _RingRadius), _RingWidth) *
                                 ExponentialFalloff(abs(position.y), diskThickness * 0.5);
                    float disk = ExponentialFalloff(planarRadius, 0.45) *
                                 ExponentialFalloff(abs(position.y), diskThickness) * 0.12;
                    densitySample.disk = ring + disk;
                    densitySample.core = core * 0.55;
                    densitySample.nebula = ring * smoothstep(0.48, 0.82, noise);
                }
                else
                {
                    if (sphericalRadius > diskRadius)
                        return densitySample;

                    float ellipsoidRadius = length(float3(
                        position.x,
                        position.y / max(0.05, _Ellipticity),
                        position.z));
                    float elliptical = Gaussian(ellipsoidRadius, _CoreRadius) * 0.95 +
                                       ExponentialFalloff(ellipsoidRadius, 0.35) * 0.55;
                    float broadCloud = Gaussian(sphericalRadius, 0.55);
                    float clumps = smoothstep(0.50, 0.84, noise);
                    float irregular = broadCloud * (0.24 + clumps * (1.0 + _Irregularity));
                    densitySample.disk = lerp(elliptical, irregular, _Irregularity);
                    densitySample.core = core * (0.75 - _Irregularity * 0.25);
                    densitySample.nebula = irregular * clumps;
                }

                densitySample.density = saturate(densitySample.disk + densitySample.core);
                return densitySample;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy * 2.0, 0.0, 1.0);
                output.uv = input.uv;
                return output;
            }

            float3 GetGalaxyShapeRayDirection(float2 uv)
            {
                float2 ndc = uv * 2.0 - 1.0;
                float4 viewPosition = mul(UNITY_MATRIX_I_P, float4(ndc, 1.0, 1.0));
                viewPosition.xyz /= max(abs(viewPosition.w), 0.000001);
                float3 viewRay = normalize(viewPosition.xyz);
                float3 worldRay = normalize(mul((float3x3)UNITY_MATRIX_I_V, viewRay));
                return normalize(mul((float3x3)_WorldToGalaxyShape, worldRay));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 rayDirection = GetGalaxyShapeRayDirection(input.uv);
                float3 rayOrigin = _CameraGalaxyPosition.xyz;
                float radius = max(0.25, _VolumeRadius);
                float projection = dot(rayOrigin, rayDirection);
                float closestDistanceSquared = dot(rayOrigin, rayOrigin) - projection * projection;
                float radiusSquared = radius * radius;

                if (closestDistanceSquared >= radiusSquared)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float halfChord = sqrt(max(0.0, radiusSquared - closestDistanceSquared));
                float startDistance = max(0.0, -projection - halfChord);
                float endDistance = -projection + halfChord;

                if (endDistance <= startDistance)
                    return half4(0.0, 0.0, 0.0, 0.0);

                int stepCount = (int)clamp(floor(_RaymarchSteps + 0.5), 8.0, (float)MAX_RAYMARCH_STEPS);
                float stepSize = (endDistance - startDistance) / max(1.0, (float)stepCount);
                float jitter = InterleavedGradientNoise(input.positionCS.xy + _Seed * 4096.0) - 0.5;

                float3 accumulatedLight = 0.0;
                float transmittance = 1.0;

                [loop]
                for (int index = 0; index < MAX_RAYMARCH_STEPS; index++)
                {
                    if (index >= stepCount)
                        break;

                    float travel = startDistance + (index + 0.5 + jitter) * stepSize;
                    float3 samplePosition = rayOrigin + rayDirection * travel;
                    GalaxyDensitySample densitySample = EvaluateGalaxyDensity(samplePosition);

                    if (densitySample.density <= 0.00001)
                        continue;

                    float dust = 0.0;
                    if (densitySample.disk > 0.02)
                    {
                        float planarRadius = length(samplePosition.xz);
                        float dustBand = 0.5 + 0.5 * cos(
                            atan2(samplePosition.z, samplePosition.x) * max(1.0, _SpiralArmCount) -
                            planarRadius * (_SpiralArmTightness * 7.0 + 5.0));

                        dust = smoothstep(0.50, 0.88, densitySample.noise * 0.72 + dustBand * 0.48) *
                               _DustStrength * densitySample.disk * (0.55 + densitySample.core);
                    }

                    float attenuatedDensity = densitySample.density * (1.0 - dust * 0.82);
                    float extinction = attenuatedDensity * _Opacity * _GasDensity * stepSize * 3.0;
                    float sampleAlpha = 1.0 - exp(-extinction);

                    float3 color =
                        _DiskColor.rgb * densitySample.disk +
                        _CoreColor.rgb * densitySample.core * 1.25 +
                        _NebulaColor.rgb * densitySample.nebula * 0.62 +
                        _HaloColor.rgb * densitySample.density * 0.04;

                    accumulatedLight += transmittance * color * sampleAlpha * _Brightness;
                    transmittance *= 1.0 - sampleAlpha;

                    if (transmittance < 0.015)
                        break;
                }

                float alpha = saturate(1.0 - transmittance);
                return half4(accumulatedLight, alpha);
            }
            ENDHLSL
        }
    }
}
