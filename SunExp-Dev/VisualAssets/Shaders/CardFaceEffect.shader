Shader "SunExp/CardFaceEffect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card Face Sprite", 2D) = "white" {}
        _NoiseTex ("Effect Noise", 2D) = "white" {}
        _TextureSampleAdd ("Texture Sample Add", Vector) = (0, 0, 0, 0)
        _SunExpEffectMode ("Effect Mode", Float) = 0
        _SunExpHoloColorA ("Holo Color A", Color) = (1, 0.78, 0.32, 1)
        _SunExpHoloColorB ("Holo Color B", Color) = (0.42, 0.92, 1, 1)
        _SunExpHoloColorC ("Holo Color C", Color) = (1, 0.42, 0.86, 1)
        _SunExpStardustColorA ("Stardust Core", Color) = (0.86, 0.94, 1, 1)
        _SunExpStardustColorB ("Stardust Warm", Color) = (1, 0.85, 0.44, 1)
        _SunExpFlowSpeed ("Flow Speed", Float) = 0.36
        _SunExpFlowScale ("Flow Scale", Float) = 1.65
        _SunExpNoiseScale ("Noise Scale", Float) = 4.8
        _SunExpDistortion ("Distortion", Float) = 0.018
        _SunExpEffectIntensity ("Effect Intensity", Range(0, 2)) = 0.72
        _SunExpQualityScale ("Quality Scale", Range(0, 1)) = 1
        _SunExpEdgeGlow ("Edge Glow", Range(0, 1)) = 0.22
        _SunExpSweepFrequency ("Sweep Frequency", Float) = 5.6
        _SunExpSweepWidth ("Sweep Width", Range(0.01, 1)) = 0.16
        _SunExpSweepIntensity ("Sweep Intensity", Range(0, 2)) = 0.9
        _SunExpPrismScale ("Prism Scale", Float) = 14
        _SunExpPrismStrength ("Prism Strength", Range(0, 1)) = 0.68
        _SunExpFoilGrain ("Foil Grain", Range(0, 1)) = 0.26
        _SunExpMirrorSweep ("Mirror Sweep", Range(0, 2)) = 0.55
        _SunExpSwirlStrength ("Swirl Strength", Range(0, 1)) = 0.2
        _SunExpStardustDensity ("Stardust Density", Range(0, 1)) = 0.38
        _SunExpStardustTwinkle ("Stardust Twinkle", Range(0, 2)) = 1.0
        _SunExpStardustOrbit ("Stardust Orbit", Range(0, 1)) = 0.32
        _SunExpEdgeSample ("Edge Sample", Float) = 2

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
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _SunExpEffectMode;
            fixed4 _SunExpHoloColorA;
            fixed4 _SunExpHoloColorB;
            fixed4 _SunExpHoloColorC;
            fixed4 _SunExpStardustColorA;
            fixed4 _SunExpStardustColorB;
            float _SunExpFlowSpeed;
            float _SunExpFlowScale;
            float _SunExpNoiseScale;
            float _SunExpDistortion;
            float _SunExpEffectIntensity;
            float _SunExpQualityScale;
            float _SunExpEdgeGlow;
            float _SunExpSweepFrequency;
            float _SunExpSweepWidth;
            float _SunExpSweepIntensity;
            float _SunExpPrismScale;
            float _SunExpPrismStrength;
            float _SunExpFoilGrain;
            float _SunExpMirrorSweep;
            float _SunExpSwirlStrength;
            float _SunExpStardustDensity;
            float _SunExpStardustTwinkle;
            float _SunExpStardustOrbit;
            float _SunExpEdgeSample;

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
                fixed3 color = _SunExpHoloColorA.rgb * waves.r
                    + _SunExpHoloColorB.rgb * waves.g
                    + _SunExpHoloColorC.rgb * waves.b;
                return color / sum;
            }

            float faceAlpha(float2 uv)
            {
                return saturate((tex2D(_MainTex, uv) + _TextureSampleAdd).a);
            }

            float innerEdge(float2 uv, float mask)
            {
                float2 stepUv = _MainTex_TexelSize.xy * max(_SunExpEdgeSample, 0.5);
                float neighbor = min(faceAlpha(uv + float2(stepUv.x, 0.0)), faceAlpha(uv - float2(stepUv.x, 0.0)));
                neighbor = min(neighbor, faceAlpha(uv + float2(0.0, stepUv.y)));
                neighbor = min(neighbor, faceAlpha(uv - float2(0.0, stepUv.y)));
                return saturate((mask - neighbor) * 5.5);
            }

            float starCell(float2 uv, float time, float density)
            {
                float2 grid = uv * 34.0 + float2(time * 0.12, -time * 0.08);
                float2 cell = floor(grid);
                float2 local = frac(grid) - 0.5;
                float seed = hash21(cell);
                float gate = smoothstep(1.0 - density * 0.52, 1.0, seed);
                float cross = max(1.0 - abs(local.x) * 13.0, 0.0) * max(1.0 - abs(local.y) * 2.3, 0.0)
                    + max(1.0 - abs(local.y) * 13.0, 0.0) * max(1.0 - abs(local.x) * 2.3, 0.0);
                float core = pow(saturate(1.0 - length(local) * 3.2), 5.0);
                float twinkle = 0.62 + 0.38 * sin(time * (2.2 + seed * 3.0) + seed * 18.0);
                return saturate((cross * 0.7 + core) * gate * twinkle);
            }

            float stardustField(float2 uv, float time, float3 noise)
            {
                float2 centered = uv - 0.5;
                float radius = length(centered);
                float angle = atan2(centered.y, centered.x);
                float orbit = sin(angle * 3.0 + radius * 21.0 - time * 0.72 + noise.b * 1.2) * 0.5 + 0.5;
                orbit = pow(orbit, 4.0) * smoothstep(0.62, 0.08, radius) * _SunExpStardustOrbit;

                float dustA = starCell(uv + noise.rg * 0.035, time, _SunExpStardustDensity);
                float dustB = starCell(uv * 1.37 + float2(0.11, 0.29), -time * 0.73, _SunExpStardustDensity * 0.72);
                float fine = pow(saturate(noise.r * 0.8 + hash21(floor(uv * 112.0)) * 0.35), 8.0) * _SunExpFoilGrain;
                return saturate((dustA + dustB * 0.65) * _SunExpStardustTwinkle + orbit + fine);
            }

            fixed3 applyFoil(fixed3 baseColor, float2 uv, float mask, float time, float3 noise, float edge, float intensity)
            {
                float2 microFlow = float2(hash21(uv * 37.0), hash21(uv * 41.0)) - 0.5;
                float2 flow = ((noise.rg * 2.0 - 1.0) + microFlow * 0.22) * _SunExpDistortion;
                float2 effectUv = uv + flow * mask;

                float2 centered = effectUv - 0.5;
                float swirl = (atan2(centered.y, centered.x) * 0.1591549 + length(centered) * 2.8) * _SunExpSwirlStrength;
                float sweepAxis = (effectUv.x * 0.78 + effectUv.y * 1.18 + swirl) * _SunExpSweepFrequency - time * 0.72 + noise.b * 0.18;
                float sweepLine = abs(frac(sweepAxis) - 0.5) * 2.0;
                float sweep = smoothstep(_SunExpSweepWidth, 0.0, sweepLine) * _SunExpSweepIntensity;
                float mirror = pow(saturate(1.0 - abs(effectUv.x + effectUv.y * 0.42 - frac(time * 0.18) * 1.5 + 0.3) * 2.6), 4.0) * _SunExpMirrorSweep;

                float prismA = sin((effectUv.x * 1.23 + effectUv.y * 0.71 + noise.b * 0.24 + time * 0.08 + swirl) * _SunExpPrismScale);
                float prismB = sin((-effectUv.x * 0.48 + effectUv.y * 1.57 + noise.g * 0.18 - time * 0.11) * _SunExpPrismScale * 1.87);
                float prism = saturate(pow(prismA * 0.5 + 0.5, 1.55) * 0.7 + pow(prismB * 0.5 + 0.5, 4.0) * 0.3);

                float shimmerSeed = hash21(floor(effectUv * 112.0) + floor(time * 4.0));
                float shimmer = pow(saturate(noise.r * 0.62 + shimmerSeed * 0.42), 7.0) * _SunExpFoilGrain;
                float flowBand = sin((effectUv.x - effectUv.y * 0.45 + noise.g * 0.18 + time * 0.18) * _SunExpFlowScale * 6.2831853) * 0.5 + 0.5;
                fixed3 holo = holoRamp(effectUv.x * 0.92 + effectUv.y * 0.68 + prism * 0.28 + noise.b * 0.18 + time * 0.045);
                float foil = saturate(prism * 0.36 + flowBand * 0.16 + sweep * 0.62 + mirror + shimmer + edge);

                fixed3 foilColor = lerp(baseColor, saturate(baseColor * 0.74 + holo * 0.82), _SunExpPrismStrength);
                fixed3 color = lerp(baseColor, foilColor, foil * intensity);
                color += holo * (sweep * 0.18 + mirror * 0.18 + edge * 0.28) * intensity;
                return color;
            }

            fixed3 applyStardust(fixed3 baseColor, float2 uv, float mask, float time, float3 noise, float edge, float intensity)
            {
                float dust = stardustField(uv, time, noise) * mask;
                float slowBand = sin((uv.x * 0.72 + uv.y * 1.35 + time * 0.1) * 6.2831853) * 0.5 + 0.5;
                fixed3 starColor = lerp(_SunExpStardustColorA.rgb, _SunExpStardustColorB.rgb, slowBand * 0.55 + noise.b * 0.24);
                fixed3 color = baseColor + starColor * dust * intensity * 0.78;
                color = lerp(color, saturate(baseColor * 0.84 + starColor * 0.62), dust * intensity * 0.24);
                color += _SunExpStardustColorA.rgb * edge * intensity * 0.18;
                return color;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 face = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                float mask = saturate(face.a);
                float time = _Time.y * _SunExpFlowSpeed;
                float2 noiseUv = i.uv * _SunExpNoiseScale + float2(time * 0.19, -time * 0.13);
                float3 noise = tex2D(_NoiseTex, noiseUv).rgb;
                float edge = innerEdge(i.uv, mask) * _SunExpEdgeGlow;
                float intensity = _SunExpEffectIntensity * _SunExpQualityScale * mask;

                fixed3 foilColor = applyFoil(face.rgb, i.uv, mask, time, noise, edge, intensity);
                fixed3 starColor = applyStardust(face.rgb, i.uv, mask, time, noise, edge, intensity);
                fixed3 color = _SunExpEffectMode > 0.5 ? starColor : foilColor;
                fixed4 result = fixed4(color, mask);

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
