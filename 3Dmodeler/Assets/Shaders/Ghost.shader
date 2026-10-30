// See-through glowing look for the placement ghost and the aim highlight.
// Brighter at the edges (where you look along the surface), so the shape reads clearly.
Shader "Builder/Ghost"
{
    Properties
    {
        _Color ("Color", Color) = (0.4, 0.85, 1.0, 0.3)
        _RimStrength ("Edge Glow", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _RimStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);
                half rim = pow(1.0 - saturate(dot(normalize(i.normalWS), viewDir)), 2.0);
                half4 col = _Color;
                col.rgb += rim * _RimStrength;
                col.a = saturate(col.a + rim * _RimStrength);
                return col;
            }
            ENDHLSL
        }
    }
}
