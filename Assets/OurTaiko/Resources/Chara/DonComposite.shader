// Puts a Don's render target on a canvas (OurTaiko.Chara.DonView's RawImage): YataiDON's
// outline_fxaa.fs. Sixteen taps _Thickness texels out give the dark rim around the silhouette;
// inside it the colour is darkened by _TintAmount and _TintColor added (miss streak, rainbow
// gauge). The target holds colour already multiplied by alpha, so it is blended premultiplied.
Shader "OurTaiko/Don Composite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Thickness ("Rim width in texels", Float) = 3
        _TintColor ("Tint colour", Color) = (0,0,0,1)
        _TintAmount ("Tint amount", Float) = 0
        _Footprint ("Average over the screen pixel (0: one bilinear tap)", Float) = 1
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
                float4 _MainTex_TexelSize;
                float _Thickness;
                half4 _TintColor;
                float _TintAmount;
                float _Footprint;
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
                float2 texel = _MainTex_TexelSize.xy;
                // The target is supersampled: average the screen pixel's whole footprint (four
                // bilinear taps a quarter pixel out, a tent over ~3x3 texels at 2x) instead of the one
                // tap at its centre, which leaves the edges stepped.
                half4 center;
                if (_Footprint > 0.5)
                {
                    float2 dx = ddx(input.uv) * 0.25, dy = ddy(input.uv) * 0.25;
                    center = 0.25 * (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - dx - dy)
                        + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + dx - dy)
                        + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - dx + dy)
                        + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + dx + dy));
                }
                else center = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half coverage = 0;
                [unroll] for (int i = 0; i < 16; i++)
                {
                    float angle = 6.28318530718 * (i + 0.5) / 16.0;
                    float2 delta = float2(cos(angle), sin(angle)) * texel * _Thickness;
                    coverage = max(coverage, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + delta).a);
                }
                half alpha = max(coverage, center.a);
                if (alpha <= 0) discard;
                half3 rgb = center.a > 0 ? center.rgb * (1 - _TintAmount) + _TintColor.rgb * center.a : half3(0, 0, 0);
                return half4(rgb, alpha) * input.color.a;
            }
            ENDHLSL
        }
    }
}
