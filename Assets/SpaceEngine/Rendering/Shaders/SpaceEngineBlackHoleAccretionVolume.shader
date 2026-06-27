Shader "SpaceEngine/Streaming/Volumetric Black Hole Accretion Disk"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 12)) = 2.2
        _RotationSpeed ("Rotation Speed", Range(0, 4)) = 0.8
        _DiskInnerRadius ("Disk Inner Radius", Range(0.1, 5)) = 1.2
        _DiskOuterRadius ("Disk Outer Radius", Range(2, 20)) = 12.0
        _DiskThickness ("Disk Half Thickness", Range(0.05, 3)) = 0.65
        _DiskFlare ("Disk Flare", Range(0, 2)) = 0.8
        _WarpStrength ("Warp Strength", Range(0, 2)) = 0.2
        _WarpFrequency ("Warp Frequency", Range(1, 5)) = 2.0
        _SpiralStrength ("Spiral Strength", Range(0, 1)) = 0.5
        _TurbulenceScale ("Turbulence Scale", Range(0.3, 3)) = 1.0
        _VolumeDensity ("Gas Density", Range(0.2, 3)) = 1.0
        _DopplerStrength ("Doppler Strength", Range(0, 1.5)) = 0.8
        _RelativisticLift ("Relativistic Lift", Range(0, 4)) = 1.8
        _RelativisticWrap ("Relativistic Wrap", Range(0, 2)) = 0.95
        _RelativisticCompression ("Relativistic Compression", Range(0, 1)) = 0.24
        _GasSoftness ("Gas Softness", Range(0.5, 3)) = 1.75
        _CloudContrast ("Cloud Contrast", Range(0, 2)) = 0.65
        [HDR] _InnerColor ("Inner Gas Color", Color) = (6.2, 4.35, 1.54, 1)
        [HDR] _MiddleColor ("Middle Gas Color", Color) = (2.16, 0.58, 0.075, 1)
        [HDR] _OuterColor ("Outer Gas Color", Color) = (0.44, 0.018, 0.003, 1)
        [HDR] _DustColor ("Dust Lane Color", Color) = (0.94, 0.17, 0.012, 1)
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
            Name "VolumetricAccretionDisk"
            Tags { "LightMode" = "UniversalForward" }

            // The mesh contains several thin gas sheets. Additive blending
            // lets their overlapping density read as a continuous luminous
            // volume without storing or ray marching a heavy 3D texture.
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _RotationSpeed;
                float _DiskInnerRadius;
                float _DiskOuterRadius;
                float _DiskThickness;
                float _DiskFlare;
                float _WarpStrength;
                float _WarpFrequency;
                float _SpiralStrength;
                float _TurbulenceScale;
                float _VolumeDensity;
                float _DopplerStrength;
                float _RelativisticLift;
                float _RelativisticWrap;
                float _RelativisticCompression;
                float _GasSoftness;
                float _CloudContrast;
                half4 _InnerColor;
                half4 _MiddleColor;
                half4 _OuterColor;
                half4 _DustColor;
                float _Seed;
                float _SurfaceTime;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 volumeUv : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 tangentWS : TEXCOORD1;
                float3 diskNormalWS : TEXCOORD2;
                float radialDistance : TEXCOORD3;
                float verticalFraction : TEXCOORD4;
                float2 uv : TEXCOORD5;
                float innerBand : TEXCOORD6;
                float nearSide : TEXCOORD7;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);

                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));

                return lerp(
                    lerp(a, b, local.x),
                    lerp(c, d, local.x),
                    local.y);
            }

            float Fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;

                [unroll]
                for (int octave = 0; octave < 4; octave++)
                {
                    value += ValueNoise(p) * amplitude;
                    p = p * 2.03 + float2(17.1, 9.2);
                    amplitude *= 0.5;
                }

                return value / 0.9375;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionOS = input.positionOS.xyz;
                float radialDistance = max(length(positionOS.xz), 0.0001);
                float angle = atan2(positionOS.z, positionOS.x);
                float radial01 = saturate(
                    (radialDistance - _DiskInnerRadius) /
                    max(_DiskOuterRadius - _DiskInnerRadius, 0.0001));
                float verticalFraction = input.volumeUv.x;

                float flare = lerp(0.32, 1.0, radial01) * _DiskFlare;
                float warp = sin(
                    angle * _WarpFrequency +
                    radial01 * 7.0 +
                    _Seed * 18.0) *
                    _WarpStrength * radial01 * radial01;
                float secondaryWarp = sin(
                    angle * (_WarpFrequency + 1.0) -
                    radial01 * 11.0 +
                    _Seed * 33.0) *
                    _WarpStrength * 0.20 * radial01;

                float radialWobble = sin(
                    angle * (3.0 + _WarpFrequency) +
                    radial01 * 14.0 +
                    _Seed * 49.0) *
                    _SpiralStrength * 0.035 * radial01;

                float3 cameraOS = TransformWorldToObject(_WorldSpaceCameraPos);
                float2 cameraPlaneDirection = normalize(cameraOS.xz + float2(0.0001, 0.0001));
                float2 radialDirection = normalize(positionOS.xz + float2(0.0001, 0.0001));
                float nearSide = saturate(dot(radialDirection, cameraPlaneDirection) * 0.5 + 0.5);
                float innerBand = pow(1.0 - radial01, 1.45);
                float wrapBand = smoothstep(0.0, 0.55, innerBand);
                float layerAbs = abs(verticalFraction);
                float verticalShell = lerp(1.0, 0.62, layerAbs);
                float relativisticLift = _RelativisticLift * wrapBand *
                                         lerp(0.35, 1.0, nearSide) *
                                         verticalShell;
                float relativisticArch = _RelativisticWrap * wrapBand *
                                         (0.32 + 0.68 * nearSide);
                float radialCompression = _RelativisticCompression * wrapBand *
                                          (0.20 + 0.80 * nearSide);

                positionOS.xz *= (1.0 + radialWobble) * (1.0 - radialCompression);
                positionOS.y = verticalFraction *
                               _DiskThickness *
                               flare +
                               warp +
                               secondaryWarp;
                positionOS.y += sign(verticalFraction + 0.0001) * relativisticLift;
                positionOS.y += relativisticArch * (0.5 - abs(radialDirection.y));

                float3 tangentOS = normalize(float3(
                    -positionOS.z,
                    0.0,
                    positionOS.x));

                output.positionCS = TransformObjectToHClip(positionOS);
                output.positionWS = TransformObjectToWorld(positionOS);
                output.tangentWS = TransformObjectToWorldDir(tangentOS);
                output.diskNormalWS = TransformObjectToWorldDir(
                    float3(0.0, 1.0, 0.0));
                output.radialDistance = radialDistance;
                output.verticalFraction = verticalFraction;
                output.uv = input.uv;
                output.innerBand = innerBand;
                output.nearSide = nearSide;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.radialDistance < _DiskInnerRadius ||
                    input.radialDistance > _DiskOuterRadius)
                {
                    discard;
                }

                float radial = saturate(
                    (input.radialDistance - _DiskInnerRadius) /
                    max(_DiskOuterRadius - _DiskInnerRadius, 0.0001));
                float angle = input.uv.x * 6.28318530718;
                float time = _SurfaceTime * _RotationSpeed;
                float angularFlow = angle + time / (0.12 + radial * 1.48);

                float broadNoise = Fbm(float2(
                    angularFlow * 2.6 * _TurbulenceScale,
                    radial * 5.4 + _Seed * 19.0));
                float mediumNoise = Fbm(float2(
                    angularFlow * 7.6 * _TurbulenceScale + time * 0.12,
                    radial * 13.5 + _Seed * 37.0));
                float fineNoise = Fbm(float2(
                    angularFlow * 21.0 * _TurbulenceScale + time * 0.33,
                    radial * 32.0 + _Seed * 53.0));

                float cloudShape = saturate(
                    broadNoise * 0.52 +
                    mediumNoise * 0.34 +
                    fineNoise * 0.24 -
                    (0.34 + radial * 0.08));
                cloudShape = pow(cloudShape, lerp(1.45, 0.78, _CloudContrast));

                float wisps = smoothstep(
                    0.12,
                    0.92,
                    mediumNoise * 0.56 + fineNoise * 0.44);
                float filamentMask = lerp(
                    1.0,
                    0.58 + 0.42 * wisps,
                    0.45 + _CloudContrast * 0.25);

                float spiralBands = 0.60 + 0.40 * sin(
                    angularFlow * (8.0 + radial * 6.0) -
                    radial * 18.0 +
                    fineNoise * 2.8);
                spiralBands = lerp(1.0, spiralBands, _SpiralStrength * 0.55);

                float innerHeat = pow(1.0 - radial, 0.42);
                float outerFalloff = 1.0 - smoothstep(0.72, 1.0, radial);
                float innerFalloff = smoothstep(0.0, 0.07, radial);
                float verticalDensity = exp(-
                    input.verticalFraction * input.verticalFraction *
                    lerp(0.95, 2.65, radial) / max(_GasSoftness, 0.001));
                float fluffyEnvelope = pow(1.0 - radial, 0.22) *
                                       pow(verticalDensity, 0.65);

                float3 colour = lerp(
                    _OuterColor.rgb,
                    _MiddleColor.rgb,
                    smoothstep(0.0, 0.68, innerHeat));
                colour = lerp(
                    colour,
                    _InnerColor.rgb,
                    smoothstep(0.34, 1.0, innerHeat));

                float dustAmount = radial * radial *
                                   (0.12 + 0.36 * fineNoise);
                colour = lerp(colour, _DustColor.rgb, dustAmount);

                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);
                float approaching = saturate(
                    dot(normalize(input.tangentWS), viewDirection) *
                    0.5 + 0.5);
                float3 recedingTint = float3(0.70, 0.24, 0.14);
                float3 approachingTint = float3(1.18, 1.35, 1.60);
                colour *= lerp(
                    recedingTint,
                    approachingTint,
                    approaching * _DopplerStrength);

                float edgeOn = 1.0 - abs(dot(
                    normalize(input.diskNormalWS),
                    viewDirection));
                float frontWrapGlow = (0.46 + 0.54 * input.nearSide) *
                                      pow(input.innerBand, 0.58);
                colour = lerp(
                    colour,
                    _InnerColor.rgb * 1.08,
                    0.28 * frontWrapGlow);

                float layerWeight = 0.07 + verticalDensity * 0.18;
                float brightness =
                    cloudShape *
                    filamentMask *
                    (0.55 + 0.45 * spiralBands) *
                    (0.26 + 1.22 * innerHeat) *
                    outerFalloff *
                    innerFalloff *
                    fluffyEnvelope *
                    layerWeight *
                    _VolumeDensity *
                    _Intensity;
                brightness *= lerp(0.96, 1.85, edgeOn);
                brightness *= 1.0 + frontWrapGlow * 0.82;

                return half4(colour * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
