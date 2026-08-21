Shader "Terrias/CardFaceEffect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card Face Sprite", 2D) = "white" {}
        _NoiseTex ("Effect Noise", 2D) = "white" {}
        _FoilTex ("Foil Texture", 2D) = "white" {}
        _TextureSampleAdd ("Texture Sample Add", Vector) = (0, 0, 0, 0)
        _TerriasEffectMode ("Effect Mode", Float) = 0
        _TerriasOverlayMode ("Overlay Mode", Float) = 0
        _TerriasFrameOnlyOverlay ("Frame Only Overlay", Float) = 0
        _TerriasFoilMode ("Foil Mode", Float) = 1
        _TerriasHoloColorA ("Holo Color A", Color) = (1, 0.94, 0.65, 1)
        _TerriasHoloColorB ("Holo Color B", Color) = (0.65, 0.95, 1, 1)
        _TerriasHoloColorC ("Holo Color C", Color) = (0.82, 0.72, 1, 1)
        _TerriasStardustColorA ("Stardust Core", Color) = (0.86, 0.94, 1, 1)
        _TerriasStardustColorB ("Stardust Warm", Color) = (1, 0.85, 0.44, 1)
        _TerriasFlowSpeed ("Flow Speed", Float) = 0.55
        _TerriasFlowScale ("Flow Scale", Float) = 1.22
        _TerriasNoiseScale ("Noise Scale", Float) = 4.0
        _TerriasDistortion ("Distortion", Float) = 0.009
        _TerriasEffectIntensity ("Effect Intensity", Range(0, 2)) = 1.04
        _TerriasQualityScale ("Quality Scale", Range(0, 1)) = 1
        _TerriasEdgeGlow ("Edge Glow", Range(0, 1)) = 0.28
        _TerriasSweepFrequency ("Sweep Frequency", Float) = 4.4
        _TerriasSweepWidth ("Sweep Width", Range(0.01, 1)) = 0.13
        _TerriasSweepIntensity ("Sweep Intensity", Range(0, 2)) = 1.12
        _TerriasPrismScale ("Prism Scale", Float) = 13.5
        _TerriasPrismStrength ("Prism Strength", Range(0, 1)) = 1
        _TerriasFoilGrain ("Foil Grain", Range(0, 1)) = 0.08
        _TerriasMirrorSweep ("Mirror Sweep", Range(0, 2)) = 0.58
        _TerriasSwirlStrength ("Swirl Strength", Range(0, 1)) = 0.06
        _TerriasFoilShardScale ("Foil Shard Scale", Float) = 18
        _TerriasFoilShardWarp ("Foil Shard Warp", Range(0, 1)) = 0.08
        _TerriasFoilGalaxyDensity ("Foil Galaxy Density", Range(0, 1)) = 0.015
        _TerriasFoilGlintSpeed ("Foil Glint Speed", Float) = 1.1
        _TerriasFoilTextureStrength ("Foil Texture Strength", Range(0, 2)) = 0.6
        _TerriasRainbowStrength ("Rainbow Strength", Range(0, 2)) = 1.25
        _TerriasRidgeStrength ("Ridge Strength", Range(0, 2)) = 0.7
        _TerriasGlareStrength ("Glare Strength", Range(0, 2)) = 0.35
        _TerriasPointerAutoSpeed ("Pointer Auto Speed", Float) = 0.78
        _TerriasFoilOverlayAlpha ("Foil Overlay Alpha", Range(0, 2)) = 1
        _TerriasPointerX ("Pointer X", Float) = -1
        _TerriasPointerY ("Pointer Y", Float) = -1
        _TerriasStardustDensity ("Stardust Density", Range(0, 1)) = 0.38
        _TerriasStardustTwinkle ("Stardust Twinkle", Range(0, 2)) = 1.0
        _TerriasStardustTwinkleSpeed ("Stardust Twinkle Speed", Float) = 1.0
        _TerriasStardustOrbit ("Stardust Orbit", Range(0, 1)) = 0.32
        _TerriasStardustGlowRadius ("Stardust Glow Radius", Range(0.03, 0.7)) = 0.22
        _TerriasStardustGlowPower ("Stardust Glow Power", Range(1, 8)) = 4
        _TerriasStardustSweepSpeed ("Stardust Sweep Speed", Float) = 1.0
        _TerriasStardustSweepIntensity ("Stardust Sweep Intensity", Range(0, 2)) = 0.72
        _TerriasStardustSweepWidth ("Stardust Sweep Width", Range(0.01, 0.35)) = 0.085
        _TerriasEdgeSample ("Edge Sample", Float) = 2

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _NoiseTex;
            sampler2D _FoilTex;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _TerriasEffectMode;
            float _TerriasOverlayMode;
            float _TerriasFrameOnlyOverlay;
            float _TerriasFoilMode;
            fixed4 _TerriasHoloColorA;
            fixed4 _TerriasHoloColorB;
            fixed4 _TerriasHoloColorC;
            fixed4 _TerriasStardustColorA;
            fixed4 _TerriasStardustColorB;
            float _TerriasFlowSpeed;
            float _TerriasFlowScale;
            float _TerriasNoiseScale;
            float _TerriasDistortion;
            float _TerriasEffectIntensity;
            float _TerriasQualityScale;
            float _TerriasEdgeGlow;
            float _TerriasSweepFrequency;
            float _TerriasSweepWidth;
            float _TerriasSweepIntensity;
            float _TerriasPrismScale;
            float _TerriasPrismStrength;
            float _TerriasFoilGrain;
            float _TerriasMirrorSweep;
            float _TerriasSwirlStrength;
            float _TerriasFoilShardScale;
            float _TerriasFoilShardWarp;
            float _TerriasFoilGalaxyDensity;
            float _TerriasFoilGlintSpeed;
            float _TerriasFoilTextureStrength;
            float _TerriasRainbowStrength;
            float _TerriasRidgeStrength;
            float _TerriasGlareStrength;
            float _TerriasPointerAutoSpeed;
            float _TerriasFoilOverlayAlpha;
            float _TerriasPointerX;
            float _TerriasPointerY;
            float _TerriasStardustDensity;
            float _TerriasStardustTwinkle;
            float _TerriasStardustTwinkleSpeed;
            float _TerriasStardustOrbit;
            float _TerriasStardustGlowRadius;
            float _TerriasStardustGlowPower;
            float _TerriasStardustSweepSpeed;
            float _TerriasStardustSweepIntensity;
            float _TerriasStardustSweepWidth;
            float _TerriasEdgeSample;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed3 holoRamp(float t)
            {
                t = frac(t);
                float3 waves = 0.5 + 0.5 * cos(6.2831853 * (t + float3(0.0, 0.33, 0.67)));
                waves = pow(saturate(waves), 1.18);
                float sum = max(waves.r + waves.g + waves.b, 0.001);
                fixed3 color = _TerriasHoloColorA.rgb * waves.r
                    + _TerriasHoloColorB.rgb * waves.g
                    + _TerriasHoloColorC.rgb * waves.b;
                return color / sum;
            }

            float faceAlpha(float2 uv)
            {
                return saturate((tex2D(_MainTex, uv) + _TextureSampleAdd).a);
            }

            float innerEdge(float2 uv, float mask)
            {
                float2 stepUv = _MainTex_TexelSize.xy * max(_TerriasEdgeSample, 0.5);
                float neighbor = min(faceAlpha(uv + float2(stepUv.x, 0.0)), faceAlpha(uv - float2(stepUv.x, 0.0)));
                neighbor = min(neighbor, faceAlpha(uv + float2(0.0, stepUv.y)));
                neighbor = min(neighbor, faceAlpha(uv - float2(0.0, stepUv.y)));
                return saturate((mask - neighbor) * 5.5);
            }

            float frameOnlyOverlayMask(float2 uv, float mask)
            {
                if (_TerriasFrameOnlyOverlay < 0.5)
                {
                    return mask;
                }

                float2 border = min(uv, 1.0 - uv);
                float outer = 1.0 - smoothstep(0.055, 0.18, min(border.x, border.y));
                float titleBand = smoothstep(0.835, 0.895, uv.y) * smoothstep(0.04, 0.12, uv.x) * smoothstep(0.96, 0.88, uv.x);
                float textBoxBand = smoothstep(0.08, 0.16, uv.y) * smoothstep(0.28, 0.36, 1.0 - uv.y);
                float frameBand = saturate(outer + titleBand * 0.45 + textBoxBand * 0.35);
                return mask * frameBand;
            }

            float starCell(float2 uv, float time, float density)
            {
                float2 grid = uv * 34.0 + float2(time * 0.18, -time * 0.13);
                float2 cell = floor(grid);
                float2 local = frac(grid) - 0.5;
                float seed = hash21(cell);
                float gate = smoothstep(1.0 - density * 0.52, 1.0, seed);
                float cross = max(1.0 - abs(local.x) * 13.0, 0.0) * max(1.0 - abs(local.y) * 2.3, 0.0)
                    + max(1.0 - abs(local.y) * 13.0, 0.0) * max(1.0 - abs(local.x) * 2.3, 0.0);
                float core = pow(saturate(1.0 - length(local) * 3.2), 5.0);
                float twinkle = 0.5 + 0.5 * sin(time * _TerriasStardustTwinkleSpeed * (3.7 + seed * 4.6) + seed * 18.0);
                twinkle = smoothstep(0.18, 1.0, twinkle);
                return saturate((cross * 0.7 + core) * gate * twinkle);
            }

            float brightStarCell(float2 uv, float time, float density, float scale, float phase)
            {
                float2 grid = uv * scale + float2(time * 0.09 + phase, -time * 0.065 + phase * 0.37);
                float2 cell = floor(grid);
                float2 local = frac(grid) - 0.5;
                float seed = hash21(cell + phase);
                float gate = smoothstep(1.0 - density * 0.34, 1.0, seed);
                float twinkle = 0.5 + 0.5 * sin(time * _TerriasStardustTwinkleSpeed * (5.2 + seed * 5.4) + seed * 38.0 + phase);

                float core = pow(saturate(1.0 - length(local) * 5.2), 6.0);
                float cross = max(1.0 - abs(local.x) * 18.0, 0.0) * max(1.0 - abs(local.y) * 2.15, 0.0)
                    + max(1.0 - abs(local.y) * 18.0, 0.0) * max(1.0 - abs(local.x) * 2.15, 0.0);
                float diagA = max(1.0 - abs(local.x + local.y) * 13.0, 0.0) * max(1.0 - abs(local.x - local.y) * 2.7, 0.0);
                float diagB = max(1.0 - abs(local.x - local.y) * 13.0, 0.0) * max(1.0 - abs(local.x + local.y) * 2.7, 0.0);
                float pulse = smoothstep(0.24, 1.0, twinkle);
                return saturate((core * 1.35 + cross * 0.78 + (diagA + diagB) * 0.28) * gate * pulse);
            }

            float stardustField(float2 uv, float time, float3 noise)
            {
                float2 centered = uv - 0.5;
                float radius = length(centered);
                float angle = atan2(centered.y, centered.x);
                float orbit = sin(angle * 3.0 + radius * 21.0 - time * 1.28 + noise.b * 1.2) * 0.5 + 0.5;
                float glowRadius = max(_TerriasStardustGlowRadius, 0.03);
                float compactGlow = 1.0 - smoothstep(glowRadius * 0.38, glowRadius, radius);
                orbit = pow(orbit, _TerriasStardustGlowPower) * compactGlow * _TerriasStardustOrbit;

                float dustA = starCell(uv + noise.rg * 0.035, time, _TerriasStardustDensity);
                float dustB = starCell(uv * 1.37 + float2(0.11, 0.29), -time * 0.73, _TerriasStardustDensity * 0.72);
                float brightA = brightStarCell(uv + noise.gb * 0.02, time, _TerriasStardustDensity, 16.0, 1.7);
                float brightB = brightStarCell(uv * 1.42 + float2(0.19, 0.31), -time * 0.82, _TerriasStardustDensity * 0.82, 11.0, 5.3);
                float fine = pow(saturate(noise.r * 0.82 + hash21(floor(uv * 136.0)) * 0.38), 7.0) * _TerriasFoilGrain;
                float veil = smoothstep(0.88, 1.0, noise.g) * 0.055 * _TerriasStardustTwinkle;
                return saturate((dustA + dustB * 0.54) * _TerriasStardustTwinkle + brightA * 0.84 + brightB * 0.42 + orbit + fine * 0.58 + veil);
            }

            float stardustSweep(float2 uv, float time, float3 noise)
            {
                float2 centered = uv - 0.5;
                float diagonal = centered.x * 0.74 + centered.y * 1.18;
                float drift = frac(time * 0.31 * _TerriasStardustSweepSpeed + noise.b * 0.045) * 2.2 - 1.1;
                float primary = smoothstep(_TerriasStardustSweepWidth, 0.0, abs(diagonal - drift));
                float echo = smoothstep(_TerriasStardustSweepWidth * 1.65, 0.0, abs(diagonal - drift + 0.22)) * 0.42;
                float pulse = 0.55 + 0.45 * sin(time * 6.8 * _TerriasStardustSweepSpeed + noise.r * 6.2831853);
                return saturate((primary + echo) * pulse * _TerriasStardustSweepIntensity);
            }

            float foilFrameWeight(float2 uv, float edge)
            {
                float2 border = min(uv, 1.0 - uv);
                float outerRim = 1.0 - smoothstep(0.018, 0.17, min(border.x, border.y));
                return saturate(edge * 1.65 + outerRim * 0.82 + 0.16);
            }

            float prismSheen(float2 uv, float time, float3 noise, float frameWeight)
            {
                float2 centered = uv - 0.5;
                float swirl = (atan2(centered.y, centered.x) * 0.1591549 + length(centered) * 1.35) * _TerriasSwirlStrength;
                float2 warped = uv + (noise.rg * 2.0 - 1.0) * (_TerriasDistortion + _TerriasFoilShardWarp * 0.01);
                float broadA = sin((warped.x * 0.82 + warped.y * 1.08 + noise.b * 0.2 + time * 0.052 + swirl) * _TerriasPrismScale);
                float broadB = sin((-warped.x * 1.14 + warped.y * 0.44 + noise.g * 0.18 - time * 0.038) * _TerriasPrismScale * 0.72);
                float membrane = saturate((broadA * 0.5 + 0.5) * 0.62 + (broadB * 0.5 + 0.5) * 0.38);
                float slowFlow = sin((uv.x - uv.y * 0.52 + time * 0.1 + noise.r * 0.08) * _TerriasFlowScale * 6.2831853) * 0.5 + 0.5;
                return saturate((membrane * 0.82 + slowFlow * 0.36) * frameWeight);
            }

            float diffractionLine(float2 uv, float time, float3 noise, float frameWeight)
            {
                float lineScale = max(_TerriasFoilShardScale, 8.0) * 0.62;
                float drift = time * 0.045 + noise.b * 0.035;
                float lineA = abs(frac((uv.x * 1.08 + uv.y * 0.36 + drift) * lineScale) - 0.5) * 2.0;
                float lineB = abs(frac((-uv.x * 0.24 + uv.y * 1.16 - drift * 0.72) * lineScale * 0.58) - 0.5) * 2.0;
                float fineA = pow(saturate(1.0 - lineA), 5.5);
                float fineB = pow(saturate(1.0 - lineB), 7.0) * 0.55;
                float grainGate = smoothstep(0.36, 0.9, noise.r) * _TerriasFoilGrain;
                return saturate((fineA * 1.25 + fineB * 0.85) * (0.34 + grainGate) * frameWeight);
            }

            float cornerGlint(float2 uv, float time, float3 noise, float frameWeight)
            {
                float2 corner = min(uv, 1.0 - uv);
                float cornerMask = 1.0 - smoothstep(0.055, 0.25, length(corner));
                float sweepPos = frac(time * 0.16 * _TerriasFoilGlintSpeed + noise.g * 0.04) * 2.2 - 0.6;
                float diagonal = uv.x + uv.y * 0.78;
                float glint = pow(saturate(1.0 - abs(diagonal - sweepPos) * 3.2), 5.4) * cornerMask;
                float sparse = pow(saturate(noise.b * 0.72 + hash21(floor(uv * 18.0)) * 0.28), 12.0) * _TerriasFoilGalaxyDensity;
                return saturate((glint + sparse * 0.55) * frameWeight);
            }

            float2 foilPointer(float time)
            {
                if (_TerriasPointerX >= 0.0 && _TerriasPointerX <= 1.0 && _TerriasPointerY >= 0.0 && _TerriasPointerY <= 1.0)
                {
                    return float2(_TerriasPointerX, _TerriasPointerY);
                }

                float speed = max(_TerriasPointerAutoSpeed, 0.01);
                return float2(
                    0.5 + sin(time * speed * 1.17) * 0.34,
                    0.5 + cos(time * speed * 0.83 + 1.2) * 0.34);
            }

            fixed3 lightenBlend(fixed3 baseColor, fixed3 layer)
            {
                return max(baseColor, layer);
            }

            fixed3 hardLightBlend(fixed3 baseColor, fixed3 layer)
            {
                fixed3 low = 2.0 * baseColor * layer;
                fixed3 high = 1.0 - 2.0 * (1.0 - baseColor) * (1.0 - layer);
                return saturate(lerp(low, high, step(0.5, layer)));
            }

            fixed3 colorDodgeBlend(fixed3 baseColor, fixed3 layer, float strength)
            {
                fixed3 dodged = baseColor / max(1.0 - layer * saturate(strength), 0.08);
                return saturate(lerp(baseColor, dodged, saturate(strength)));
            }

            float foilLuma(fixed3 color)
            {
                return dot(color, fixed3(0.299, 0.587, 0.114));
            }

            fixed3 pokemonMainShine(float2 uv, float time, float3 noise, float2 pointer, float fromCenter)
            {
                float2 background = lerp(float2(0.5, 0.5), pointer, 0.86);
                background += float2(sin(time * 0.31), cos(time * 0.27)) * 0.035;

                float rainbowAxis = uv.x * 0.84 - uv.y * 0.54 + background.x * 1.14 + noise.b * 0.1;
                fixed3 rainbow = holoRamp(rainbowAxis * 4.2 + time * 0.045) * _TerriasRainbowStrength;

                float stripeAxis = uv.x * 0.56 + uv.y * 1.04 + background.y * 0.72 - time * 0.03;
                float stripe = abs(frac(stripeAxis * 7.2) - 0.5) * 2.0;
                float lightStripe = pow(saturate(1.0 - stripe), 3.2);
                float darkStripe = smoothstep(0.18, 0.86, stripe);
                fixed3 stripeColor = lerp(fixed3(0.32, 0.38, 0.78), fixed3(1.08, 1.12, 0.96), lightStripe);

                float radial = 1.0 - smoothstep(0.0, 0.82, distance(uv, pointer));
                fixed3 pointerColor = holoRamp(uv.x * 0.42 + uv.y * 0.58 + radial * 0.42 + time * 0.025);
                pointerColor *= saturate(0.35 + radial * 0.92 + fromCenter * 0.32);

                fixed3 blended = lerp(rainbow, rainbow * stripeColor, 0.52 + darkStripe * 0.18);
                blended = lerp(blended, pointerColor, radial * 0.42);
                return saturate(blended);
            }

            fixed3 pokemonTextureShine(float2 uv, float time, float3 noise, float2 pointer)
            {
                float2 foilUv = uv * float2(1.55, 0.82) + float2(time * 0.035, -time * 0.018);
                fixed3 foilTex = tex2D(_FoilTex, foilUv + noise.rg * 0.018).rgb;
                float foilGrain = saturate(foilLuma(foilTex) * 1.45);

                fixed3 pillars = holoRamp(uv.y * 7.5 + pointer.y * 2.0 + time * 0.055);
                float pillarBand = pow(saturate(1.0 - abs(frac((uv.y + pointer.y * 0.21 + time * 0.035) * 8.0) - 0.5) * 2.0), 1.4);

                float ridgeAxis = uv.x * 0.72 + uv.y * 1.08 + pointer.x * 0.35 - time * 0.04;
                float ridgeLine = abs(frac(ridgeAxis * 12.0) - 0.5) * 2.0;
                float ridge = pow(saturate(1.0 - ridgeLine), 4.6) * _TerriasRidgeStrength;
                fixed3 ridgeColor = lerp(fixed3(0.22, 0.28, 0.62), fixed3(0.96, 1.08, 1.04), ridge);

                fixed3 textureLayer = lightenBlend(pillars * (0.35 + pillarBand * 0.75), ridgeColor);
                textureLayer = lerp(textureLayer, textureLayer * (0.65 + foilTex * 0.95), saturate(_TerriasFoilTextureStrength));
                textureLayer += holoRamp(uv.x * 1.1 - uv.y * 0.28 + foilGrain + time * 0.02) * foilGrain * _TerriasFoilTextureStrength * 0.62;
                return saturate(textureLayer);
            }

            float pokemonGlare(float2 uv, float2 pointer, float fromCenter)
            {
                float radial = 1.0 - smoothstep(0.0, 0.92, distance(uv, pointer));
                return saturate((radial * 0.3 + pow(radial, 3.2) * 0.24) * _TerriasGlareStrength * (0.22 + fromCenter * 0.5));
            }

            fixed3 applyPokemonFoil(fixed3 baseColor, float2 uv, float mask, float time, float3 noise, float edge, float intensity)
            {
                float2 pointer = foilPointer(time);
                float fromCenter = saturate(length(pointer - 0.5) * 2.0);
                float cardWeight = saturate(mask * (0.72 + edge * 0.65));
                fixed3 mainShine = pokemonMainShine(uv, time, noise, pointer, fromCenter);
                fixed3 textureShine = pokemonTextureShine(uv, time, noise, pointer);
                float glare = pokemonGlare(uv, pointer, fromCenter);

                fixed3 shineStack = colorDodgeBlend(baseColor, mainShine, 0.76 * _TerriasPrismStrength);
                shineStack = lightenBlend(shineStack, textureShine * (0.58 + fromCenter * 0.36));
                shineStack = hardLightBlend(shineStack, fixed3(glare, glare, glare));

                float effect = saturate((foilLuma(mainShine) * 0.56 + foilLuma(textureShine) * 0.48 + glare * 0.42 + edge * 0.22) * intensity * cardWeight);
                fixed3 color = lerp(baseColor, shineStack, effect);
                color += (mainShine * 0.24 + textureShine * 0.22 + fixed3(glare, glare, glare) * 0.14) * intensity * cardWeight;
                return saturate(color);
            }

            fixed3 applyFoil(fixed3 baseColor, float2 uv, float mask, float time, float3 noise, float edge, float intensity)
            {
                float2 microFlow = float2(hash21(uv * 17.0), hash21(uv * 23.0)) - 0.5;
                float2 flow = ((noise.rg * 2.0 - 1.0) + microFlow * 0.08) * _TerriasDistortion;
                float2 effectUv = uv + flow * mask;

                float frameWeight = foilFrameWeight(effectUv, edge) * mask;
                float sheen = prismSheen(effectUv, time, noise, frameWeight);
                float lines = diffractionLine(effectUv, time, noise, frameWeight);
                float glint = cornerGlint(effectUv, time, noise, frameWeight);
                float sweepAxis = (effectUv.x * 0.72 + effectUv.y * 0.92 + noise.b * 0.08) * _TerriasSweepFrequency - time * 0.34;
                float sweep = smoothstep(_TerriasSweepWidth, 0.0, abs(frac(sweepAxis) - 0.5) * 2.0) * _TerriasSweepIntensity * frameWeight;
                float mirror = pow(saturate(1.0 - abs(effectUv.x + effectUv.y * 0.36 - frac(time * 0.12) * 1.35 + 0.18) * 2.1), 4.0) * _TerriasMirrorSweep * frameWeight;

                fixed3 holo = holoRamp(effectUv.x * 0.72 + effectUv.y * 0.58 + sheen * 0.2 + lines * 0.09 + noise.b * 0.12 + time * 0.026);
                float foil = saturate(sheen * 0.62 + lines * 0.58 + sweep * 0.74 + mirror * 0.6 + glint * 1.08 + edge * 0.42);
                fixed3 foilColor = lerp(baseColor, saturate(baseColor * 0.86 + holo * 1.1), _TerriasPrismStrength);
                fixed3 color = lerp(baseColor, foilColor, foil * intensity);
                color += holo * (glint * 0.58 + lines * 0.22 + sweep * 0.3 + mirror * 0.16 + edge * 0.22) * intensity;
                fixed3 result = color;
                if (_TerriasFoilMode > 1.5)
                {
                    result = applyPokemonFoil(baseColor, uv, mask, time, noise, edge, intensity);
                }

                return result;
            }

            fixed3 applyStardust(fixed3 baseColor, float2 uv, float mask, float time, float3 noise, float edge, float intensity)
            {
                float dust = stardustField(uv, time, noise) * mask;
                float sweep = stardustSweep(uv, time, noise) * mask;
                float glint = pow(saturate(max(dust, sweep)), 1.65);
                float slowBand = sin((uv.x * 0.72 + uv.y * 1.35 + time * 0.1) * 6.2831853) * 0.5 + 0.5;
                fixed3 starColor = lerp(_TerriasStardustColorA.rgb, _TerriasStardustColorB.rgb, slowBand * 0.55 + noise.b * 0.24);
                fixed3 color = baseColor + starColor * dust * intensity * 0.52;
                color += starColor * sweep * intensity * 0.44;
                color = lerp(color, saturate(baseColor * 0.78 + starColor * 0.88), glint * intensity * 0.24);
                color += starColor * glint * intensity * 0.36;
                color += _TerriasStardustColorA.rgb * edge * intensity * 0.18;
                return color;
            }

            fixed4 buildFoilOverlay(float2 uv, float mask, float time, float3 noise, float edge, float intensity)
            {
                float2 microFlow = float2(hash21(uv * 17.0), hash21(uv * 23.0)) - 0.5;
                float2 flow = ((noise.rg * 2.0 - 1.0) + microFlow * 0.08) * _TerriasDistortion;
                float2 effectUv = uv + flow * mask;

                float frameWeight = foilFrameWeight(effectUv, edge) * mask;
                float sheen = prismSheen(effectUv, time, noise, frameWeight);
                float lines = diffractionLine(effectUv, time, noise, frameWeight);
                float glint = cornerGlint(effectUv, time, noise, frameWeight);
                float sweepAxis = (effectUv.x * 0.72 + effectUv.y * 0.92 + noise.b * 0.08) * _TerriasSweepFrequency - time * 0.34;
                float sweep = smoothstep(_TerriasSweepWidth, 0.0, abs(frac(sweepAxis) - 0.5) * 2.0) * _TerriasSweepIntensity * frameWeight;
                float mirror = pow(saturate(1.0 - abs(effectUv.x + effectUv.y * 0.36 - frac(time * 0.12) * 1.35 + 0.18) * 2.1), 4.0) * _TerriasMirrorSweep * frameWeight;
                fixed3 holo = holoRamp(effectUv.x * 0.72 + effectUv.y * 0.58 + sheen * 0.2 + lines * 0.09 + noise.b * 0.12 + time * 0.026);

                float sparkle = saturate(sheen * 0.7 + lines * 0.62 + sweep * 0.9 + mirror * 0.72 + glint * 1.1 + edge * 0.5);
                float alpha = saturate((sparkle * 1.05 + glint * 0.34 + edge * 0.22) * intensity);
                fixed3 color = holo * saturate(0.72 + sparkle * 1.05);
                color += _TerriasHoloColorA.rgb * (glint * 0.26 + edge * 0.2) * intensity;
                fixed4 result = fixed4(color, alpha * mask);
                if (_TerriasFoilMode > 1.5)
                {
                    float2 pointer = foilPointer(time);
                    float fromCenter = saturate(length(pointer - 0.5) * 2.0);
                    fixed3 mainShine = pokemonMainShine(uv, time, noise, pointer, fromCenter);
                    fixed3 textureShine = pokemonTextureShine(uv, time, noise, pointer);
                    float glare = pokemonGlare(uv, pointer, fromCenter);
                    fixed3 shineStack = colorDodgeBlend(mainShine * 0.68, textureShine, 0.76);
                    shineStack = lightenBlend(shineStack, textureShine * 0.78);
                    shineStack = hardLightBlend(shineStack, fixed3(glare, glare, glare));
                    float pokemonSparkle = saturate(foilLuma(mainShine) * 0.54 + foilLuma(textureShine) * 0.52 + glare * 0.42 + edge * 0.28);
                    float pokemonAlpha = saturate((pokemonSparkle * 0.98 + fromCenter * 0.12 + edge * 0.28) * intensity * _TerriasFoilOverlayAlpha);
                    result = fixed4(saturate(shineStack * (0.86 + pokemonSparkle * 0.82) + mainShine * 0.22), pokemonAlpha * mask);
                }

                return result;
            }

            fixed4 buildStardustOverlay(float2 uv, float mask, float time, float3 noise, float edge, float intensity)
            {
                float dust = stardustField(uv, time, noise) * mask;
                float sweep = stardustSweep(uv, time, noise) * mask;
                float sparkle = saturate(dust * 0.48 + sweep * 0.62 + edge * 0.46);
                float slowBand = sin((uv.x * 0.72 + uv.y * 1.35 + time * 0.1) * 6.2831853) * 0.5 + 0.5;
                fixed3 tint = lerp(_TerriasStardustColorA.rgb, _TerriasStardustColorB.rgb, slowBand * 0.55 + noise.b * 0.24);
                fixed3 color = tint * saturate(0.48 + sparkle * 0.92);
                float alpha = saturate(sparkle * intensity * 0.88);
                return fixed4(color, alpha * mask);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 face = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                float mask = saturate(face.a);
                float overlayMask = frameOnlyOverlayMask(i.uv, mask);
                float time = _Time.y * _TerriasFlowSpeed;
                float2 noiseUv = i.uv * _TerriasNoiseScale + float2(time * 0.19, -time * 0.13);
                float3 noise = tex2D(_NoiseTex, noiseUv).rgb;
                float edge = innerEdge(i.uv, mask) * _TerriasEdgeGlow;
                float intensity = _TerriasEffectIntensity * _TerriasQualityScale * mask;
                float overlayEdge = max(edge, innerEdge(i.uv, overlayMask) * _TerriasEdgeGlow);
                float overlayIntensity = _TerriasEffectIntensity * _TerriasQualityScale * overlayMask;

                fixed3 foilColor = applyFoil(face.rgb, i.uv, mask, time, noise, edge, intensity);
                fixed3 starColor = applyStardust(face.rgb, i.uv, mask, time, noise, edge, intensity);
                fixed3 color = _TerriasEffectMode > 0.5 ? starColor : foilColor;
                fixed4 result = fixed4(color, mask);
                if (_TerriasOverlayMode > 0.5)
                {
                    if (_TerriasEffectMode > 0.5)
                    {
                        result = buildStardustOverlay(i.uv, overlayMask, time, noise, overlayEdge, overlayIntensity);
                    }
                    else
                    {
                        result = buildFoilOverlay(i.uv, overlayMask, time, noise, overlayEdge, overlayIntensity);
                    }

                    result.rgb *= i.color.rgb;
                    result.a *= i.color.a;
                }

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif

                return result;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
