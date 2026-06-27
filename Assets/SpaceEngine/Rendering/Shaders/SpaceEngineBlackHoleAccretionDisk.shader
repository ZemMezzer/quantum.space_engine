Shader "SpaceEngine/Streaming/Black Hole Accretion Disk"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 16)) = 2.85
        _RotationSpeed ("Rotation Speed", Range(0, 4)) = 0.85
        _DiskInnerRadius ("Disk Inner Radius", Range(0.1, 5)) = 1.18
        _DiskOuterRadius ("Disk Outer Radius", Range(2, 20)) = 20.0
        _DiskThickness ("Disk Half Thickness", Range(0.05, 3)) = 0.78
        _DiskFlare ("Disk Flare", Range(0, 2)) = 1.15
        _WarpStrength ("Warp Strength", Range(0, 1)) = 0.12
        _WarpFrequency ("Warp Frequency", Range(1, 5)) = 2.0
        _Twist ("Twist", Range(0, 64)) = 28.0
        _TurbulenceScale ("Turbulence Scale", Range(0.3, 3)) = 1.0
        _VolumeDensity ("Gas Density", Range(0.2, 3)) = 1.18
        _Redshift ("Doppler Strength", Range(0, 1.5)) = 0.2
        _Temperature ("Temperature", Range(0.1, 4.0)) = 1.85
        _WrapAmount ("Wrap Amount", Range(0, 2)) = 1.0
        [HDR] _InnerColor ("Inner Gas Color", Color) = (10.5, 9.2, 8.6, 1)
        [HDR] _MiddleColor ("Middle Gas Color", Color) = (3.6, 1.9, 0.62, 1)
        [HDR] _OuterColor ("Outer Gas Color", Color) = (0.55, 0.11, 0.028, 1)
        [HDR] _DustColor ("Dust Lane Color", Color) = (1.45, 0.42, 0.12, 1)
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
            Name "AccretionDiskVolume"
            Tags { "LightMode" = "UniversalForward" }

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
                float _Twist;
                float _TurbulenceScale;
                float _VolumeDensity;
                float _Redshift;
                float _Temperature;
                float _WrapAmount;
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
                float edgeOn : TEXCOORD6;
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

            float FractalNoise(float2 p)
            {
                float result = 0.0;
                float amplitude = 0.5;
                [unroll]
                for (int octave = 0; octave < 4; octave++)
                {
                    result += ValueNoise(p) * amplitude;
                    p = mul(float2x2(1.7, -1.2, 1.2, 1.7), p) * 1.55 + float2(3.1, 7.2);
                    amplitude *= 0.5;
                }
                return result / 0.9375;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionOS = input.positionOS.xyz;
                float radialDistance = max(length(positionOS.xz), 0.0001);
                float radial01 = saturate(
                    (radialDistance - _DiskInnerRadius) /
                    max(_DiskOuterRadius - _DiskInnerRadius, 0.0001));
                float angle = atan2(positionOS.z, positionOS.x);
                float verticalFraction = input.volumeUv.x;

                float3 baseWorld = TransformObjectToWorld(float3(positionOS.x, 0.0, positionOS.z));
                float3 diskNormalWS = normalize(TransformObjectToWorldDir(float3(0.0, 1.0, 0.0)));
                float3 viewDirection = normalize(_WorldSpaceCameraPos - baseWorld);
                float edgeOn = 1.0 - abs(dot(diskNormalWS, viewDirection));

                float innerBias = pow(1.0 - radial01, 1.85);
                float flare = lerp(0.45, 1.0, radial01) * _DiskFlare;
                float warp = sin(
                    angle * _WarpFrequency +
                    radial01 * 6.0 +
                    _Seed * 19.0) *
                    _WarpStrength * radial01 * radial01;
                float wrapLift = _WrapAmount * edgeOn * innerBias;
                float thickness = _DiskThickness * flare * (1.0 + innerBias * 0.95 + wrapLift * 1.4);

                positionOS.y = verticalFraction * thickness;
                positionOS.y += sign(verticalFraction) * wrapLift * 0.65 * (0.35 + 0.65 * (1.0 - abs(verticalFraction)));
                positionOS.y += warp;
                positionOS.xz *= 1.0 - wrapLift * innerBias * 0.035;

                float3 tangentOS = normalize(float3(-positionOS.z, 0.0, positionOS.x));

                output.positionCS = TransformObjectToHClip(positionOS);
                output.positionWS = TransformObjectToWorld(positionOS);
                output.tangentWS = TransformObjectToWorldDir(tangentOS);
                output.diskNormalWS = diskNormalWS;
                output.radialDistance = radialDistance;
                output.verticalFraction = verticalFraction;
                output.uv = input.uv;
                output.edgeOn = edgeOn;
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
                float swirl = angle + radial * radial * _Twist * 1.25 + time / (0.18 + radial * 0.95);
                float2 dir1 = float2(cos(swirl), sin(swirl));
                float2 dir2 = float2(cos(swirl * 2.7 + radial * 2.1), sin(swirl * 2.7 + radial * 2.1));
                float2 dir3 = float2(cos(swirl * 5.4 - radial * 3.8), sin(swirl * 5.4 - radial * 3.8));

                float macro = FractalNoise(dir1 * 2.4 + dir2 * 0.9 + float2(radial * 1.2 + _Seed * 7.0, radial * 1.8 + _Seed * 11.0));
                float bands = FractalNoise(dir1 * 8.5 + dir2 * 2.2 + float2(radial * 3.3 + macro * 1.25, radial * 5.8 + _Seed * 19.0));
                float wisps = FractalNoise(dir1 * 14.0 + dir2 * 3.5 + dir3 * 1.5 + float2(radial * 6.0 + bands * 1.1, radial * 10.5 + _Seed * 29.0));
                float flicker = FractalNoise(dir1 * 22.0 + dir3 * 2.5 + float2(radial * 10.0 - wisps * 0.8, radial * 15.0 + _Seed * 37.0));

                float gas = saturate(macro * 0.52 + bands * 0.32 + wisps * 0.20 - 0.18 - radial * 0.05);
                gas = smoothstep(0.18, 0.92, gas);
                float filaments = smoothstep(0.46, 0.92, bands * 0.62 + wisps * 0.38);
                float hotPatches = smoothstep(0.54, 0.94, macro * 0.55 + flicker * 0.45);

                float innerHeat = pow(1.0 - radial, 0.32) * max(_Temperature, 0.0);
                float outerFalloff = 1.0 - smoothstep(0.88, 1.0, radial);
                float innerFalloff = smoothstep(0.0, 0.035, radial);
                float verticalDensity = exp(-input.verticalFraction * input.verticalFraction * lerp(1.6, 4.8, radial));

                float3 colour = lerp(
                    _OuterColor.rgb,
                    _MiddleColor.rgb,
                    smoothstep(0.0, 0.70, innerHeat));
                colour = lerp(
                    colour,
                    _InnerColor.rgb,
                    smoothstep(0.24, 1.0, innerHeat));
                colour = lerp(colour, _DustColor.rgb, radial * radial * (0.08 + 0.16 * wisps));

                float3 viewDirection = normalize(_WorldSpaceCameraPos - input.positionWS);
                float approaching = saturate(dot(normalize(input.tangentWS), viewDirection) * 0.5 + 0.5);
                float3 recedingTint = float3(0.90, 0.60, 0.30);
                float3 approachingTint = float3(1.10, 1.08, 1.05);
                colour *= lerp(recedingTint, approachingTint, approaching * saturate(_Redshift));

                float photonWrap = input.edgeOn * pow(1.0 - radial, 1.7);
                colour = lerp(colour, _InnerColor.rgb, photonWrap * 0.25);
                colour = lerp(colour, float3(1.0, 0.99, 0.97) * max(_InnerColor.r, 1.0), photonWrap * 0.18 * hotPatches);

                float brightness = gas *
                                   (0.40 + 0.60 * filaments) *
                                   (0.55 + 0.45 * hotPatches) *
                                   (0.26 + 1.85 * innerHeat) *
                                   outerFalloff *
                                   innerFalloff *
                                   verticalDensity *
                                   _VolumeDensity *
                                   _Intensity;
                brightness *= lerp(0.92, 1.85, input.edgeOn);
                brightness += photonWrap * (0.25 + 1.8 * hotPatches) * verticalDensity;

                return half4(colour * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
