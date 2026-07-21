Shader "Terrias/StarScoreHud"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _NoiseTex ("Flow Noise", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TerriasTint ("Terrias Tint", Color) = (1, 1, 1, 1)
        _TerriasGlowColor ("Terrias Glow Color", Color) = (1, 0.88, 0.54, 1)
        _TerriasFlowColor ("Terrias Flow Color", Color) = (0.62, 0.86, 1, 1)
        _TerriasLitAmount ("Terrias Lit Amount", Range(0, 1)) = 0
        _TerriasPulse ("Terrias Pulse", Range(0, 1)) = 0
        _TerriasFlowTime ("Terrias Flow Time", Float) = 0
        _TerriasFlowStrength ("Terrias Flow Strength", Range(0, 1)) = 0
        _TerriasSlotIndex ("Terrias Slot Index", Float) = 0
        _TerriasFlowSpeed ("Terrias Flow Speed", Float) = 0.55
        _TerriasFlowScale ("Terrias Flow Scale", Float) = 1.2
        _TerriasEdgeGlow ("Terrias Edge Glow", Range(0, 1)) = 0.35

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

            fixed4 _TerriasTint;
            fixed4 _TerriasGlowColor;
            fixed4 _TerriasFlowColor;
            float _TerriasLitAmount;
            float _TerriasPulse;
            float _TerriasFlowTime;
            float _TerriasFlowStrength;
            float _TerriasSlotIndex;
            float _TerriasFlowSpeed;
            float _TerriasFlowScale;
            float _TerriasEdgeGlow;

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
                float lit = saturate(_TerriasLitAmount);
                float pulse = saturate(_TerriasPulse);
                float flowStrength = saturate(_TerriasFlowStrength);

                float2 flowUv = i.texcoord * max(_TerriasFlowScale, 0.001)
                    + float2(_TerriasFlowTime * _TerriasFlowSpeed, _TerriasSlotIndex * 0.173);
                float noise = tex2D(_NoiseTex, flowUv).r;
                float flow = saturate((noise - 0.42) * 2.25) * flowStrength;
                float edge = smoothstep(0.02, 0.34, alpha) * (1.0 - smoothstep(0.55, 1.0, alpha));

                fixed3 litColor = lerp(baseColor.rgb * 0.72, baseColor.rgb * _TerriasTint.rgb, lit);
                litColor += _TerriasFlowColor.rgb * flow * 0.28;
                litColor += _TerriasGlowColor.rgb * edge * _TerriasEdgeGlow * max(lit, pulse);
                litColor = lerp(litColor, _TerriasGlowColor.rgb, pulse * 0.38);

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
