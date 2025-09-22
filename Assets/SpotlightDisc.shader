Shader "UI/SpotlightDisc"
{
    Properties
    {
        _Color   ("Color", Color) = (1,1,1,1)
        _Center  ("Center (UV)", Vector) = (0.5,0.5,0,0)
        _Radius  ("Radius", Range(0,1)) = 0.18
        _Feather ("Feather", Range(0,0.5)) = 0.12
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings  { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };

            float4 _Color;
            float4 _Center;
            float  _Radius;
            float  _Feather;

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv; o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float2 c  = _Center.xy;

                float d = distance(uv, c);
                float inner = _Radius;
                float outer = _Radius + max(0.0001, _Feather);

                // 1 inside -> 0 outside (부드러운 가장자리)
                float m = 1.0 - smoothstep(inner, outer, d);

                return half4(_Color.rgb, _Color.a * m);
            }
            ENDHLSL
        }
    }
}
