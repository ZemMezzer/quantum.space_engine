Shader "SpaceEngine/Streaming/Black Hole Accretion Lens"
{
    Properties
    {
        _LensingStrength ("Lensing Strength", Range(0, 2)) = 0.95
        _LensViewportRadius ("Lens Viewport Radius", Range(0.0001, 2)) = 0.10
        _LensCenterViewport ("Lens Center Viewport", Vector) = (0.5, 0.5, 0, 0)
        _ShadowRadiusRatio ("Apparent Shadow Radius Ratio", Range(0.02, 0.50)) = 0.2165
        _DiskIntensity ("Disk Intensity", Range(0, 16)) = 4.2
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
            Name "LensedAccretionDisk"
            Tags { "LightMode" = "UniversalForward" }

            // When the opaque scene colour is available, the shader replaces
            // its own circular area with a lensed copy of the sky. In the
            // fallback case the alpha keeps the disk usable without it.
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
                float _ShadowRadiusRatio;
                float _DiskIntensity;
                float _Seed;
                float _SurfaceTime;
                float _SceneColorAvailable;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float GaussianBand(float value, float width)
            {
                float safeWidth = max(width, 0.0001);
                float normalized = value / safeWidth;
                return exp(-normalized * normalized);
            }

            float StreamNoise(float coordinate, float phase)
            {
                float broad = sin(coordinate * 38.0 + phase * 1.7);
                float fine = sin(coordinate * 121.0 - phase * 3.1);
                float micro = sin(coordinate * 283.0 + phase * 5.3);
                return 0.66 + broad * 0.16 + fine * 0.11 + micro * 0.07;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 screenUv = input.positionCS.xy / _ScaledScreenParams.xy;
                float2 centre = _LensCenterViewport.xy;

                float aspect = _ScaledScreenParams.x /
                               max(_ScaledScreenParams.y, 1.0);
                float2 aspectScale = float2(aspect, 1.0);
                float lensRadius = max(_LensViewportRadius * aspect, 0.00001);
                float2 local = (screenUv - centre) * aspectScale;
                float2 p = local / lensRadius;
                float radius = length(p);

                // The quad is intentionally larger than the optical effect.
                // Discarding its square corners leaves a clean circular lens.
                if (radius > 1.0)
                    discard;

                float2 radialDirection = radius > 0.00001
                    ? p / radius
                    : float2(1.0, 0.0);
                float2 tangentDirection = float2(
                    -radialDirection.y,
                    radialDirection.x);

                float shadowRadius = max(_ShadowRadiusRatio, 0.0001);
                float impact = max(radius / shadowRadius, 1.0005);
                float capture = 1.0 / max(impact - 1.0, 0.035);

                // This is still an inexpensive approximation rather than a
                // full geodesic integrator, but it has the important visual
                // behaviour: the sky is drawn into a tight, circular whirl
                // beside the capture shadow rather than merely pinched.
                float weakDeflection = 0.24 / (impact * impact + 0.10);
                float criticalDeflection = 0.34 * log(1.0 + capture);
                float deflection = _LensingStrength *
                                   (weakDeflection + criticalDeflection) *
                                   shadowRadius;
                deflection = min(deflection, shadowRadius * 2.8);

                float swirl = shadowRadius * 0.18 *
                              exp(-pow((impact - 1.65) / 0.72, 2.0));

                float2 sourceLocal =
                    radialDirection * (radius + deflection) +
                    tangentDirection * swirl;
                float2 lensedUv = centre +
                    (sourceLocal * lensRadius) / aspectScale;

                float sceneAvailable = step(0.5, _SceneColorAvailable);
                float3 originalScene = SampleSceneColor(saturate(screenUv));
                float3 lensedScene = SampleSceneColor(saturate(lensedUv));

                float outerFade = 1.0 - smoothstep(0.72, 1.0, radius);
                float nearCapture = exp(-pow((impact - 1.15) / 0.38, 2.0));
                float lensBlend = sceneAvailable * outerFade *
                                  saturate(0.13 + nearCapture * 0.87);
                float3 colour = lerp(originalScene, lensedScene, lensBlend);

                // The direct image of the disk is deliberately kept thin and
                // edge-on. The bright approaching side is on screen-left;
                // the receding side remains dim and red, as in the reference.
                float horizontalFade = 1.0 - smoothstep(
                    0.64,
                    0.98,
                    abs(p.x));
                float nearHoleFlare = exp(-pow(
                    (abs(p.x) - shadowRadius * 1.22) /
                    (shadowRadius * 0.90),
                    2.0));
                float stripWidth = lerp(0.020, 0.055, nearHoleFlare);
                float directStrip = GaussianBand(p.y, stripWidth) *
                                    horizontalFade;

                // Light from the back of the same flat disk is bent above
                // and below the shadow. Rendering it procedurally as tight
                // circular arcs gives the characteristic wrapped silhouette
                // without a very expensive ray-marched spacetime shader.
                float arcRadius = shadowRadius * 1.60;
                float arcWidth = shadowRadius * 0.165;
                float arcBand = GaussianBand(radius - arcRadius, arcWidth);

                float topArc = smoothstep(
                    -0.20,
                    0.38,
                    p.y / max(arcRadius, 0.0001));
                float bottomArc = 1.0 - smoothstep(
                    -0.42,
                    0.14,
                    p.y / max(arcRadius, 0.0001));

                float arcSideFade = 1.0 - smoothstep(0.64, 0.96, abs(p.x));
                float lensedArcs = arcBand * arcSideFade *
                                   (topArc * 1.00 + bottomArc * 0.56);

                // A faint second image hugs the photon orbit. It is much
                // weaker than the disk itself, avoiding the broad orange halo
                // produced by the previous horizon shader.
                float photonArc = GaussianBand(
                    radius - shadowRadius * 1.10,
                    shadowRadius * 0.045);
                photonArc *= 0.16 + topArc * 0.34;

                float phase = _SurfaceTime * 0.62 + _Seed * 27.0;
                float streams = saturate(StreamNoise(
                    p.x + p.y * 0.34,
                    phase));

                float approaching = 1.0 - smoothstep(-0.62, 0.78, p.x);
                float receding = 1.0 - approaching;

                float3 recedingColor = float3(1.15, 0.075, 0.003);
                float3 approachingColor = float3(3.8, 1.85, 0.48);
                float3 diskColor = lerp(
                    recedingColor,
                    approachingColor,
                    approaching);

                // The core of the blue-shifted side is near-white instead of
                // a uniform orange plume.
                float whiteCore = approaching *
                                  directStrip *
                                  exp(-pow(p.x / 0.44, 2.0));
                diskColor = lerp(
                    diskColor,
                    float3(5.4, 4.5, 2.4),
                    whiteCore * 0.68);

                float diskMask = directStrip * 1.20 +
                                 lensedArcs * 1.05 +
                                 photonArc;
                float diskBrightness = diskMask *
                                       streams *
                                       _DiskIntensity;

                // The event-horizon renderer placed immediately after this
                // pass writes physical depth too. Keeping the same capture
                // mask here prevents a one-pixel bright seam on its contour.
                float shadowMask = 1.0 - smoothstep(
                    shadowRadius * 0.985,
                    shadowRadius * 1.025,
                    radius);

                colour += diskColor * diskBrightness;
                colour = lerp(colour, 0.0, shadowMask);

                if (sceneAvailable < 0.5)
                {
                    float fallbackAlpha = saturate(
                        diskMask * 0.88 + photonArc * 0.24);

                    if (fallbackAlpha <= 0.0005)
                        discard;

                    return half4(
                        diskColor * diskBrightness,
                        fallbackAlpha);
                }

                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
}
