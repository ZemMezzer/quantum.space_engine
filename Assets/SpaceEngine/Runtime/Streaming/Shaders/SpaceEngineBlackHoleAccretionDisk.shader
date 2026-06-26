Shader "SpaceEngine/Streaming/Black Hole Accretion Disk"
{
    Properties
    {
        _Intensity ("Intensity", Range(0, 12)) = 3.4
        _RotationSpeed ("Rotation Speed", Range(0, 4)) = 0.8
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
            Name "AccretionDisk"
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
                float2 uv : TEXCOORD2;
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
                float3 tangentOS = normalize(float3(
                    -positionOS.z,
                    0.0,
                    positionOS.x));

                output.positionCS =
                    TransformObjectToHClip(positionOS);
                output.positionWS =
                    TransformObjectToWorld(positionOS);
                output.tangentWS =
                    TransformObjectToWorldDir(tangentOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float radial = saturate(input.uv.y);
                float angle = input.uv.x * 6.28318530718;
                float time = _SurfaceTime * _RotationSpeed;

                // Inner material orbits more quickly than the cooler outer
                // disk, producing streaks instead of a flat glowing ring.
                float angularFlow = angle +
                                    time / (0.10 + radial * 1.45);

                float broadTurbulence = Fbm(float2(
                    angularFlow * 8.5,
                    radial * 15.0 + _Seed * 19.0));

                float fineTurbulence = Fbm(float2(
                    angularFlow * 31.0 + time * 0.36,
                    radial * 52.0 + _Seed * 53.0));

                float filaments = smoothstep(
                    0.36,
                    0.80,
                    broadTurbulence * 0.72 +
                    fineTurbulence * 0.52);

                float spiralBands = 0.58 + 0.42 * sin(
                    angularFlow * (18.0 + radial * 10.0) -
                    radial * 34.0 +
                    fineTurbulence * 4.0);

                float innerHeat = pow(1.0 - radial, 0.45);
                float outerFalloff = 1.0 - smoothstep(
                    0.78,
                    1.0,
                    radial);

                float3 outerColor = float3(0.32, 0.008, 0.001);
                float3 middleColor = float3(1.0, 0.075, 0.006);
                float3 innerColor = float3(1.0, 0.76, 0.30);

                float3 colour = lerp(
                    outerColor,
                    middleColor,
                    smoothstep(0.0, 0.60, innerHeat));

                colour = lerp(
                    colour,
                    innerColor,
                    smoothstep(0.42, 1.0, innerHeat));

                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);
                float doppler = dot(
                    normalize(input.tangentWS),
                    viewDirection);

                // The approaching side is brighter and slightly bluer; the
                // receding side remains warmer and dimmer.
                float approaching = saturate(doppler * 0.5 + 0.5);
                colour = lerp(
                    colour * float3(1.08, 0.26, 0.08),
                    colour * float3(1.12, 1.25, 1.42),
                    approaching * 0.36);

                float brightness = (0.18 + 0.82 * filaments) *
                                   (0.54 + 0.46 * spiralBands) *
                                   (0.34 + 0.96 * innerHeat) *
                                   outerFalloff *
                                   _Intensity;

                return half4(colour * brightness, 1.0);
            }
            ENDHLSL
        }
    }
}
