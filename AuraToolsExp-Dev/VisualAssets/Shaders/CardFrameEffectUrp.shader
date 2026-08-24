Shader "AuraTools/CardFrameEffectURP"
{
    Properties
    {
        _MainTex ("Card Frame", 2D) = "white" {}
        _NoiseTex ("Effect Noise", 2D) = "white" {}
        _FoilTex ("Foil Texture", 2D) = "white" {}
        _TerriasEffectMode ("Effect Mode", Float) = 0
        _TerriasOverlayMode ("Overlay Mode", Float) = 0
        _TerriasFrameOnlyOverlay ("Frame Only Overlay", Float) = 0
        _TerriasFoilMode ("Foil Mode", Float) = 1
        _TerriasFlowSpeed ("Flow Speed", Float) = 0.55
        _TerriasFlowScale ("Flow Scale", Float) = 1.22
        _TerriasEffectIntensity ("Effect Intensity", Range(0, 2)) = 1
        _TerriasRainbowStrength ("Rainbow Strength", Range(0, 2)) = 1.25
        _TerriasStardustDensity ("Stardust Density", Range(0, 1)) = 0.38
        _TerriasStardustTwinkle ("Stardust Twinkle", Range(0, 2)) = 1
        _TerriasStardustTwinkleSpeed ("Stardust Twinkle Speed", Float) = 1
        _TerriasHoloColorA ("Holo Color A", Color) = (1, 0.94, 0.65, 1)
        _TerriasHoloColorB ("Holo Color B", Color) = (0.65, 0.95, 1, 1)
        _TerriasHoloColorC ("Holo Color C", Color) = (0.82, 0.72, 1, 1)
        _TerriasStardustColorA ("Stardust Core", Color) = (0.86, 0.94, 1, 1)
        _TerriasStardustColorB ("Stardust Warm", Color) = (1, 0.85, 0.44, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "CardFrameEffectURP"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _NoiseTex;
            sampler2D _FoilTex;
            float4 _MainTex_ST;
            float _TerriasEffectMode;
            float _TerriasOverlayMode;
            float _TerriasFrameOnlyOverlay;
            float _TerriasFoilMode;
            float _TerriasFlowSpeed;
            float _TerriasFlowScale;
            float _TerriasEffectIntensity;
            float _TerriasRainbowStrength;
            float _TerriasStardustDensity;
            float _TerriasStardustTwinkle;
            float _TerriasStardustTwinkleSpeed;
            half4 _TerriasHoloColorA;
            half4 _TerriasHoloColorB;
            half4 _TerriasHoloColorC;
            half4 _TerriasStardustColorA;
            half4 _TerriasStardustColorB;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = UnityObjectToClipPos(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            half3 HoloRamp(float value)
            {
                float3 waves = 0.5 + 0.5 * cos(6.2831853 * (frac(value) + float3(0, 0.33, 0.67)));
                waves = pow(saturate(waves), 1.18);
                float sum = max(waves.r + waves.g + waves.b, 0.001);
                return (_TerriasHoloColorA.rgb * waves.r
                        + _TerriasHoloColorB.rgb * waves.g
                        + _TerriasHoloColorC.rgb * waves.b) / sum;
            }

            half3 ApplyFoil(half3 baseColor, float2 uv, float mask, float time, half3 noise)
            {
                half3 foilTexture = tex2D(
                    _FoilTex,
                    uv * float2(1.55, 0.82) + float2(time * 0.035, -time * 0.018)).rgb;
                float axis = uv.x * 0.84 - uv.y * 0.54 + noise.b * 0.12 + time * 0.045;
                half3 rainbow = HoloRamp(axis * max(_TerriasFlowScale, 0.1) * 4.2)
                                * _TerriasRainbowStrength;
                float sweep = pow(saturate(1.0 - abs(frac((uv.x + uv.y + time * 0.12) * 3.2) - 0.5) * 2.0), 4.0);
                float grain = saturate(dot(foilTexture, half3(0.299, 0.587, 0.114)));
                float strength = saturate((sweep * 0.62 + grain * 0.38) * _TerriasEffectIntensity * mask);
                return saturate(baseColor + rainbow * strength * 0.42 + foilTexture * strength * 0.16);
            }

            half3 ApplyStardust(half3 baseColor, float2 uv, float mask, float time, half3 noise)
            {
                float2 grid = uv * 34.0 + float2(time * 0.18, -time * 0.13);
                float2 cell = floor(grid);
                float2 local = frac(grid) - 0.5;
                float seed = Hash21(cell);
                float gate = smoothstep(1.0 - _TerriasStardustDensity * 0.52, 1.0, seed);
                float cross = max(1.0 - abs(local.x) * 13.0, 0.0) * max(1.0 - abs(local.y) * 2.3, 0.0)
                              + max(1.0 - abs(local.y) * 13.0, 0.0) * max(1.0 - abs(local.x) * 2.3, 0.0);
                float twinkle = 0.5 + 0.5 * sin(
                    time * _TerriasStardustTwinkleSpeed * (3.7 + seed * 4.6) + seed * 18.0);
                float dust = saturate(cross * gate * smoothstep(0.18, 1.0, twinkle));
                float band = 0.5 + 0.5 * sin((uv.x * 0.72 + uv.y * 1.35 + time * 0.1) * 6.2831853);
                half3 tint = lerp(_TerriasStardustColorA.rgb, _TerriasStardustColorB.rgb, band * 0.6 + noise.b * 0.2);
                float strength = dust * _TerriasStardustTwinkle * _TerriasEffectIntensity * mask;
                return saturate(baseColor + tint * strength * 0.72);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 frame = tex2D(_MainTex, input.uv);
                half3 noise = tex2D(
                    _NoiseTex,
                    input.uv * 4.0 + float2(_Time.y * 0.03, -_Time.y * 0.02)).rgb;
                float mask = saturate(frame.a);
                float time = _Time.y * _TerriasFlowSpeed;
                half3 color = _TerriasEffectMode > 0.5
                    ? ApplyStardust(frame.rgb, input.uv, mask, time, noise)
                    : ApplyFoil(frame.rgb, input.uv, mask, time, noise);
                return half4(color, frame.a);
            }
            ENDCG
        }
    }
}
