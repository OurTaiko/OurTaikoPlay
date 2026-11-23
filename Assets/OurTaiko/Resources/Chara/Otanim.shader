// An .otanim animation on a canvas (OurTaiko.Chara.OtanimView). Drawn premultiplied: the texture
// is premultiplied already when _Premultiplied is 1; a vertex whose u has 2 added is an additive
// part (alpha 0, so the blend adds it). The view gives screen parts a material of their own with
// _DstBlend OneMinusSrcColor. Clipped by a RectMask2D, as the stage's bands are.
Shader "OurTaiko/Otanim"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Premultiplied ("Texture is premultiplied", Float) = 0
        _DstBlend ("Destination blend", Float) = 10
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One [_DstBlend]
        ColorMask [_ColorMask]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _Premultiplied;
                float _DstBlend;
            CBUFFER_END
            float4 _ClipRect;
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float2 local : TEXCOORD1; };
            Varyings Vertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                o.color = input.color;
                o.local = input.positionOS.xy;
                return o;
            }
            half4 Fragment(Varyings input) : SV_Target
            {
                float additive = step(1.5, input.uv.x);
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(input.uv.x - additive * 2, input.uv.y));
                if (_Premultiplied < 0.5) t.rgb *= t.a;
                half4 c;
                c.a = t.a * input.color.a;
                c.rgb = t.rgb * input.color.rgb * input.color.a;
                c.a *= 1 - additive;
                #ifdef UNITY_UI_CLIP_RECT
                float2 inside = step(_ClipRect.xy, input.local) * step(input.local, _ClipRect.zw);
                c *= inside.x * inside.y;
                #endif
                return c;
            }
            ENDHLSL
        }
    }
}
