// Flat see-through color that is always drawn on top of everything (gizmo arrows, rings and handles),
// so they can be grabbed even when they are inside or behind a piece.
Shader "Builder/Overlay"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 0.9)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // A little shading from above so the 3D shape is readable.
                half light = 0.75 + 0.25 * saturate(normalize(i.normalWS).y * 0.5 + 0.5);
                return half4(_Color.rgb * light, _Color.a);
            }
            ENDHLSL
        }
    }
}
