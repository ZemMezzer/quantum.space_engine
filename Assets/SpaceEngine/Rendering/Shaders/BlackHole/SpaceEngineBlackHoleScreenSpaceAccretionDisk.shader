Shader "SpaceEngine/Streaming/Black Hole Screen Space Accretion Disk"
{
    Properties
    {
        _BlackHoleCenterWorld ("Black Hole Center World", Vector) = (0,0,0,1)
        _BlackHoleRadiusWorld ("Black Hole Radius World", Float) = 1.0
        _DiskInnerRadiusWorld ("Disk Inner Radius World", Float) = 1.2
        _AccretionRadiusWorld ("Accretion Radius World", Float) = 10.0
        _DiskPlaneNormalWorld ("Disk Plane Normal World", Vector) = (0,1,0,0)
        _DiskPlaneRightWorld ("Disk Plane Right World", Vector) = (1,0,0,0)
        _DiskPlaneForwardWorld ("Disk Plane Forward World", Vector) = (0,0,1,0)
        _Cutoff ("Lensing Cutoff Value", Range(0.0001, 0.05)) = 0.0025
        _Twist ("Accretion Disk Twist", Float) = 28.0
        _Temperature ("Accretion Disk Base Temperature", Range(0.1, 4.0)) = 1.85
        _Speed ("Accretion Disk Animation Speed", Range(0, 1)) = 0.12
        _Redshift ("Accretion Disk Redshift Effect", Range(0, 1)) = 0.12
        _Seed ("Seed", Range(0, 1)) = 0.0
        _SurfaceTime ("Surface Time", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "ScreenSpaceAccretionDisk"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BlackHoleCenterWorld;
                float _BlackHoleRadiusWorld;
                float _DiskInnerRadiusWorld;
                float _AccretionRadiusWorld;
                float4 _DiskPlaneNormalWorld;
                float4 _DiskPlaneRightWorld;
                float4 _DiskPlaneForwardWorld;
                float _Cutoff;
                float _Twist;
                float _Temperature;
                float _Speed;
                float _Redshift;
                float _Seed;
                float _SurfaceTime;
            CBUFFER_END

            static const float BH_TAU = 6.28318530718;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float FractalNoise(float2 p)
            {
                float result = 0.0;
                float amplitude = 0.5;
                [unroll(5)]
                for (int octave = 0; octave < 5; octave++)
                {
                    result += ValueNoise(p) * amplitude;
                    p = mul(float2x2(1.7, -1.2, 1.2, 1.7), p) * 1.55;
                    amplitude *= 0.5;
                }
                return result;
            }

            float PeriodicFractalNoise(
                float2 samplePosition,
                float angularPhase,
                float angularPeriod)
            {
                // At phase 0 and 1 the two samples refer to the same point
                // on the ring. The smooth blend makes the procedural gas
                // tile cleanly around the complete circumference.
                float phase = saturate(angularPhase);
                float blend = phase * phase * (3.0 - 2.0 * phase);
                float first = FractalNoise(samplePosition);
                float second = FractalNoise(
                    samplePosition - float2(angularPeriod, 0.0));
                return lerp(first, second, blend);
            }

            float3 RotateAroundAxis(float3 v, float3 axis, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
            }

            float3 GetAccretionTemperatureColour(float temperature)
            {
                float normalizedTemperature = saturate(
                    (max(temperature, 0.1) - 0.1) / 3.9);

                float3 deepRed = float3(1.00, 0.035, 0.004);
                float3 orange = float3(1.00, 0.32, 0.018);
                float3 yellow = float3(1.00, 0.74, 0.22);
                float3 white = float3(1.00, 0.94, 0.78);
                float3 blueWhite = float3(0.72, 0.84, 1.00);

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
                colour = lerp(
                    colour,
                    blueWhite,
                    smoothstep(0.76, 1.00, normalizedTemperature));
                return colour;
            }

            bool IntersectDiskPlane(
                float3 rayOrigin,
                float3 rayDirection,
                float3 diskCentre,
                float3 diskNormal,
                out float2 diskCoordinates,
                out float hitDistance)
            {
                diskCoordinates = 0.0;
                hitDistance = 0.0;
                float denom = dot(rayDirection, diskNormal);
                if (abs(denom) <= 0.00001)
                    return false;
                hitDistance = dot(diskCentre - rayOrigin, diskNormal) / denom;
                if (hitDistance <= 0.0)
                    return false;
                float3 hitPos = rayOrigin + rayDirection * hitDistance;
                float3 rel = hitPos - diskCentre;
                diskCoordinates = float2(dot(rel, _DiskPlaneRightWorld.xyz), dot(rel, _DiskPlaneForwardWorld.xyz));
                return true;
            }

            half4 SampleAccretionGas(float2 diskCoordinates, float3 rayDirection, float3 diskNormal, float frontWrap)
            {
                float innerRadius = max(_DiskInnerRadiusWorld, _BlackHoleRadiusWorld * 1.01);
                float outerRadius = max(_AccretionRadiusWorld, innerRadius + 0.001);
                float radialDistance = length(diskCoordinates);
                if (radialDistance < innerRadius || radialDistance > outerRadius)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float radial01 = saturate((radialDistance - innerRadius) / max(outerRadius - innerRadius, 0.0001));
                float angle = atan2(diskCoordinates.y, diskCoordinates.x);
                float angularPeriod = 4.0 + radial01 * 6.0;
                float2 normalizedDisk = diskCoordinates / max(outerRadius, 0.0001);

                // Make the disk visibly rotate from simulation time.
                // Inner regions move faster than outer regions, which gives
                // a clearer orbital feel without breaking the seamless tiling.
                float time = _SurfaceTime * _Speed * 3.0;
                float localAngularSpeed = lerp(0.22, 1.35, 1.0 - radial01);
                float spinAngle = -time * localAngularSpeed;
                float animatedAngle = angle + spinAngle;
                float angularPhase = frac(animatedAngle / BH_TAU + 0.5);

                float spinSin = sin(spinAngle);
                float spinCos = cos(spinAngle);
                float2 rotatedDisk = float2(
                    normalizedDisk.x * spinCos - normalizedDisk.y * spinSin,
                    normalizedDisk.x * spinSin + normalizedDisk.y * spinCos);

                // This offset uses the rotated sampling frame, so the gas
                // visibly orbits instead of only shimmering in place.
                float2 warpOffset = float2(
                    FractalNoise(rotatedDisk * 2.2 + float2(1.7 + _Seed * 3.0, -2.1 - _Seed * 5.0)),
                    FractalNoise(rotatedDisk.yx * 2.6 + float2(-3.4 - _Seed * 7.0, 4.8 + _Seed * 11.0)));
                warpOffset = (warpOffset - 0.5) * float2(0.18, 0.08);

                float2 flowUv = float2(
                    angularPhase * angularPeriod + time * (0.12 + (1.0 - radial01) * 0.32),
                    radial01 * (6.0 + (1.0 - radial01) * 5.0));
                flowUv += warpOffset;

                float2 macroUv = flowUv * 1.35 + float2(_Seed * 7.0, _Seed * 13.0);
                float macro = PeriodicFractalNoise(
                    macroUv,
                    angularPhase,
                    angularPeriod * 1.35);

                float2 mediumUv = flowUv * 2.70 + float2(5.7 - time * 0.10, -3.1 + macro * 0.8);
                float medium = PeriodicFractalNoise(
                    mediumUv,
                    angularPhase,
                    angularPeriod * 2.70);

                float2 detailUv = flowUv * 5.20 + float2(-7.9 + medium * 1.4, 6.3 + time * 0.16);
                float detail = PeriodicFractalNoise(
                    detailUv,
                    angularPhase,
                    angularPeriod * 5.20);

                float2 wispsUv = float2(
                    flowUv.x * 7.5 + detail * 1.8,
                    flowUv.y * 3.2 - macro * 1.2 + time * 0.08);
                float wisps = PeriodicFractalNoise(
                    wispsUv,
                    angularPhase,
                    angularPeriod * 7.5);

                float cloudDensity = macro * 0.52 + medium * 0.28 + detail * 0.20;
                cloudDensity = smoothstep(0.24, 0.92, cloudDensity);

                float laneCoord = radial01 * 74.0 + (macro - 0.5) * 6.5 + (medium - 0.5) * 3.2;
                float laneA = 0.5 + 0.5 * sin(laneCoord);
                float laneB = 0.5 + 0.5 * sin(laneCoord * 1.75 + 1.2 + detail * 1.5);
                float laneC = 0.5 + 0.5 * sin(laneCoord * 0.65 - 0.9 + macro * 1.8);
                float ringLanes = laneA * 0.44 + laneB * 0.36 + laneC * 0.20;
                ringLanes = smoothstep(0.18, 0.96, ringLanes);
                ringLanes = lerp(0.60, 1.0, ringLanes);

                float smokeWisps = smoothstep(0.40, 0.95, medium * 0.45 + wisps * 0.55);
                float clumps = smoothstep(0.56, 0.96, macro * 0.50 + detail * 0.20 + wisps * 0.30);
                float hotPatches = smoothstep(0.62, 0.98, clumps * 0.55 + ringLanes * 0.45);

                float innerHeat = pow(1.0 - radial01, 0.32) * max(_Temperature, 0.0);
                float outerFalloff = 1.0 - smoothstep(0.84, 1.0, radial01);
                float innerFalloff = smoothstep(0.0, 0.028, radial01);

                float baseTemperature = max(_Temperature, 0.1);
                float3 outerColour = GetAccretionTemperatureColour(
                    baseTemperature * 0.38) * 0.30;
                float3 warmColour = GetAccretionTemperatureColour(
                    baseTemperature * 0.82) * 1.20;
                float3 hotColour = GetAccretionTemperatureColour(
                    baseTemperature * 1.45) * 3.10;
                float3 coreColour = GetAccretionTemperatureColour(
                    baseTemperature * 2.40) * 6.00;

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
                    (0.55 + 0.45 * hotPatches));
                colour = lerp(
                    colour,
                    warmColour * 0.82,
                    (1.0 - cloudDensity) * 0.22);

                float2 tangentCoords = normalize(float2(-diskCoordinates.y, diskCoordinates.x));
                float3 tangentWorld = normalize(_DiskPlaneRightWorld.xyz * tangentCoords.x + _DiskPlaneForwardWorld.xyz * tangentCoords.y);
                float doppler = saturate(dot(tangentWorld, -rayDirection) * 0.5 + 0.5);
                float3 recedingTint = float3(0.92, 0.56, 0.22);
                float3 approachingTint = float3(1.12, 1.08, 1.02);
                colour *= lerp(recedingTint, approachingTint, doppler * _Redshift);
                colour = lerp(
                    colour,
                    coreColour,
                    frontWrap * 0.28 + hotPatches * frontWrap * 0.25);

                float density = cloudDensity * ringLanes;
                density *= (0.62 + 0.38 * smokeWisps);
                density *= (0.76 + 0.24 * clumps);

                float brightness = density * (0.20 + 2.15 * innerHeat) * outerFalloff * innerFalloff;
                brightness *= 0.90 + hotPatches * 0.55;
                brightness *= 1.0 + frontWrap * 0.55;
                brightness += frontWrap * hotPatches * pow(1.0 - radial01, 1.8) * 0.65;

                float alpha = saturate(
                    density * (0.68 + 0.52 * innerHeat) *
                    outerFalloff * innerFalloff +
                    hotPatches * (0.20 + 0.12 * innerHeat));
                alpha = max(alpha, frontWrap * hotPatches * 0.22);
                alpha = clamp(alpha, 0.2, 0.7);
                return half4(colour * brightness, alpha);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (unity_OrthoParams.w != 0.0)
                    return half4(0,0,0,0);

                float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 ndc = screenUv * 2.0 - 1.0;
                if (_ProjectionParams.x < 0.0)
                    ndc.y *= -1.0;
                float3 viewDirectionVS = normalize(float3(ndc.x / UNITY_MATRIX_P[0][0], ndc.y / UNITY_MATRIX_P[1][1], -1.0));
                float3 viewDirectionWS = normalize(mul((float3x3)UNITY_MATRIX_I_V, viewDirectionVS));

                float3 cameraPos = _WorldSpaceCameraPos.xyz;
                float3 centre = _BlackHoleCenterWorld.xyz;
                float radius = max(_BlackHoleRadiusWorld, 0.0001);
                float3 toCentre = centre - cameraPos;
                float centreDistance = dot(toCentre, viewDirectionWS);
                if (centreDistance <= 0.0)
                    discard;

                float3 closestPos = cameraPos + viewDirectionWS * centreDistance;
                float impactDistance = length(closestPos - centre);
                float outerCutoff = _AccretionRadiusWorld * 1.65;
                if (impactDistance <= radius * 1.001 || impactDistance > outerCutoff)
                    discard;

                float impactParameter = impactDistance * impactDistance / (radius * radius) - 1.0;
                impactParameter = max(impactParameter, 0.001);
                float lensAngle = 0.65 / (impactParameter + 0.05);
                lensAngle = min(lensAngle, 1.45);
                float lensFade = saturate(1.0 - impactDistance / max(outerCutoff, 0.0001));
                lensFade *= saturate(lensAngle / max(_Cutoff, 0.0001));

                float3 lensAxis = cross(viewDirectionWS, normalize(centre - closestPos));
                if (dot(lensAxis, lensAxis) <= 0.0000001)
                    lensAxis = _DiskPlaneNormalWorld.xyz;
                else
                    lensAxis = normalize(lensAxis);

                float3 deflectedDir = RotateAroundAxis(viewDirectionWS, lensAxis, lensAngle * lensFade);
                float3 diskNormal = normalize(_DiskPlaneNormalWorld.xyz);

                float2 coordsLensed = 0.0;
                float2 coordsDirect = 0.0;
                float distLensed = 0.0;
                float distDirect = 0.0;
                bool hasLensed = IntersectDiskPlane(cameraPos, deflectedDir, centre, diskNormal, coordsLensed, distLensed);
                bool hasDirect = IntersectDiskPlane(cameraPos, viewDirectionWS, centre, diskNormal, coordsDirect, distDirect);
                if (!hasLensed && !hasDirect)
                    discard;

                float transitionWidth = max(radius * 2.6, 0.0001);
                float frontBlend = hasDirect ? smoothstep(
                    -transitionWidth,
                    transitionWidth,
                    centreDistance - distDirect) : 0.0;
                float rearBlend = hasLensed ? smoothstep(
                    -transitionWidth,
                    transitionWidth,
                    distLensed - centreDistance) : 0.0;

                float blendSum = max(frontBlend + rearBlend, 0.0001);
                frontBlend /= blendSum;
                rearBlend /= blendSum;

                half4 frontSample = half4(0.0, 0.0, 0.0, 0.0);
                half4 rearSample = half4(0.0, 0.0, 0.0, 0.0);

                if (hasDirect)
                    frontSample = SampleAccretionGas(coordsDirect, viewDirectionWS, diskNormal, 0.0);

                if (hasLensed)
                    rearSample = SampleAccretionGas(
                        coordsLensed,
                        deflectedDir,
                        diskNormal,
                        lensFade * rearBlend);

                float3 combinedColor = lerp(
                    rearSample.rgb,
                    frontSample.rgb,
                    frontBlend);
                float combinedAlpha = lerp(
                    rearSample.a,
                    frontSample.a,
                    frontBlend);

                if (combinedAlpha <= 0.0001)
                    discard;

                return half4(combinedColor, combinedAlpha);
            }
            ENDHLSL
        }
    }
}
