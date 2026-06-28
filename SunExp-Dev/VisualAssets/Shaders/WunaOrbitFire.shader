Shader "SunExp/WunaOrbitFire"
{
    Properties
    {
        _MainTex ("Trail Mask", 2D) = "white" {}
        _NoiseTex ("Erosion Noise", 2D) = "white" {}
        _SunExpCoreColor ("Core Color", Color) = (1, 0.86, 0.42, 1)
        _SunExpEdgeColor ("Edge Color", Color) = (1, 0.28, 0.08, 1)
        _SunExpSmokeColor ("Smoke Color", Color) = (0.44, 0.08, 0.04, 0.45)
        _SunExpFlowTime ("Flow Time", Float) = 0
        _SunExpIntensity ("Intensity", Range(0, 2)) = 1
        _SunExpLayer ("Layer", Float) = 1
        _SunExpCoreMode ("Core Mode", Float) = 0
        _SunExpNoiseScale ("Noise Scale", Float) = 2.7
        _SunExpDistortion ("Distortion", Float) = 0.22
        _SunExpAlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.035
        _SunExpAlphaSoftness ("Alpha Softness", Range(0.001, 0.5)) = 0.08
        _SunExpFlowSpeed ("Flow Speed", Float) = 0.65
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 trailUv : TEXCOORD0;
                float2 localUv : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 trailUv : TEXCOORD0;
                float2 localUv : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            fixed4 _SunExpCoreColor;
            fixed4 _SunExpEdgeColor;
            fixed4 _SunExpSmokeColor;
            float _SunExpFlowTime;
            float _SunExpIntensity;
            float _SunExpLayer;
            float _SunExpCoreMode;
            float _SunExpNoiseScale;
            float _SunExpDistortion;
            float _SunExpAlphaCutoff;
            float _SunExpAlphaSoftness;
            float _SunExpFlowSpeed;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float noise = hash21(v.localUv * _SunExpNoiseScale + _SunExpFlowTime * 0.17);
                float side = (v.localUv.y - 0.5) * 2.0;
                float head = saturate(v.localUv.x);
                float bend = sin(_SunExpFlowTime * 2.4 + v.localUv.x * 7.0 + noise * 6.28) * _SunExpDistortion * head;
                v.vertex.xy += float2(side * bend * 0.006, bend * 0.004);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.trailUv = v.trailUv;
                o.localUv = v.localUv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 mask = tex2D(_MainTex, i.trailUv);
                float2 uv = saturate(i.localUv);
                float2 noiseUv = float2(uv.x * 1.85 - _SunExpFlowTime * _SunExpFlowSpeed, uv.y * 2.35 + _SunExpLayer * 0.13);
                float noiseA = tex2D(_NoiseTex, noiseUv).r;
                float noiseB = hash21(uv * _SunExpNoiseScale + float2(_SunExpFlowTime * 0.31, _SunExpLayer * 0.19));
                float side = abs(uv.y * 2.0 - 1.0);
                float sideFade = 1.0 - smoothstep(0.48 + noiseB * 0.22, 1.0, side);
                float tipFade = smoothstep(0.0, 0.08, uv.x) * (1.0 - smoothstep(0.9, 1.0, uv.x));
                float textureAlpha = smoothstep(_SunExpAlphaCutoff, _SunExpAlphaCutoff + _SunExpAlphaSoftness, mask.a);
                float erosion = smoothstep(0.06, 0.76, mask.r + noiseA * 0.36 - side * 0.2);
                float coreLine = pow(saturate(1.0 - side), 3.2);
                float coreAlpha = coreLine * tipFade * i.color.a * saturate(_SunExpIntensity) * 1.25;
                float detailAlpha = textureAlpha * erosion * sideFade * tipFade * i.color.a * saturate(_SunExpIntensity) * 1.2;
                float coreMode = saturate(_SunExpCoreMode);
                float alpha = lerp(detailAlpha, coreAlpha, coreMode);

                float heat = saturate((1.0 - side) * 0.78 + uv.x * 0.35 + noiseA * 0.22);
                fixed3 sampledColor = max(mask.rgb, mask.rrr * 0.42);
                fixed3 tintColor = lerp(_SunExpEdgeColor.rgb, _SunExpCoreColor.rgb, heat);
                fixed3 detailColor = sampledColor * tintColor * (1.5 + uv.x * 0.55);
                fixed3 coreColor = lerp(_SunExpEdgeColor.rgb, _SunExpCoreColor.rgb, 0.76 + noiseA * 0.18) * (1.9 + coreLine * 1.65);
                fixed3 color = lerp(detailColor, coreColor, coreMode);
                color = lerp(_SunExpSmokeColor.rgb, color, saturate(alpha * 2.2));

                float layerFade = _SunExpLayer > 0.0 ? 1.0 : 0.84;
                clip(alpha - 0.003);
                return fixed4(color, saturate(alpha * layerFade * 1.18));
            }
            ENDCG
        }
    }
}
