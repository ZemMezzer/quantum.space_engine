Shader "SpaceEngine/Streaming/Star Surface"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (1, 0.18, 0.03, 1)
        [HDR] _SurfaceColor ("Surface Color", Color) = (1.1, 0.42, 0.08, 1)
        [HDR] _HotColor ("Hot Color", Color) = (1.6, 0.85, 0.2, 1)
        [HDR] _SpotColor ("Spot Color", Color) = (0.08, 0.004, 0.001, 1)
        _GranulationScale ("Convection Cell Scale", Range(4, 160)) = 52
        _DetailScale ("Fine Plasma Detail Scale", Range(8, 640)) = 240
        _SpotScale ("Active Region Scale", Range(2, 40)) = 18
        _SpotStrength ("Active Region Strength", Range(0, 1)) = 0.45
        _FlowSpeed ("Flow Speed", Range(0, 4)) = 0.2
        _Seed ("Seed", Range(0, 1)) = 0
        _SurfaceTime ("Surface Time", Float) = 0
        _Intensity ("Intensity", Range(0, 16)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "StarSurface"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _SurfaceColor;
                half4 _HotColor;
                half4 _SpotColor;
                float _GranulationScale;
                float _DetailScale;
                float _SpotScale;
                float _SpotStrength;
                float _FlowSpeed;
                float _Seed;
                float _SurfaceTime;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 directionOS : TEXCOORD2;
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float3 Hash33(float3 p)
            {
                return float3(
                    Hash31(p + float3(11.7, 3.1, 17.9)),
                    Hash31(p + float3(29.4, 41.3, 5.6)),
                    Hash31(p + float3(7.2, 53.8, 31.5)));
            }

            float ValueNoise(float3 p)
            {
                float3 cell = floor(p);
                float3 local = frac(p);
                local = local * local * (3.0 - 2.0 * local);

                float n000 = Hash31(cell + float3(0, 0, 0));
                float n100 = Hash31(cell + float3(1, 0, 0));
                float n010 = Hash31(cell + float3(0, 1, 0));
                float n110 = Hash31(cell + float3(1, 1, 0));
                float n001 = Hash31(cell + float3(0, 0, 1));
                float n101 = Hash31(cell + float3(1, 0, 1));
                float n011 = Hash31(cell + float3(0, 1, 1));
                float n111 = Hash31(cell + float3(1, 1, 1));

                float x00 = lerp(n000, n100, local.x);
                float x10 = lerp(n010, n110, local.x);
                float x01 = lerp(n001, n101, local.x);
                float x11 = lerp(n011, n111, local.x);
                float y0 = lerp(x00, x10, local.y);
                float y1 = lerp(x01, x11, local.y);

                return lerp(y0, y1, local.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;

                [unroll]
                for (int octave = 0; octave < 4; octave++)
                {
                    sum += ValueNoise(p) * amplitude;
                    p = p * 2.03 + float3(17.17, 9.31, 31.73);
                    amplitude *= 0.5;
                }

                return sum / 0.9375;
            }

            void Cellular(
                float3 p,
                out float nearestDistance,
                out float secondDistance,
                out float cellValue)
            {
                float3 cell = floor(p);
                float3 local = frac(p);

                float nearestSquared = 8.0;
                float secondSquared = 8.0;
                cellValue = 0.0;

                [unroll]
                for (int z = -1; z <= 1; z++)
                {
                    [unroll]
                    for (int y = -1; y <= 1; y++)
                    {
                        [unroll]
                        for (int x = -1; x <= 1; x++)
                        {
                            float3 offset = float3(x, y, z);
                            float3 id = cell + offset;
                            float3 feature = offset + Hash33(id);
                            float3 delta = feature - local;
                            float distanceSquared = dot(delta, delta);

                            if (distanceSquared < nearestSquared)
                            {
                                secondSquared = nearestSquared;
                                nearestSquared = distanceSquared;
                                cellValue = Hash31(id + 91.17);
                            }
                            else if (distanceSquared < secondSquared)
                            {
                                secondSquared = distanceSquared;
                            }
                        }
                    }
                }

                nearestDistance = sqrt(nearestSquared);
                secondDistance = sqrt(secondSquared);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.directionOS = normalize(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float time = _SurfaceTime * _FlowSpeed;
                float3 direction = normalize(input.directionOS);

                // The low-frequency Worley field produces the bright cells and
                // dark intercellular lanes of a convective photosphere. Fine
                // flowing noise breaks their edges into turbulent plasma.
                float3 convectionPosition =
                    direction * _GranulationScale +
                    float3(time * 0.20, -time * 0.13, time * 0.09) +
                    _Seed * 37.31;

                float nearestCell;
                float secondCell;
                float cellValue;
                Cellular(
                    convectionPosition,
                    nearestCell,
                    secondCell,
                    cellValue);

                float cellCore = saturate(1.0 - nearestCell * 1.16);
                float cellBoundary = 1.0 - smoothstep(
                    0.055,
                    0.250,
                    secondCell - nearestCell);

                float3 detailPosition =
                    direction * _DetailScale +
                    float3(-time * 0.67, time * 0.31, time * 0.45) +
                    _Seed * 83.19;

                float fineTurbulence = Fbm(detailPosition);
                float filamentTurbulence = Fbm(
                    detailPosition * 0.42 + float3(13.1, 7.9, 29.4));

                float granulation = saturate(
                    cellCore * 0.84 +
                    fineTurbulence * 0.29 -
                    cellBoundary * 0.78);

                float hotCell = smoothstep(0.42, 0.88, granulation);
                float filament = smoothstep(
                    0.66,
                    0.91,
                    filamentTurbulence) *
                    (0.25 + 0.75 * cellCore);

                float3 color = lerp(
                    _BaseColor.rgb,
                    _SurfaceColor.rgb,
                    saturate(0.28 + granulation * 0.72));

                color = lerp(
                    color,
                    _HotColor.rgb,
                    hotCell * (0.58 + 0.42 * fineTurbulence));

                color = lerp(
                    color,
                    _SpotColor.rgb,
                    cellBoundary * (0.46 + 0.28 * (1.0 - fineTurbulence)));

                float spotNoise = Fbm(
                    direction * _SpotScale +
                    float3(time * 0.025, -time * 0.037, time * 0.019) +
                    _Seed * 129.7);

                float spots = smoothstep(0.71, 0.91, spotNoise) *
                              _SpotStrength;

                color = lerp(color, _SpotColor.rgb, spots);
                color += _HotColor.rgb * filament * 0.16;

                float3 normal = normalize(input.normalWS);
                float3 viewDirection = normalize(
                    _WorldSpaceCameraPos - input.positionWS);
                float viewAlignment = saturate(dot(normal, viewDirection));

                // A real photosphere darkens toward the limb; bright faculae
                // remain visible where active plasma rises above the surface.
                float limbDarkening = lerp(0.56, 1.0, viewAlignment);
                float faculae = pow(1.0 - viewAlignment, 2.1) *
                                (hotCell * 0.55 + filament * 0.45);

                color = color * limbDarkening + _HotColor.rgb * faculae * 0.26;

                return half4(color * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
