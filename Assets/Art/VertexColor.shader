// GNP flat-shaded vertex-color shader, Built-in pipeline.
// One material for the whole game (art-bible.md): no textures, no PBR, no
// normal maps. Lighting = one directional light + ambient + the point lights
// in the rooms, per flat face.
//
// Three passes, because the Built-in forward renderer is three passes:
// ForwardBase carries the sun, the ambient and the sun's shadow; ForwardAdd
// runs once per point/spot light in range and ADDS it; ShadowCaster is what
// makes a slab cast a shadow at all. With only the first, every ceiling
// light rendered as nothing and the sun shone straight through the roof
// into the basement.
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
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float4 color  : COLOR;
                float3 normal : TEXCOORD0;
                float3 wpos   : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_SHADOW_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                UNITY_TRANSFER_SHADOW(o, float2(0, 0));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                UNITY_LIGHT_ATTENUATION(atten, i, i.wpos);
                float ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                float3 light = _LightColor0.rgb * ndl * atten + ShadeSH9(float4(n, 1));
                fixed4 col = fixed4(i.color.rgb * light, i.color.a);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off
            Fog { Color (0,0,0,0) }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // _fullshadows: any additional directional/spot light is shadow-
            // tested too, instead of shining through the slabs.
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float4 color  : COLOR;
                float3 normal : TEXCOORD0;
                float3 wpos   : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_LIGHTING_COORDS(3, 4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                UNITY_TRANSFER_LIGHTING(o, float2(0, 0));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                // Point/spot lights carry a position (w = 1); a directional
                // light in an add pass carries a direction (w = 0).
                float3 ldir = normalize(_WorldSpaceLightPos0.xyz - i.wpos * _WorldSpaceLightPos0.w);
                UNITY_LIGHT_ATTENUATION(atten, i, i.wpos);
                float ndl = saturate(dot(n, ldir));
                fixed4 col = fixed4(i.color.rgb * _LightColor0.rgb * ndl * atten, 0);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
                return col;
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}
