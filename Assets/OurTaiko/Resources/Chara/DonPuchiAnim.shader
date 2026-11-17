// An animated (.otanim) puchichara beside the Don (OurTaiko.Chara.PuchiAnimGraphic), tinted like
// DonPuchi. Drawn premultiplied: the texture is premultiplied already when _Premultiplied is 1;
// a vertex whose u has 2 added is an additive part (alpha 0, so the blend adds it).
Shader "OurTaiko/Don Puchi Anim"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _TintColor ("Tint colour", Color) = (0,0,0,1)
        _TintAmount ("Tint amount", Float) = 0
        _Premultiplied ("Texture is premultiplied", Float) = 0
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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _TintColor;
                float _TintAmount;
                float _Premultiplied;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vertex(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                o.color = input.color;
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
                c.rgb = lerp(c.rgb, _TintColor.rgb * c.a, saturate(_TintAmount)) + _TintColor.rgb * c.a;
                c.a *= 1 - additive;
                return c;
            }
            ENDHLSL
        }
    }
}
