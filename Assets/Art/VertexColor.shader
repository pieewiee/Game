// GNP flat-shaded vertex-color shader, Built-in pipeline.
// One material for the whole game (art-bible.md): no textures, no PBR, no
// normal maps. Lighting = one directional light + ambient, per flat face.
Shader "GNP/VertexColor"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                float3 light = _LightColor0.rgb * ndl + ShadeSH9(float4(n, 1));
                o.color = float4(v.color.rgb * light, v.color.a);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
