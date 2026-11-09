Shader "SceneGeography/Samples/Unlit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct VertexOutput
            {
                float4 position : SV_POSITION;
            };

            VertexOutput Vert(float4 position : POSITION)
            {
                VertexOutput output;
                output.position = UnityObjectToClipPos(position);
                return output;
            }

            fixed4 Frag(VertexOutput input) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
    Fallback Off
}
