Shader "AuraCg/MaskedInvertFlash"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _AuraCgFlashStrength ("Flash Strength", Range(0, 1)) = 0.82
        _AuraCgKeyThreshold ("Key Threshold", Range(0, 1)) = 0.02
        _AuraCgKeySoftness ("Key Softness", Range(0.001, 1)) = 0.1
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

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend OneMinusDstColor OneMinusSrcColor

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _AuraCgFlashStrength;
            float _AuraCgKeyThreshold;
            float _AuraCgKeySoftness;

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
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord) * i.color;
                float luma = dot(tex.rgb, float3(0.299, 0.587, 0.114));
                float mask = smoothstep(_AuraCgKeyThreshold, _AuraCgKeyThreshold + _AuraCgKeySoftness, luma) * tex.a;
                float flash = saturate(mask * _AuraCgFlashStrength);
                return fixed4(flash, flash, flash, flash);
            }
            ENDCG
        }
    }
}
