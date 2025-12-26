Shader "Hidden/TilePreviewRotate"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Rot ("Rotation", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Rot;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                int r = (int)fmod(max(0, (int)round(_Rot)), 4);
                float2 src;
                if (r == 0)
                    src = uv;
                else if (r == 1)
                    src = float2(1.0 - uv.y, uv.x); // 90° CW
                else if (r == 2)
                    src = float2(1.0 - uv.x, 1.0 - uv.y); // 180°
                else // r == 3
                    src = float2(uv.y, 1.0 - uv.x); // 270° CW

                return tex2D(_MainTex, src);
            }
            ENDCG
        }
    }
}
