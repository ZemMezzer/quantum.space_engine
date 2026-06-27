Shader "SpaceEngine/Streaming/Black Hole Accretion Disk"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 12)) = 1.8
        _RotationSpeed ("Rotation Speed", Range(0, 4)) = 0.8
        _DiskInnerRadius ("Disk Inner Radius", Range(0.1, 5)) = 1.2
        _DiskOuterRadius ("Disk Outer Radius", Range(2, 20)) = 12.0
        _DiskThickness ("Disk Half Thickness", Range(0.05, 3)) = 0.5
        _DiskFlare ("Disk Flare", Range(0, 2)) = 0.8
        _WarpStrength ("Warp Strength", Range(0, 2)) = 0.2
        _WarpFrequency ("Warp Frequency", Range(1, 5)) = 2.0
        _SpiralStrength ("Spiral Strength", Range(0, 1)) = 0.5
        _TurbulenceScale ("Turbulence Scale", Range(0.3, 3)) = 1.0
        _VolumeDensity ("Gas Density", Range(0.2, 3)) = 1.0
        _DopplerStrength ("Doppler Strength", Range(0, 1.5)) = 0.8
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
            Name "AccretionDiskProxy"
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
                float _SpiralStrength;
                float _TurbulenceScale;
                float _VolumeDensity;
                float _DopplerStrength;
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
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 tangentWS : TEXCOORD1;
                float3 diskNormalWS : TEXCOORD2;
                float radialDistance : TEXCOORD3;
                float2 uv : TEXCOORD4;
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

                float warp = sin(
                    angle * _WarpFrequency +
                    radial01 * 7.0 +
                    _Seed * 18.0) *
                    _WarpStrength * radial01 * radial01;
                positionOS.y += warp;

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
                output.uv = input.uv;
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
                float angularFlow = angle + time / (0.14 + radial * 1.52);

                float broadTurbulence = Fbm(float2(
                    angularFlow * 7.4 * _TurbulenceScale,
                    radial * 13.0 + _Seed * 19.0));
                float fineTurbulence = Fbm(float2(
                    angularFlow * 24.0 * _TurbulenceScale + time * 0.28,
                    radial * 43.0 + _Seed * 53.0));

                float filaments = smoothstep(
                    0.30,
                    0.80,
                    broadTurbulence * 0.72 + fineTurbulence * 0.52);
                float spiralBands = 0.58 + 0.42 * sin(
                    angularFlow * (11.0 + radial * 8.0) -
                    radial * 25.0 +
                    fineTurbulence * 3.2);
                spiralBands = lerp(1.0, spiralBands, _SpiralStrength);

                float innerHeat = pow(1.0 - radial, 0.58);
                float outerFalloff = 1.0 - smoothstep(0.82, 1.0, radial);
                float innerFalloff = smoothstep(0.0, 0.055, radial);

                float3 colour = lerp(
                    _OuterColor.rgb,
                    _MiddleColor.rgb,
                    smoothstep(0.0, 0.62, innerHeat));
                colour = lerp(
                    colour,
                    _InnerColor.rgb,
                    smoothstep(0.50, 1.0, innerHeat));

                float dustAmount = radial * radial *
                                   (0.26 + 0.34 * fineTurbulence);
                colour = lerp(colour, _DustColor.rgb, dustAmount);

                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);
                float approaching = saturate(
                    dot(normalize(input.tangentWS), viewDirection) *
                    0.5 + 0.5);
                float3 recedingTint = float3(0.72, 0.22, 0.13);
                float3 approachingTint = float3(1.16, 1.30, 1.52);
                colour *= lerp(
                    recedingTint,
                    approachingTint,
                    approaching * _DopplerStrength);

                float edgeOn = 1.0 - abs(dot(
                    normalize(input.diskNormalWS),
                    viewDirection));
                float brightness =
                    (0.20 + 0.80 * filaments) *
                    (0.58 + 0.42 * spiralBands) *
                    (0.30 + 1.08 * innerHeat) *
                    outerFalloff *
                    innerFalloff *
                    _VolumeDensity *
                    _Intensity;
                brightness *= lerp(0.78, 1.36, edgeOn);

                return half4(colour * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
