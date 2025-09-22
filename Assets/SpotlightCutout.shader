Shader "UI/SpotlightCutout"
{
    Properties
    {
        _Tint    ("Overlay Tint", Color) = (0.2,0.2,0.22,0.9)
        _Center  ("Center (UV)", Vector) = (0.5,0.5,0,0)
        _Radius  ("Radius", Range(0,1)) = 0.2
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

            float4 _Tint;
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

                // m = 0(구멍) ~ 1(덮개)
                float m = smoothstep(inner, outer, d);

                // 구멍 영역은 완전 투명, 그 밖은 회색 오버레이
                return half4(_Tint.rgb, _Tint.a * m);
            }
            ENDHLSL
        }
    }
}
