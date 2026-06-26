Shader "SpaceEngine/Streaming/Black Hole Lens"
{
    Properties
    {
        _LensingStrength ("Lensing Strength", Range(0, 2)) = 0.82
        _LensViewportRadius ("Lens Viewport Radius", Range(0.0001, 2)) = 0.10
        _LensCenterViewport ("Lens Center Viewport", Vector) = (0.5, 0.5, 0, 0)
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

            // The shell samples and replaces the celestial camera's previous
            // colour at the same pixel. It intentionally does not contribute
            // depth: it is an optical region, not solid matter.
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
                float _LensViewportRadius;
                float4 _LensCenterViewport;
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
                float2 screenUv = input.positionCS.xy /
                                  _ScaledScreenParams.xy;
                float2 centre = _LensCenterViewport.xy;
                float2 offset = screenUv - centre;
                float radialDistance = length(offset);
                float lensRadius = max(_LensViewportRadius, 0.0001);
                float normalizedRadius = radialDistance / lensRadius;
                float2 radialDirection = radialDistance > 0.000001
                    ? offset / radialDistance
                    : float2(1.0, 0.0);

                // A camera ray close to the shadow samples light from farther
                // out in the background. The falloff leaves the outer shell
                // continuous with the unbent celestial sky.
                float shellFade = 1.0 - smoothstep(
                    0.64,
                    1.06,
                    normalizedRadius);

                float nearHorizon = pow(
                    saturate(1.0 - normalizedRadius),
                    1.65);

                float radialOffset = lensRadius *
                                     _LensingStrength *
                                     (0.08 + 0.42 * nearHorizon) *
                                     shellFade;

                float2 lensedUv = centre +
                                  radialDirection *
                                  (radialDistance + radialOffset);

                float3 originalColour = SampleSceneColor(screenUv);
                float3 lensedColour = SampleSceneColor(
                    saturate(lensedUv));

                float blendAmount = shellFade *
                                    (0.25 + 0.75 * nearHorizon);

                // The weak high-frequency term prevents the lens edge from
                // becoming a mathematically perfect CG circle without adding
                // visible animated noise.
                float edgeBreakup = 0.985 + 0.015 * sin(
                    atan2(offset.y, offset.x) * 13.0 +
                    _Seed * 37.0);

                blendAmount *= edgeBreakup;

                // Alpha blending keeps the shell harmless on renderers
                // that do not expose a camera opaque texture. In URP the
                // camera setting is enabled by CelestialRenderer3D and the
                // lensed scene colour replaces the original at full alpha.
                return half4(
                    lensedColour,
                    saturate(blendAmount * _SceneColorAvailable));
            }
            ENDHLSL
        }
    }
}
