Shader "SunExp/WunaOrbitFire"
{
    Properties
    {
        _MainTex ("Legacy Trail Mask", 2D) = "white" {}
        _NoiseTex ("Flow Noise", 2D) = "white" {}
        _SunExpCoreColor ("Core Color", Color) = (1, 0.86, 0.42, 1)
        _SunExpEdgeColor ("Edge Color", Color) = (1, 0.28, 0.08, 1)
        _SunExpSmokeColor ("Smoke Color", Color) = (0.44, 0.08, 0.04, 0.45)
        _SunExpFlowTime ("Flow Time", Float) = 0
        _SunExpIntensity ("Intensity", Range(0, 2)) = 1
        _SunExpLayer ("Layer", Float) = 1
        _SunExpCoreMode ("Core Mode", Float) = 0
        _SunExpNoiseScale ("Noise Scale", Float) = 3.4
        _SunExpDistortion ("Distortion", Float) = 0.16
        _SunExpAlphaCutoff ("Flow Cutoff", Range(0, 1)) = 0.18
        _SunExpAlphaSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.12
        _SunExpFlowSpeed ("Flow Speed", Float) = 0.42
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

            float flowNoise(float2 p)
            {
                float a = tex2D(_NoiseTex, p).r;
                float b = tex2D(_NoiseTex, p * 1.87 + float2(0.19, 0.37)).g;
                return lerp(a, b, 0.42);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float noise = hash21(v.localUv * _SunExpNoiseScale + _SunExpFlowTime * 0.17);
                float side = (v.localUv.y - 0.5) * 2.0;
                float flow = v.trailUv.x - _SunExpFlowTime * _SunExpFlowSpeed * 0.42;
                float bend = sin(flow * 13.0 + noise * 6.28) * _SunExpDistortion;
                v.vertex.xy += float2(side * bend * 0.004, bend * 0.003);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.trailUv = v.trailUv;
                o.localUv = v.localUv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = saturate(i.localUv);
                float side = abs(uv.y * 2.0 - 1.0);
                float flow = i.trailUv.x - _SunExpFlowTime * _SunExpFlowSpeed * (_SunExpLayer > 0.0 ? 1.0 : 0.72);
                float2 noiseUv = float2(flow * 1.35, uv.y * 2.15 + _SunExpLayer * 0.17);
                float noiseA = flowNoise(noiseUv);
                float noiseB = flowNoise(noiseUv * 2.35 + float2(0.13, _SunExpFlowTime * 0.11));
                float hash = hash21(float2(flow * 6.0, uv.y * 4.0 + _SunExpLayer));

                float width = saturate(0.56 + noiseA * 0.24 - noiseB * 0.12);
                float softEdge = max(_SunExpAlphaSoftness, 0.025);
                float body = 1.0 - smoothstep(width, width + softEdge, side);
                float feather = 1.0 - smoothstep(width * 0.44, 1.0, side);
                float coreLine = pow(saturate(1.0 - side), lerp(4.8, 8.0, saturate(_SunExpCoreMode)));

                float wave = sin(flow * 38.0 + noiseA * 5.6 + noiseB * 2.1) * 0.5 + 0.5;
                float streak = pow(saturate(wave + noiseB * 0.32 - side * 0.46), 3.6);
                float head = pow(saturate(sin(flow * 11.0 - _SunExpFlowTime * 1.7 + hash * 1.6) * 0.5 + 0.5), 7.0);
                float brokenTail = smoothstep(_SunExpAlphaCutoff, 0.9, noiseA * 0.62 + streak * 0.56 + head * 0.42);
                float coreMode = saturate(_SunExpCoreMode);
                float alpha = lerp(body * brokenTail * feather, coreLine * (0.62 + head * 0.56), coreMode);
                alpha *= i.color.a * saturate(_SunExpIntensity) * (_SunExpLayer > 0.0 ? 1.14 : 0.78);

                float heat = saturate(coreLine * 0.82 + streak * 0.48 + head * 0.72 + noiseA * 0.22);
                fixed3 edge = lerp(_SunExpSmokeColor.rgb, _SunExpEdgeColor.rgb, saturate(body * 1.6));
                fixed3 hot = lerp(_SunExpEdgeColor.rgb, _SunExpCoreColor.rgb, heat);
                fixed3 color = lerp(edge, hot, saturate(heat + coreMode * 0.36));
                color *= 1.35 + streak * 0.95 + head * 1.25 + coreLine * 1.15;

                clip(alpha - 0.004);
                return fixed4(color, saturate(alpha));
            }
            ENDCG
        }
    }
}
