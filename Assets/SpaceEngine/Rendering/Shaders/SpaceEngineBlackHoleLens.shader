Shader "SpaceEngine/Streaming/Black Hole Lens"
{
    Properties
    {
        _LensingStrength ("Lensing Strength", Range(0, 2)) = 0.95
        _LensRadius ("Lens Radius", Float) = 0.14
        _HorizonRadius ("Horizon Radius", Float) = 0.05
        _LensCenterViewport ("Lens Center Viewport", Vector) = (0.5, 0.5, 0, 0)
        _LensEdgeSoftness ("Lens Edge Softness", Range(0.01, 0.50)) = 0.20
        [HDR] _LensRingColor ("Lens Ring Color", Color) = (0.78, 0.88, 1.0, 1)
        _LensRingIntensity ("Lens Ring Intensity", Range(0, 1)) = 0.16
        _SwirlStrength ("Swirl Strength", Range(0, 12)) = 5.5
        _SwirlFalloff ("Swirl Falloff", Range(0.25, 6)) = 1.75
        _SwirlDirection ("Swirl Direction", Range(-1, 1)) = 1
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
            Name "GravitationalLensing"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _LensingStrength;
                float _LensRadius;
                float _HorizonRadius;
                float4 _LensCenterViewport;
                float _LensEdgeSoftness;
                half4 _LensRingColor;
                float _LensRingIntensity;
                float _SwirlStrength;
                float _SwirlFalloff;
                float _SwirlDirection;
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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
                float2 centre = _LensCenterViewport.xy;

                float aspect = _ScaledScreenParams.x /
                               max(_ScaledScreenParams.y, 1.0);
                float2 aspectScale = float2(aspect, 1.0);
                float2 localOffset = (screenUv - centre) * aspectScale;
                float radialDistance = length(localOffset);

                float horizonRadius = max(_HorizonRadius, 0.000001);
                float lensRadius = max(_LensRadius, horizonRadius + 0.000001);

                // This discard is the exact safety boundary: no lensed pixel can
                // ever be emitted inside the measured silhouette of the mesh.
                clip(radialDistance - horizonRadius);
                clip(lensRadius - radialDistance);

                float2 radialDirection = localOffset /
                                         max(radialDistance, 0.000001);

                float exterior = (radialDistance - horizonRadius) /
                                 max(lensRadius - horizonRadius, 0.000001);
                float impact = max(radialDistance / horizonRadius, 1.0001);

                // Radial compression toward the photon sphere.
                float weakDeflection = 0.52 / (impact * impact + 0.10);
                float strongDeflection = 0.44 /
                                         max(impact - 0.94, 0.08);
                float deflection = horizonRadius * _LensingStrength *
                                   1.18 * (weakDeflection + strongDeflection);
                deflection = min(
                    deflection,
                    max(lensRadius - radialDistance * 0.25, horizonRadius));

                // Add visible frame dragging / swirl instead of pure radial
                // lensing. The twist is strongest near the horizon and fades
                // out smoothly toward the outer edge of the lensing region.
                float nearHorizon = 1.0 - saturate(exterior);
                float impactGap = max(impact - 1.0, 0.06);
                float twistWeight = pow(nearHorizon, max(_SwirlFalloff, 0.01));
                float twistAngle = _SwirlDirection * _SwirlStrength *
                                   1.85 * twistWeight *
                                   (0.35 + 1.15 / (impactGap * 2.1 + 0.85));
                twistAngle = clamp(twistAngle, -5.4, 5.4);

                float baseAngle = atan2(localOffset.y, localOffset.x);
                float twistedAngle = baseAngle + twistAngle;
                float2 twistedRadialDirection = float2(
                    cos(twistedAngle),
                    sin(twistedAngle));
                float2 twistedTangentDirection = float2(
                    -twistedRadialDirection.y,
                    twistedRadialDirection.x);

                float tangentialDeflection = horizonRadius * _SwirlStrength *
                                             0.72 * twistWeight /
                                             max(impactGap + 0.12, 0.12);

                float2 sourceLocal = twistedRadialDirection *
                                     (radialDistance + deflection) +
                                     twistedTangentDirection *
                                     tangentialDeflection * _SwirlDirection;
                float2 lensedUv = centre + sourceLocal / aspectScale;

                float3 lensedColor = SampleSceneColor(saturate(lensedUv));

                float pixelWidth = max(fwidth(radialDistance), 0.00001);
                float horizonFade = smoothstep(
                    horizonRadius,
                    horizonRadius + pixelWidth * 1.5,
                    radialDistance);
                float edgeStart = saturate(1.0 - _LensEdgeSoftness);
                float outerFade = 1.0 - smoothstep(
                    edgeStart,
                    1.0,
                    exterior);
                float opacity = horizonFade * outerFade;

                // A small Einstein-ring highlight makes the field readable when
                // the sampled background is nearly a uniform colour.
                float photonRing = exp(-pow((impact - 1.12) / 0.11, 2.0));
                lensedColor += _LensRingColor.rgb * photonRing *
                               _LensRingIntensity * _LensingStrength;

                return half4(lensedColor, saturate(opacity));
            }
            ENDHLSL
        }
    }
}
