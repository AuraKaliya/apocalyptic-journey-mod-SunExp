Shader "SunExp/CardFrameHoloFlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Frame Sprite", 2D) = "white" {}
        _NoiseTex ("Flow Noise", 2D) = "white" {}
        _TextureSampleAdd ("Texture Sample Add", Vector) = (0, 0, 0, 0)
        _SunExpHoloColorA ("Holo Color A", Color) = (1, 0.78, 0.32, 1)
        _SunExpHoloColorB ("Holo Color B", Color) = (0.42, 0.92, 1, 1)
        _SunExpHoloColorC ("Holo Color C", Color) = (1, 0.42, 0.86, 1)
        _SunExpFlowSpeed ("Flow Speed", Float) = 0.42
        _SunExpFlowScale ("Flow Scale", Float) = 2.2
        _SunExpNoiseScale ("Noise Scale", Float) = 3.4
        _SunExpDistortion ("Distortion", Float) = 0.035
        _SunExpEffectIntensity ("Effect Intensity", Range(0, 2)) = 0.78
        _SunExpQualityScale ("Quality Scale", Range(0, 1)) = 1
        _SunExpEdgeGlow ("Edge Glow", Range(0, 1)) = 0.28
        _SunExpSweepFrequency ("Sweep Frequency", Float) = 7.5
        _SunExpSweepWidth ("Sweep Width", Range(0.01, 1)) = 0.22

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
            sampler2D _NoiseTex;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            fixed4 _SunExpHoloColorA;
            fixed4 _SunExpHoloColorB;
            fixed4 _SunExpHoloColorC;
            float _SunExpFlowSpeed;
            float _SunExpFlowScale;
            float _SunExpNoiseScale;
            float _SunExpDistortion;
            float _SunExpEffectIntensity;
            float _SunExpQualityScale;
            float _SunExpEdgeGlow;
            float _SunExpSweepFrequency;
            float _SunExpSweepWidth;

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
                fixed3 ab = lerp(_SunExpHoloColorA.rgb, _SunExpHoloColorB.rgb, smoothstep(0.0, 0.5, t));
                fixed3 bc = lerp(_SunExpHoloColorB.rgb, _SunExpHoloColorC.rgb, smoothstep(0.35, 0.85, t));
                fixed3 ca = lerp(_SunExpHoloColorC.rgb, _SunExpHoloColorA.rgb, smoothstep(0.72, 1.0, t));
                return t < 0.45 ? ab : (t < 0.82 ? bc : ca);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 frame = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                float mask = saturate(frame.a);
                float time = _Time.y * _SunExpFlowSpeed;

                float2 noiseUv = i.uv * _SunExpNoiseScale + float2(time * 0.19, -time * 0.13);
                float3 noise = tex2D(_NoiseTex, noiseUv).rgb;
                float2 flow = (noise.rg * 2.0 - 1.0) * _SunExpDistortion;
                float2 uv = i.uv + flow * mask;

                float sweepPhase = (uv.x * 0.78 + uv.y * 1.18) * _SunExpSweepFrequency - time;
                float sweep = sin(sweepPhase + noise.b * 2.7) * 0.5 + 0.5;
                sweep = smoothstep(1.0 - _SunExpSweepWidth, 1.0, sweep);

                float shimmerSeed = hash21(floor(uv * 72.0) + floor(time * 2.0));
                float shimmer = pow(saturate(noise.r * 0.72 + shimmerSeed * 0.32), 5.0);
                float band = sin((uv.x - uv.y * 0.45 + noise.g * 0.18 + time * 0.22) * _SunExpFlowScale * 6.2831853);
                band = band * 0.5 + 0.5;

                float edge = smoothstep(0.02, 0.9, mask) * _SunExpEdgeGlow;
                float intensity = _SunExpEffectIntensity * _SunExpQualityScale * mask;
                fixed3 holo = holoRamp(uv.x * 0.95 + uv.y * 0.72 + noise.b * 0.25 + time * 0.06);
                float glow = saturate(sweep * 0.78 + band * 0.22 + shimmer * 0.34 + edge);

                fixed3 color = frame.rgb + holo * glow * intensity;
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
