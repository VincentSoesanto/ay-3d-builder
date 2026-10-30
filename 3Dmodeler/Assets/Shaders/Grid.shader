// Floor grid lines, drawn on a big flat quad lying just above the floor.
// Thin lines every _GridSize meters, stronger lines every meter, red X axis and blue Z axis lines.
// Fades out with distance so the horizon stays clean.
Shader "Builder/Grid"
{
    Properties
    {
        _GridSize ("Grid Size (m)", Float) = 0.25
        _MinorColor ("Minor Lines", Color) = (1, 1, 1, 0.12)
        _MajorColor ("Meter Lines", Color) = (1, 1, 1, 0.35)
        _AxisXColor ("X Axis", Color) = (0.95, 0.3, 0.3, 0.9)
        _AxisZColor ("Z Axis", Color) = (0.3, 0.5, 1.0, 0.9)
        _FadeDistance ("Fade Distance (m)", Float) = 80
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Grid"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _GridSize;
                half4 _MinorColor;
                half4 _MajorColor;
                half4 _AxisXColor;
                half4 _AxisZColor;
                float _FadeDistance;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // How much this pixel is on a line of the given spacing (0..1). fwidth keeps lines about 1 pixel wide.
            float LineMask(float2 p, float spacing)
            {
                float2 cell = p / spacing;
                float2 width = fwidth(cell);
                float2 dist = abs(frac(cell - 0.5) - 0.5) / max(width, 1e-5);
                // When lines get closer than a few pixels, fade them out instead of flickering.
                float density = saturate(1.5 - max(width.x, width.y) * 4.0);
                return (1.0 - saturate(min(dist.x, dist.y))) * density;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.positionWS.xz;

                half4 col = _MinorColor * LineMask(p, _GridSize);
                col = lerp(col, _MajorColor, LineMask(p, 1.0));

                // Axis lines: X axis is where z = 0, Z axis is where x = 0.
                float2 w = fwidth(p) * 1.5;
                col = lerp(col, _AxisXColor, 1.0 - saturate(abs(p.y) / max(w.y, 1e-5)));
                col = lerp(col, _AxisZColor, 1.0 - saturate(abs(p.x) / max(w.x, 1e-5)));

                float dist = distance(_WorldSpaceCameraPos, i.positionWS);
                col.a *= saturate(1.0 - dist / _FadeDistance);
                return col;
            }
            ENDHLSL
        }
    }
}
