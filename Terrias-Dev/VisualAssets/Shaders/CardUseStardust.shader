Shader "Terrias/CardUseStardust"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "gray" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TerriasOverlayMode ("Overlay Mode", Float) = 1
        _TerriasFlowSpeed ("Flow Speed", Float) = 0.72
        _TerriasFlowScale ("Flow Scale", Float) = 1.5
        _TerriasNoiseScale ("Noise Scale", Float) = 6.2
        _TerriasDistortion ("Distortion", Range(0, 0.05)) = 0.004
        _TerriasEffectIntensity ("Intensity", Range(0, 2)) = 0.95
        _TerriasQualityScale ("Quality", Range(0.5, 1)) = 1
        _TerriasEdgeGlow ("Edge Glow", Range(0, 2)) = 0.12
        _TerriasStardustGrain ("Fine Grain", Range(0, 1)) = 0.28
        _TerriasStardustDensity ("Density", Range(0, 1)) = 0.46
        _TerriasStardustTwinkle ("Twinkle", Range(0, 2)) = 1.25
        _TerriasStardustTwinkleSpeed ("Twinkle Speed", Float) = 2.15
        _TerriasStardustOrbit ("Orbit", Range(0, 1)) = 0.18
        _TerriasStardustGlowRadius ("Glow Radius", Range(0.03, 0.7)) = 0.18
        _TerriasStardustGlowPower ("Glow Power", Range(1, 8)) = 5.4
        _TerriasStardustSweepSpeed ("Sweep Speed", Float) = 1.85
        _TerriasStardustSweepIntensity ("Sweep Intensity", Range(0, 2)) = 0.62
        _TerriasStardustSweepWidth ("Sweep Width", Range(0.01, 0.35)) = 0.045
        _TerriasEdgeSample ("Edge Sample", Range(1, 4)) = 2
        _TerriasStardustColorA ("Core Color", Color) = (0.953,0.984,1,1)
        _TerriasStardustColorB ("Warm Color", Color) = (1,0.902,0.659,1)

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
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float4 _ClipRect;
            float _TerriasOverlayMode;
            float _TerriasFlowSpeed;
            float _TerriasFlowScale;
            float _TerriasNoiseScale;
            float _TerriasDistortion;
            float _TerriasEffectIntensity;
            float _TerriasQualityScale;
            float _TerriasEdgeGlow;
            float _TerriasStardustGrain;
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
            fixed4 _TerriasStardustColorA;
            fixed4 _TerriasStardustColorB;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            float hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            float starLayer(float2 uv, float scale, float time, float density)
            {
                float2 grid = uv * scale;
                float2 cell = floor(grid);
                float2 local = frac(grid) - 0.5;
                float seed = hash21(cell);
                float active = step(1.0 - density * 0.18, seed);
                float size = lerp(0.045, 0.14, hash21(cell + 17.3));
                float core = smoothstep(size, 0.0, length(local));
                float rays = smoothstep(size * 0.42, 0.0, min(abs(local.x), abs(local.y)));
                float twinkle = 0.5 + 0.5 * sin(time * _TerriasStardustTwinkleSpeed * (3.5 + seed * 4.0) + seed * 19.0);
                return active * saturate(core + rays * 0.45) * lerp(0.3, 1.0, twinkle) * _TerriasStardustTwinkle;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float time = _Time.y * _TerriasFlowSpeed;
                float2 noiseUv = input.uv * max(0.1, _TerriasNoiseScale) + float2(time * 0.07, -time * 0.05);
                float3 noise = tex2D(_NoiseTex, noiseUv).rgb;
                float2 uv = input.uv + (noise.rg * 2.0 - 1.0) * _TerriasDistortion;
                fixed4 face = tex2D(_MainTex, uv) * input.color;
                float mask = face.a;

                float2 edgeStep = _MainTex_TexelSize.xy * max(1.0, _TerriasEdgeSample);
                float around = max(max(tex2D(_MainTex, uv + float2(edgeStep.x, 0)).a,
                                       tex2D(_MainTex, uv - float2(edgeStep.x, 0)).a),
                                   max(tex2D(_MainTex, uv + float2(0, edgeStep.y)).a,
                                       tex2D(_MainTex, uv - float2(0, edgeStep.y)).a));
                float edge = saturate(around - mask + mask * 0.08);

                float2 flowUv = (uv - 0.5) * _TerriasFlowScale + 0.5;
                float stars = starLayer(flowUv + noise.rg * 0.03, 34.0, time, _TerriasStardustDensity)
                              + starLayer(flowUv * 1.31 + float2(0.17, 0.29), 49.0, -time * 0.73, _TerriasStardustDensity * 0.72) * 0.55;
                float fine = pow(saturate(noise.b * 0.75 + hash21(floor(flowUv * 128.0)) * 0.35), 7.0)
                             * _TerriasStardustGrain;
                float diagonal = flowUv.x + flowUv.y * 0.58;
                float sweepPosition = frac(time * 0.31 * _TerriasStardustSweepSpeed + noise.r * 0.05) * 2.2 - 0.6;
                float sweep = smoothstep(_TerriasStardustSweepWidth, 0.0, abs(diagonal - sweepPosition))
                              * _TerriasStardustSweepIntensity;
                float orbitDistance = abs(length(flowUv - 0.5) - _TerriasStardustGlowRadius);
                float orbit = pow(saturate(1.0 - orbitDistance / max(0.03, _TerriasStardustGlowRadius)), _TerriasStardustGlowPower)
                              * _TerriasStardustOrbit;
                float energy = saturate(stars + fine * 0.58 + sweep + orbit + edge * _TerriasEdgeGlow)
                               * _TerriasEffectIntensity * _TerriasQualityScale;
                fixed3 tint = lerp(_TerriasStardustColorA.rgb, _TerriasStardustColorB.rgb,
                                   saturate(noise.g * 0.55 + sweep * 0.35));
                fixed4 result = _TerriasOverlayMode > 0.5
                    ? fixed4(tint, saturate(energy * mask))
                    : fixed4(face.rgb + tint * energy, face.a);

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
