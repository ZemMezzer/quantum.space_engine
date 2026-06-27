Shader "SpaceEngine/Streaming/Black Hole Lens"
{
    Properties
    {
        _LensingStrength ("Lensing Strength", Range(0, 2)) = 1.02
        _LensViewportRadius ("Lens Viewport Radius", Range(0.0001, 2)) = 0.14
        _LensCenterViewport ("Lens Center Viewport", Vector) = (0.5, 0.5, 0, 0)
        _ShadowRadiusRatio ("Apparent Shadow Radius Ratio", Range(0.02, 0.50)) = 0.2165
        _LensEdgeSoftness ("Lens Edge Softness", Range(0.01, 0.50)) = 0.24
        _Seed ("Seed", Range(0, 1)) = 0
        _SurfaceTime ("Surface Time", Float) = 0
        [HideInInspector] _SceneColorAvailable ("Scene Color Available", Float) = 0
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
            Name "GravitationalLensing"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _LensingStrength;
                float _LensViewportRadius;
                float4 _LensCenterViewport;
                float _ShadowRadiusRatio;
                float _LensEdgeSoftness;
                float _Seed;
                float _SurfaceTime;
                float _SceneColorAvailable;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUv = input.positionCS.xy / _ScaledScreenParams.xy;
                float2 centre = _LensCenterViewport.xy;

                float aspect = _ScaledScreenParams.x / max(_ScaledScreenParams.y, 1.0);
                float2 aspectScale = float2(aspect, 1.0);
                float2 localOffset = (screenUv - centre) * aspectScale;

                float lensRadius = max(_LensViewportRadius * aspect, 0.000001);
                float radialDistance = length(localOffset);
                float normalizedRadius = radialDistance / lensRadius;

                if (normalizedRadius >= 1.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float2 radialDirection = radialDistance > 0.000001
                    ? localOffset / radialDistance
                    : float2(1.0, 0.0);
                float2 tangentDirection = float2(-radialDirection.y, radialDirection.x);

                float shadowRadius = max(lensRadius * _ShadowRadiusRatio, 0.000001);
                float impact = max(radialDistance / shadowRadius, 1.0001);
                float captureProximity = 1.0 / max(impact - 1.0, 0.030);

                float weakDeflection = 0.55 / (impact * impact + 0.08);
                float criticalDeflection = 0.62 * log(1.0 + captureProximity);
                float ringCompression = 0.12 / (impact * impact * impact + 0.10);

                float deflection = shadowRadius * _LensingStrength *
                                   (weakDeflection + criticalDeflection + ringCompression);
                deflection = min(deflection, shadowRadius * 5.0);

                float tangentialTwist = shadowRadius * 0.52 *
                                        exp(-pow(max(impact - 1.28, 0.0) / 0.52, 2.0));

                float2 sourceLocal = radialDirection * (radialDistance + deflection) +
                                     tangentDirection * tangentialTwist;
                float2 lensedUv = centre + sourceLocal / aspectScale;

                float3 lensedColour = SampleSceneColor(saturate(lensedUv));

                float edgeStart = saturate(1.0 - _LensEdgeSoftness);
                float edgeFade = 1.0 - smoothstep(edgeStart, 1.0, normalizedRadius);

                float ringZone = exp(-pow((impact - 1.45) / 0.52, 2.0));
                float innerZone = exp(-pow((impact - 1.05) / 0.18, 2.0));
                float blendAmount = edgeFade * saturate(0.12 + ringZone * 0.68 + innerZone * 0.42);

                return half4(lensedColour, saturate(blendAmount * _SceneColorAvailable));
            }
            ENDHLSL
        }
    }
}
