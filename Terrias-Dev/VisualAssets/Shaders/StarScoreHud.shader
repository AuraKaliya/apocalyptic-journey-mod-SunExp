Shader "SunExp/StarScoreHud"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _NoiseTex ("Flow Noise", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SunExpTint ("SunExp Tint", Color) = (1, 1, 1, 1)
        _SunExpGlowColor ("SunExp Glow Color", Color) = (1, 0.88, 0.54, 1)
        _SunExpFlowColor ("SunExp Flow Color", Color) = (0.62, 0.86, 1, 1)
        _SunExpLitAmount ("SunExp Lit Amount", Range(0, 1)) = 0
        _SunExpPulse ("SunExp Pulse", Range(0, 1)) = 0
        _SunExpFlowTime ("SunExp Flow Time", Float) = 0
        _SunExpFlowStrength ("SunExp Flow Strength", Range(0, 1)) = 0
        _SunExpSlotIndex ("SunExp Slot Index", Float) = 0
        _SunExpFlowSpeed ("SunExp Flow Speed", Float) = 0.55
        _SunExpFlowScale ("SunExp Flow Scale", Float) = 1.2
        _SunExpEdgeGlow ("SunExp Edge Glow", Range(0, 1)) = 0.35

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
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _SunExpTint;
            fixed4 _SunExpGlowColor;
            fixed4 _SunExpFlowColor;
            float _SunExpLitAmount;
            float _SunExpPulse;
            float _SunExpFlowTime;
            float _SunExpFlowStrength;
            float _SunExpSlotIndex;
            float _SunExpFlowSpeed;
            float _SunExpFlowScale;
            float _SunExpEdgeGlow;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseColor = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;
                float alpha = baseColor.a;
                float lit = saturate(_SunExpLitAmount);
                float pulse = saturate(_SunExpPulse);
                float flowStrength = saturate(_SunExpFlowStrength);

                float2 flowUv = i.texcoord * max(_SunExpFlowScale, 0.001)
                    + float2(_SunExpFlowTime * _SunExpFlowSpeed, _SunExpSlotIndex * 0.173);
                float noise = tex2D(_NoiseTex, flowUv).r;
                float flow = saturate((noise - 0.42) * 2.25) * flowStrength;
                float edge = smoothstep(0.02, 0.34, alpha) * (1.0 - smoothstep(0.55, 1.0, alpha));

                fixed3 litColor = lerp(baseColor.rgb * 0.72, baseColor.rgb * _SunExpTint.rgb, lit);
                litColor += _SunExpFlowColor.rgb * flow * 0.28;
                litColor += _SunExpGlowColor.rgb * edge * _SunExpEdgeGlow * max(lit, pulse);
                litColor = lerp(litColor, _SunExpGlowColor.rgb, pulse * 0.38);

                baseColor.rgb = litColor;
                baseColor.a = alpha * saturate(0.35 + lit * 0.65 + pulse * 0.18);

                #ifdef UNITY_UI_CLIP_RECT
                baseColor.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(baseColor.a - 0.001);
                #endif

                return baseColor;
            }
            ENDCG
        }
    }
}
