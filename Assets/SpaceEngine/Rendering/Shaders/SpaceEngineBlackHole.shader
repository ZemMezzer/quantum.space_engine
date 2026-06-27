Shader "SpaceEngine/Streaming/Black Hole Unified"
{
    Properties
    {
        _BlackHoleCenterWorld ("Black Hole Center World", Vector) = (0,0,0,1)
        _BlackHoleRadiusWorld ("Black Hole Radius World", Float) = 1.0
        [HDR] _HorizonColor ("Horizon Color", Color) = (0,0,0,1)

        _LensEnabled ("Lens Enabled", Float) = 1.0
        _LensingStrength ("Lensing Strength", Range(0, 2)) = 0.95
        _LensingRadiusWorld ("Lensing Radius World", Float) = 4.0
        _LensCenterViewport ("Lens Center Viewport", Vector) = (0.5,0.5,0,0)
        _LensRadius ("Lens Radius", Float) = 0.14
        _HorizonRadius ("Horizon Radius", Float) = 0.05
        _LensEdgeSoftness ("Lens Edge Softness", Range(0.01, 0.5)) = 0.20
        [HDR] _LensRingColor ("Lens Ring Color", Color) = (0.78,0.88,1,1)
        _LensRingIntensity ("Lens Ring Intensity", Range(0,1)) = 0.16
        _SwirlStrength ("Swirl Strength", Range(0,12)) = 5.5
        _SwirlFalloff ("Swirl Falloff", Range(0.25,6)) = 1.75
        _SwirlDirection ("Swirl Direction", Range(-1,1)) = 1.0

        _HasAccretionDisk ("Has Accretion Disk", Float) = 1.0
        _DiskInnerRadiusWorld ("Disk Inner Radius World", Float) = 1.2
        _AccretionRadiusWorld ("Accretion Radius World", Float) = 10.0
        _DiskHalfThicknessWorld ("Disk Volume Half Thickness World", Float) = 1.08
        _DiskPlaneNormalWorld ("Disk Plane Normal World", Vector) = (0,1,0,0)
        _DiskPlaneRightWorld ("Disk Plane Right World", Vector) = (1,0,0,0)
        _DiskPlaneForwardWorld ("Disk Plane Forward World", Vector) = (0,0,1,0)
        _RaymarchSteps ("Unified Raymarch Steps", Range(8,64)) = 32
        _VolumeOpacity ("Volumetric Gas Opacity", Range(0,4)) = 4.0
        _VolumeBrightness ("Volumetric Emission Brightness", Range(0,8)) = 2.35
        _Cutoff ("Lensing Cutoff Value", Range(0.0001,0.05)) = 0.0025
        _Twist ("Accretion Disk Twist", Float) = 28.0
        _Temperature ("Accretion Disk Base Temperature", Range(0.1,4)) = 1.85
        _Speed ("Accretion Disk Animation Speed", Range(0,1)) = 0.12
        _Redshift ("Accretion Disk Redshift Effect", Range(0,1)) = 0.12
        _Seed ("Seed", Range(0,1)) = 0.0
        _SurfaceTime ("Surface Time", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+125"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "UnifiedBlackHoleComposite"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define MAX_UNIFIED_STEPS 64
            #define BH_TAU 6.28318530718

            CBUFFER_START(UnityPerMaterial)
                float4 _BlackHoleCenterWorld;
                float _BlackHoleRadiusWorld;
                float4 _HorizonColor;

                float _LensEnabled;
                float _LensingStrength;
                float _LensingRadiusWorld;
                float4 _LensCenterViewport;
                float _LensRadius;
                float _HorizonRadius;
                float _LensEdgeSoftness;
                float4 _LensRingColor;
                float _LensRingIntensity;
                float _SwirlStrength;
                float _SwirlFalloff;
                float _SwirlDirection;

                float _HasAccretionDisk;
                float _DiskInnerRadiusWorld;
                float _AccretionRadiusWorld;
                float _DiskHalfThicknessWorld;
                float4 _DiskPlaneNormalWorld;
                float4 _DiskPlaneRightWorld;
                float4 _DiskPlaneForwardWorld;
                float _RaymarchSteps;
                float _VolumeOpacity;
                float _VolumeBrightness;
                float _Cutoff;
                float _Twist;
                float _Temperature;
                float _Speed;
                float _Redshift;
                float _Seed;
                float _SurfaceTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            struct AccretionDensitySample
            {
                float density;
                float radial01;
                float turbulence;
                float hotPatches;
                float vertical01;
            };

            struct TraceResult
            {
                float3 radiance;
                float gasAlpha;
                float3 escapedDirection;
                float hitHorizon;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Render on a true fullscreen triangle in clip space. This
                // avoids edge clipping / cropping artifacts that can appear
                // when a camera-aligned proxy plane is not large enough after
                // perspective projection.
                output.positionCS = float4(input.positionOS.xy, 0.0, 1.0);
                return output;
            }

            float3 BlackHoleSafeNormalize(float3 value)
            {
                return value * rsqrt(max(dot(value, value), 1.0e-12));
            }

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

                [unroll(2)]
                for (int octave = 0; octave < 2; octave++)
                {
                    value += amplitude * SmoothNoise(p);
                    p = p * 2.02 + float3(
                        7.13 + octave * 3.19,
                        -5.71 + octave * 4.61,
                        3.19 + octave * 8.23);
                    amplitude *= 0.5;
                }

                return value;
            }

            float PeriodicFbm3(
                float3 samplePosition,
                float angularPhase,
                float angularPeriod)
            {
                float phase = saturate(angularPhase);
                float blend = phase * phase * (3.0 - 2.0 * phase);
                float first = Fbm3(samplePosition);
                float second = Fbm3(
                    samplePosition - float3(angularPeriod, 0.0, 0.0));
                return lerp(first, second, blend);
            }

            float3 GetAccretionTemperatureColour(float temperature)
            {
                float normalizedTemperature = saturate(
                    (max(temperature, 0.1) - 0.1) / 3.9);

                float3 deepRed = float3(0.72, 0.08, 0.02);
                float3 orange = float3(0.98, 0.34, 0.08);
                float3 yellow = float3(1.00, 0.68, 0.26);
                float3 white = float3(1.00, 0.90, 0.72);
                float3 blueWhite = float3(0.84, 0.90, 1.00);

                float3 colour = lerp(
                    deepRed,
                    orange,
                    smoothstep(0.02, 0.26, normalizedTemperature));
                colour = lerp(
                    colour,
                    yellow,
                    smoothstep(0.20, 0.55, normalizedTemperature));
                colour = lerp(
                    colour,
                    white,
                    smoothstep(0.48, 0.75, normalizedTemperature));
                return lerp(
                    colour,
                    blueWhite,
                    smoothstep(0.76, 1.00, normalizedTemperature));
            }

            float3 ToDiskLocal(float3 worldPosition, float3 centre)
            {
                float3 relativePosition = worldPosition - centre;
                return float3(
                    dot(relativePosition, _DiskPlaneRightWorld.xyz),
                    dot(relativePosition, _DiskPlaneNormalWorld.xyz),
                    dot(relativePosition, _DiskPlaneForwardWorld.xyz));
            }

            bool RaySphere(
                float3 rayOrigin,
                float3 rayDirection,
                float3 sphereCenter,
                float sphereRadius,
                out float tNear,
                out float tFar)
            {
                float3 toOrigin = rayOrigin - sphereCenter;
                float b = dot(toOrigin, rayDirection);
                float c = dot(toOrigin, toOrigin) - sphereRadius * sphereRadius;
                float discriminant = b * b - c;
                if (discriminant < 0.0)
                {
                    tNear = 0.0;
                    tFar = 0.0;
                    return false;
                }

                float root = sqrt(discriminant);
                tNear = -b - root;
                tFar = -b + root;
                return tFar > 0.0;
            }

            float SceneDistance(float2 screenUv, float3 cameraPosition)
            {
                float rawDepth = SampleSceneDepth(screenUv);

                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1.0e-5;
                #else
                    bool isSky = rawDepth >= 0.99999;
                #endif

                if (isSky)
                    return 1000000.0;

                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif

                float3 worldPosition = ComputeWorldSpacePosition(
                    screenUv,
                    rawDepth,
                    UNITY_MATRIX_I_VP);
                return distance(cameraPosition, worldPosition);
            }

            float2 ProjectDirectionToUV(
                float3 cameraPosition,
                float3 rayDirection,
                float2 fallbackUv)
            {
                float3 probePosition = cameraPosition + rayDirection;
                float4 clipPosition = mul(
                    UNITY_MATRIX_VP,
                    float4(probePosition, 1.0));
                if (clipPosition.w <= 1.0e-5)
                    return fallbackUv;

                float2 uv = clipPosition.xy / clipPosition.w;
                uv = uv * 0.5 + 0.5;
                return all(uv >= 0.0) && all(uv <= 1.0)
                    ? uv
                    : fallbackUv;
            }

            AccretionDensitySample EvaluateAccretionDensity(
                float3 samplePosition,
                float3 diskCentre)
            {
                AccretionDensitySample densitySample;
                densitySample.density = 0.0;
                densitySample.radial01 = 0.0;
                densitySample.turbulence = 0.0;
                densitySample.hotPatches = 0.0;
                densitySample.vertical01 = 0.0;

                if (_HasAccretionDisk <= 0.5)
                    return densitySample;

                float3 localPosition = ToDiskLocal(samplePosition, diskCentre);
                float innerRadius = max(
                    _DiskInnerRadiusWorld,
                    _BlackHoleRadiusWorld * 1.01);
                float outerRadius = max(
                    _AccretionRadiusWorld,
                    innerRadius + 0.001);
                float radialDistance = length(localPosition.xz);
                if (radialDistance <= innerRadius ||
                    radialDistance >= outerRadius)
                {
                    return densitySample;
                }

                float radial01 = saturate(
                    (radialDistance - innerRadius) /
                    max(outerRadius - innerRadius, 0.0001));
                float baseHalfThickness = max(
                    _DiskHalfThicknessWorld,
                    _BlackHoleRadiusWorld * 0.025);
                float localHalfThickness = baseHalfThickness *
                                           lerp(1.30, 0.62, radial01);
                float vertical01 = abs(localPosition.y) /
                                   max(localHalfThickness, 0.00001);
                if (vertical01 >= 1.0)
                    return densitySample;

                float angle = atan2(localPosition.z, localPosition.x);
                float time = _SurfaceTime * _Speed * 3.0;
                float orbitalSpeed = lerp(0.20, 1.40, 1.0 - radial01);
                float spiralTwist = _Twist * 0.018 *
                                    log(max(1.0, radialDistance / innerRadius));
                float animatedAngle = angle - time * orbitalSpeed + spiralTwist;
                float angularPhase = frac(animatedAngle / BH_TAU + 0.5);
                float angularPeriod = lerp(4.2, 10.5, radial01);

                float heightCoordinate = localPosition.y /
                                         max(localHalfThickness, 0.00001);
                float3 gasCoordinates = float3(
                    angularPhase * angularPeriod +
                    time * (0.12 + (1.0 - radial01) * 0.30),
                    radial01 * (6.5 + (1.0 - radial01) * 5.0),
                    heightCoordinate * 2.2);

                float3 warp = float3(
                    SmoothNoise(gasCoordinates * 1.25 +
                                float3(1.7, -2.1, 4.8) + _Seed * 9.0),
                    SmoothNoise(gasCoordinates * 1.25 +
                                float3(-3.4, 5.6, -1.9) - _Seed * 13.0),
                    SmoothNoise(gasCoordinates * 1.25 +
                                float3(6.1, -4.7, 2.8) + _Seed * 17.0));
                warp = (warp - 0.5) * float3(0.90, 0.24, 0.52);
                gasCoordinates += warp;

                float macro = PeriodicFbm3(
                    gasCoordinates * float3(1.35, 1.0, 1.0) +
                    float3(_Seed * 7.0, _Seed * 13.0, _Seed * 5.0),
                    angularPhase,
                    angularPeriod * 1.35);
                float medium = PeriodicFbm3(
                    gasCoordinates * float3(2.70, 2.15, 1.65) +
                    float3(5.7 - time * 0.10, -3.1, 2.6),
                    angularPhase,
                    angularPeriod * 2.70);
                float detail = PeriodicFbm3(
                    gasCoordinates * float3(5.10, 4.25, 3.80) +
                    float3(-7.9, 6.3 + time * 0.16, -4.4),
                    angularPhase,
                    angularPeriod * 5.10);

                float cloudDensity = macro * 0.52 +
                                     medium * 0.30 +
                                     detail * 0.18;
                cloudDensity = smoothstep(0.30, 0.92, cloudDensity);

                // Keep the large-scale structure spiral and cloudy rather
                // than a sequence of narrow radial lanes. Narrow lane bands
                // become visibly stretched by the bent ray when the camera
                // approaches the disk.
                float spiralPhase = animatedAngle * 4.0 +
                                    radial01 * (6.0 + _Twist * 0.12) -
                                    time * (0.55 + (1.0 - radial01) * 0.35) +
                                    (macro - 0.5) * 1.8 +
                                    (medium - 0.5) * 1.1;
                float spiral = 0.5 + 0.5 * sin(spiralPhase);
                float spiralStructure = lerp(
                    0.60,
                    1.0,
                    smoothstep(0.14, 0.86, spiral));

                float smokeWisps = smoothstep(
                    0.42,
                    0.94,
                    medium * 0.48 + detail * 0.52);
                float clumps = smoothstep(
                    0.56,
                    0.96,
                    macro * 0.50 + detail * 0.24 + medium * 0.26);
                float hotPatches = smoothstep(
                    0.60,
                    0.96,
                    clumps * 0.72 + spiralStructure * 0.28);

                float innerFade = smoothstep(0.0, 0.085, radial01);
                float outerFade = 1.0 - smoothstep(0.68, 1.0, radial01);
                outerFade = pow(saturate(outerFade), 1.08);
                float verticalFade = 1.0 - smoothstep(0.24, 1.0, vertical01);
                float turbulentVerticalFade = lerp(
                    verticalFade,
                    1.0 - vertical01,
                    smokeWisps * 0.34);

                densitySample.density = cloudDensity *
                                        spiralStructure *
                                        (0.68 + smokeWisps * 0.40) *
                                        (0.82 + clumps * 0.26) *
                                        innerFade *
                                        outerFade *
                                        turbulentVerticalFade;
                densitySample.density *= lerp(1.52, 1.04, radial01);
                densitySample.density *= lerp(1.08, 0.94, vertical01);
                densitySample.density *= 1.22;
                densitySample.radial01 = radial01;
                densitySample.turbulence = smokeWisps;
                densitySample.hotPatches = hotPatches;
                densitySample.vertical01 = vertical01;
                return densitySample;
            }

            float3 EvaluateAccretionEmission(
                AccretionDensitySample densitySample,
                float3 worldPosition,
                float3 rayDirection,
                float3 diskCentre)
            {
                float innerHeat = pow(
                    1.0 - densitySample.radial01,
                    0.32) * max(_Temperature, 0.0);
                float baseTemperature = max(_Temperature, 0.1);

                float3 outerColour = GetAccretionTemperatureColour(
                    baseTemperature * 0.38) * 0.95;
                float3 warmColour = GetAccretionTemperatureColour(
                    baseTemperature * 0.82) * 2.45;
                float3 hotColour = GetAccretionTemperatureColour(
                    baseTemperature * 1.45) * 5.60;
                float3 coreColour = GetAccretionTemperatureColour(
                    baseTemperature * 2.40) * 11.20;

                float3 colour = lerp(
                    outerColour,
                    warmColour,
                    smoothstep(0.0, 0.65, innerHeat));
                colour = lerp(
                    colour,
                    hotColour,
                    smoothstep(0.20, 0.95, innerHeat));
                colour = lerp(
                    colour,
                    coreColour,
                    smoothstep(0.55, 1.0, innerHeat) *
                    (0.55 + 0.45 * densitySample.hotPatches));
                colour = lerp(
                    colour,
                    warmColour * 0.82,
                    (1.0 - densitySample.turbulence) * 0.20);

                float3 localPosition = ToDiskLocal(worldPosition, diskCentre);
                float2 tangentCoordinates = BlackHoleSafeNormalize(
                    float3(-localPosition.z, 0.0, localPosition.x)).xz;
                float3 tangentWorld = BlackHoleSafeNormalize(
                    _DiskPlaneRightWorld.xyz * tangentCoordinates.x +
                    _DiskPlaneForwardWorld.xyz * tangentCoordinates.y);
                float doppler = saturate(
                    dot(tangentWorld, -rayDirection) * 0.5 + 0.5);
                float3 recedingTint = float3(0.92, 0.56, 0.22);
                float3 approachingTint = float3(1.12, 1.08, 1.02);
                colour *= lerp(
                    recedingTint,
                    approachingTint,
                    doppler * _Redshift);

                float outerDust = 1.0 - smoothstep(
                    0.12,
                    1.0,
                    densitySample.radial01);
                float3 dustTint = lerp(
                    float3(0.82, 0.54, 0.22),
                    float3(1.00, 0.92, 0.78),
                    pow(outerDust, 0.7));
                colour *= dustTint;

                float brightness = 0.52 + 2.70 * innerHeat;
                brightness *= 0.84 + densitySample.hotPatches * 0.68;
                brightness *= 0.78 + densitySample.turbulence * 0.36;
                brightness *= lerp(
                    1.0,
                    0.80,
                    densitySample.vertical01);
                brightness *= lerp(1.0, 1.35, densitySample.hotPatches);
                return colour * brightness;
            }

            TraceResult TraceBlackHole(
                float3 cameraPosition,
                float3 initialRayDirection,
                float3 centre,
                float traceRadius,
                float sceneDistance,
                float2 pixelPosition)
            {
                TraceResult result;
                result.radiance = 0.0;
                result.gasAlpha = 0.0;
                result.escapedDirection = initialRayDirection;
                result.hitHorizon = 0.0;

                float traceNear;
                float traceFar;
                if (!RaySphere(
                    cameraPosition,
                    initialRayDirection,
                    centre,
                    traceRadius,
                    traceNear,
                    traceFar))
                {
                    return result;
                }

                float startDistance = max(traceNear, 0.0);
                float endDistance = min(traceFar, sceneDistance);
                if (endDistance <= startDistance)
                    return result;

                float horizonNear;
                float horizonFar;
                bool directHorizonHit = RaySphere(
                    cameraPosition,
                    initialRayDirection,
                    centre,
                    max(_BlackHoleRadiusWorld, 0.0001),
                    horizonNear,
                    horizonFar) && horizonFar > 0.0;
                if (directHorizonHit)
                {
                    float horizonEntry = max(horizonNear, 0.0);
                    if (horizonEntry <= endDistance)
                    {
                        endDistance = horizonEntry;
                        result.hitHorizon = 1.0;
                    }
                }

                float rayDistance = endDistance - startDistance;
                float innerRadius = max(
                    _DiskInnerRadiusWorld,
                    _BlackHoleRadiusWorld * 1.01);
                float radialThickness = max(
                    _AccretionRadiusWorld - innerRadius,
                    _BlackHoleRadiusWorld * 0.1);
                float cameraDistanceToCentre = distance(
                    cameraPosition,
                    centre);
                float closeVolumeFactor = saturate(
                    1.0 -
                    (cameraDistanceToCentre - traceRadius * 0.55) /
                    max(traceRadius * 1.25, 0.0001));
                float adaptiveStepScale = lerp(
                    0.78,
                    1.0,
                    saturate(rayDistance /
                    max(traceRadius * 1.15, 0.0001)));
                if (closeVolumeFactor > 0.01)
                    adaptiveStepScale = 1.0;

                int requestedSteps = (int)clamp(
                    floor(_RaymarchSteps + 0.5),
                    8.0,
                    (float)MAX_UNIFIED_STEPS);
                if (_HasAccretionDisk > 0.5)
                {
                    int closeQualityFloor = (int)lerp(
                        20.0,
                        48.0,
                        closeVolumeFactor);
                    requestedSteps = max(requestedSteps, closeQualityFloor);
                }
                else
                {
                    requestedSteps = min(requestedSteps, 16);
                }

                int stepCount = (int)clamp(
                    floor(requestedSteps * adaptiveStepScale + 0.5),
                    8.0,
                    (float)MAX_UNIFIED_STEPS);
                float stepLength = rayDistance /
                                   max(1.0, (float)stepCount);
                float normalizedStep = stepLength /
                                       max(radialThickness, 0.0001);
                float jitter = InterleavedGradientNoise(
                    pixelPosition + _Seed * 4096.0) - 0.5;

                float3 rayDirection = initialRayDirection;
                float3 position = cameraPosition + rayDirection *
                                  (startDistance +
                                   (0.5 + jitter) * stepLength);
                float transmittance = 1.0;

                [loop]
                for (int index = 0; index < MAX_UNIFIED_STEPS; index++)
                {
                    if (index >= stepCount)
                        break;

                    float3 relative = position - centre;
                    float distanceToCentre = length(relative);
                    if (distanceToCentre <= _BlackHoleRadiusWorld * 1.001)
                    {
                        result.hitHorizon = 1.0;
                        break;
                    }

                    AccretionDensitySample densitySample =
                        EvaluateAccretionDensity(position, centre);
                    if (densitySample.density > 0.00001)
                    {
                        float extinction = densitySample.density *
                                           _VolumeOpacity *
                                           normalizedStep *
                                           24.0;
                        float sampleAlpha = 1.0 - exp(-extinction);
                        float3 emission = EvaluateAccretionEmission(
                            densitySample,
                            position,
                            rayDirection,
                            centre);

                        result.radiance += transmittance *
                                           emission *
                                           sampleAlpha *
                                           _VolumeBrightness;
                        transmittance *= 1.0 - sampleAlpha;
                        if (transmittance < 0.012)
                            break;
                    }

                    float inverseSquare = 1.0 / max(
                        distanceToCentre * distanceToCentre,
                        _BlackHoleRadiusWorld *
                        _BlackHoleRadiusWorld * 0.5);
                    float bend = min(
                        _LensingStrength *
                        _BlackHoleRadiusWorld *
                        stepLength * inverseSquare,
                        0.18);
                    float3 radialDirection = relative /
                                             max(distanceToCentre, 0.0001);
                    rayDirection = BlackHoleSafeNormalize(
                        rayDirection - radialDirection * bend);

                    // The disk follows the same radial inverse-square
                    // integration as the supplied reference. Tangential
                    // frame-dragging is deliberately not injected into the
                    // volumetric ray: it caused camera-distance-dependent
                    // twisting and amplified the sampling bands.
                    position += rayDirection * stepLength;
                }

                result.gasAlpha = saturate(
                    (1.0 - transmittance) * 1.95);
                result.escapedDirection = rayDirection;
                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (unity_OrthoParams.w != 0.0)
                    discard;

                float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 ndc = screenUv * 2.0 - 1.0;
                if (_ProjectionParams.x < 0.0)
                    ndc.y *= -1.0;

                float3 viewDirectionVS = BlackHoleSafeNormalize(float3(
                    ndc.x / UNITY_MATRIX_P[0][0],
                    ndc.y / UNITY_MATRIX_P[1][1],
                    -1.0));
                float3 initialRayDirection = BlackHoleSafeNormalize(
                    mul((float3x3)UNITY_MATRIX_I_V, viewDirectionVS));

                float3 cameraPosition = _WorldSpaceCameraPos.xyz;
                float3 centre = _BlackHoleCenterWorld.xyz;
                float eventHorizonRadius = max(_BlackHoleRadiusWorld, 0.0001);
                float diskInfluenceRadius = _HasAccretionDisk > 0.5
                    ? sqrt(
                        _AccretionRadiusWorld * _AccretionRadiusWorld +
                        _DiskHalfThicknessWorld * _DiskHalfThicknessWorld) *
                      1.12
                    : 0.0;
                float traceRadius = max(
                    max(_LensingRadiusWorld, diskInfluenceRadius),
                    eventHorizonRadius * 1.05);

                float traceNear;
                float traceFar;
                if (!RaySphere(
                    cameraPosition,
                    initialRayDirection,
                    centre,
                    traceRadius,
                    traceNear,
                    traceFar))
                {
                    discard;
                }

                float sceneDistance = SceneDistance(screenUv, cameraPosition);
                if (sceneDistance <= max(traceNear, 0.0))
                    discard;

                TraceResult trace = TraceBlackHole(
                    cameraPosition,
                    initialRayDirection,
                    centre,
                    traceRadius,
                    sceneDistance,
                    input.positionCS.xy);

                float lensNear;
                float lensFar;
                bool rayCrossesLens = _LensEnabled > 0.5 &&
                                      RaySphere(
                                          cameraPosition,
                                          initialRayDirection,
                                          centre,
                                          max(_LensingRadiusWorld,
                                              eventHorizonRadius * 1.05),
                                          lensNear,
                                          lensFar);
                float lensAlpha = 0.0;
                if (rayCrossesLens && lensFar > 0.0 && trace.hitHorizon < 0.5)
                {
                    float closestDistance = clamp(
                        -dot(cameraPosition - centre, initialRayDirection),
                        0.0,
                        max(lensFar, 0.0));
                    float3 closestPosition = cameraPosition +
                                             initialRayDirection *
                                             closestDistance;
                    float normalizedImpact = length(closestPosition - centre) /
                                             max(_LensingRadiusWorld, 0.0001);
                    float edgeStart = saturate(1.0 - _LensEdgeSoftness);
                    lensAlpha = 1.0 - smoothstep(
                        edgeStart,
                        1.0,
                        normalizedImpact);
                }

                float3 basePremultiplied = 0.0;
                float baseAlpha = 0.0;
                if (trace.hitHorizon > 0.5)
                {
                    basePremultiplied = _HorizonColor.rgb;
                    baseAlpha = 1.0;
                }
                else if (lensAlpha > 0.0001)
                {
                    float2 lensedUv = ProjectDirectionToUV(
                        cameraPosition,
                        trace.escapedDirection,
                        screenUv);
                    basePremultiplied = SampleSceneColor(lensedUv) * lensAlpha;
                    baseAlpha = lensAlpha;

                    float horizonRadius = max(_HorizonRadius, 0.000001);
                    float lensRadius = max(_LensRadius, horizonRadius + 0.000001);
                    float aspect = _ScaledScreenParams.x /
                                   max(_ScaledScreenParams.y, 1.0);
                    float2 localOffset =
                        (screenUv - _LensCenterViewport.xy) *
                        float2(aspect, 1.0);
                    float impact = max(
                        length(localOffset) / horizonRadius,
                        1.0001);
                    float photonRing = exp(-pow(
                        (impact - 1.12) / 0.11,
                        2.0));
                    basePremultiplied += _LensRingColor.rgb *
                                          photonRing *
                                          _LensRingIntensity *
                                          _LensingStrength *
                                          lensAlpha;
                }

                float combinedAlpha = trace.gasAlpha +
                                      baseAlpha *
                                      (1.0 - trace.gasAlpha);
                float3 combinedPremultiplied = trace.radiance +
                                                basePremultiplied *
                                                (1.0 - trace.gasAlpha);
                if (combinedAlpha <= 0.0001)
                    discard;

                return half4(combinedPremultiplied, saturate(combinedAlpha));
            }
            ENDHLSL
        }
    }
}
