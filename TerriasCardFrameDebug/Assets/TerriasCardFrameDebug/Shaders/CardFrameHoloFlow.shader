Shader "Terrias/CardFrameHoloFlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Frame Sprite", 2D) = "white" {}
        _NoiseTex ("Flow Noise", 2D) = "white" {}
        _TextureSampleAdd ("Texture Sample Add", Vector) = (0, 0, 0, 0)
        _TerriasHoloColorA ("Holo Color A", Color) = (1, 0.78, 0.32, 1)
        _TerriasHoloColorB ("Holo Color B", Color) = (0.42, 0.92, 1, 1)
        _TerriasHoloColorC ("Holo Color C", Color) = (1, 0.42, 0.86, 1)
        _TerriasFlowSpeed ("Flow Speed", Float) = 0.36
        _TerriasFlowScale ("Flow Scale", Float) = 1.65
        _TerriasNoiseScale ("Noise Scale", Float) = 4.8
        _TerriasDistortion ("Distortion", Float) = 0.018
        _TerriasEffectIntensity ("Effect Intensity", Range(0, 2)) = 0.72
        _TerriasQualityScale ("Quality Scale", Range(0, 1)) = 1
        _TerriasEdgeGlow ("Edge Glow", Range(0, 1)) = 0.22
        _TerriasSweepFrequency ("Sweep Frequency", Float) = 5.6
        _TerriasSweepWidth ("Sweep Width", Range(0.01, 1)) = 0.16
        _TerriasSweepIntensity ("Sweep Intensity", Range(0, 2)) = 0.9
        _TerriasPrismScale ("Prism Scale", Float) = 14
        _TerriasPrismStrength ("Prism Strength", Range(0, 1)) = 0.68
        _TerriasFoilGrain ("Foil Grain", Range(0, 1)) = 0.26
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
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            fixed4 _TerriasHoloColorA;
            fixed4 _TerriasHoloColorB;
            fixed4 _TerriasHoloColorC;
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

            float frameAlpha(float2 uv)
            {
                return saturate((tex2D(_MainTex, uv) + _TextureSampleAdd).a);
            }

            float innerEdge(float2 uv, float mask)
            {
                float2 stepUv = _MainTex_TexelSize.xy * max(_TerriasEdgeSample, 0.5);
                float neighbor = min(frameAlpha(uv + float2(stepUv.x, 0.0)), frameAlpha(uv - float2(stepUv.x, 0.0)));
                neighbor = min(neighbor, frameAlpha(uv + float2(0.0, stepUv.y)));
                neighbor = min(neighbor, frameAlpha(uv - float2(0.0, stepUv.y)));
                return saturate((mask - neighbor) * 5.5);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 frame = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                float mask = saturate(frame.a);
                float time = _Time.y * _TerriasFlowSpeed;

                float2 noiseUv = i.uv * _TerriasNoiseScale + float2(time * 0.19, -time * 0.13);
                float3 noise = tex2D(_NoiseTex, noiseUv).rgb;
                float2 microFlow = float2(hash21(i.uv * 37.0), hash21(i.uv * 41.0)) - 0.5;
                float2 flow = ((noise.rg * 2.0 - 1.0) + microFlow * 0.22) * _TerriasDistortion;
                float2 uv = i.uv + flow * mask;

                float sweepAxis = (uv.x * 0.78 + uv.y * 1.18) * _TerriasSweepFrequency - time * 0.72 + noise.b * 0.18;
                float sweepLine = abs(frac(sweepAxis) - 0.5) * 2.0;
                float sweep = smoothstep(_TerriasSweepWidth, 0.0, sweepLine) * _TerriasSweepIntensity;

                float prismA = sin((uv.x * 1.23 + uv.y * 0.71 + noise.b * 0.24 + time * 0.08) * _TerriasPrismScale);
                float prismB = sin((-uv.x * 0.48 + uv.y * 1.57 + noise.g * 0.18 - time * 0.11) * _TerriasPrismScale * 1.87);
                float prism = saturate(pow(prismA * 0.5 + 0.5, 1.55) * 0.7 + pow(prismB * 0.5 + 0.5, 4.0) * 0.3);

                float shimmerSeed = hash21(floor(uv * 96.0) + floor(time * 4.0));
                float shimmer = pow(saturate(noise.r * 0.62 + shimmerSeed * 0.42), 7.0) * _TerriasFoilGrain;
                float flowBand = sin((uv.x - uv.y * 0.45 + noise.g * 0.18 + time * 0.18) * _TerriasFlowScale * 6.2831853) * 0.5 + 0.5;

                float edge = innerEdge(i.uv, mask) * _TerriasEdgeGlow;
                float intensity = _TerriasEffectIntensity * _TerriasQualityScale * mask;
                fixed3 holo = holoRamp(uv.x * 0.92 + uv.y * 0.68 + prism * 0.28 + noise.b * 0.18 + time * 0.045);
                float foil = saturate(prism * 0.48 + flowBand * 0.2 + sweep * 0.72 + shimmer + edge);

                fixed3 foilColor = lerp(frame.rgb, saturate(frame.rgb * 0.72 + holo * 0.9), _TerriasPrismStrength);
                fixed3 color = lerp(frame.rgb, foilColor, foil * intensity);
                color += holo * (sweep * 0.24 + edge * 0.35) * intensity;
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
