Shader "Hidden/RotateTexture"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Rotation ("Rotation (radians)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float _Rotation;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Rotate around the center.
                float2 uv = i.uv - 0.5;
                float cosR = cos(_Rotation);
                float sinR = sin(_Rotation);
                float2 rotatedUV;
                rotatedUV.x = uv.x * cosR - uv.y * sinR;
                rotatedUV.y = uv.x * sinR + uv.y * cosR;
                rotatedUV += 0.5;

                // Make pixels outside the range transparent.
                if (rotatedUV.x < 0 || rotatedUV.x > 1 || rotatedUV.y < 0 || rotatedUV.y > 1)
                    return float4(0, 0, 0, 0);

                fixed4 col = tex2D(_MainTex, rotatedUV);
                return col;
            }
            ENDCG
        }
    }
}
