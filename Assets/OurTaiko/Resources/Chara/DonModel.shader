// The 3D Don (OurTaiko.Chara.Chara3D), drawn by a command buffer into its render target.
// Pass 0 is YataiDON's cutout.fs: unlit texture times the material colour, fully clear texels
// cut out, the rest blended (colour SrcAlpha/1-SrcAlpha, alpha One/1-SrcAlpha: YataiDON's
// CUSTOM_SEPARATE). The face plate keeps its clear texels (_AlphaClip 0) and shows a cell of the
// face sheet (_UvRect). Pass 1 is outline.vs/fs: back faces pushed out along the view normal by
// _DonOutline.x pixels times the vertex colour's green, faces turned toward the camera dropped.
// Passes 2 and 3 are those two again writing the depth only: a Don's background, which is shown
// as a layer of its own, hides the Don behind it this way (Chara3D.Render).
Shader "OurTaiko/Don Model"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _UvRect ("UV scale (xy) and offset (zw)", Vector) = (1,1,0,0)
        _AlphaClip ("Cut out clear texels", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _OutlineCull ("Outline cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            float4 _UvRect;
            float _AlphaClip;
            float _Cull;
            float _OutlineCull;
        CBUFFER_END
        float4 _DonOutline;    // x: line width in target pixels, y: facing cut-off
        float4 _DonScreenSize; // the target's size in pixels

        struct MainAttributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct MainVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        MainVaryings MainVertex(MainAttributes input)
        {
            MainVaryings o;
            o.positionCS = TransformObjectToHClip(input.positionOS);
            o.uv = input.uv * _UvRect.xy + _UvRect.zw;
            return o;
        }
        half4 MainFragment(MainVaryings input) : SV_Target
        {
            half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            if (_AlphaClip > 0.5 && texel.a <= 0) discard;
            return texel * _Color;
        }

        struct OutlineAttributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
        struct OutlineVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float facing : TEXCOORD1; };
        OutlineVaryings OutlineVertex(OutlineAttributes input)
        {
            OutlineVaryings o;
            float4 clip = TransformObjectToHClip(input.positionOS);
            float3 n = normalize(mul((float3x3)UNITY_MATRIX_V, TransformObjectToWorldNormal(input.normalOS)));
            if (dot(n.xy, n.xy) > 1e-12)
            {
                float2 px = _DonOutline.x * input.color.g * normalize(n.xy);
                // A projection flipped for rendering into a texture flips view y in clip space.
                px.y *= UNITY_MATRIX_P[1][1] < 0 ? -1 : 1;
                clip.xy += px * 2.0 / _DonScreenSize.xy * clip.w;
            }
            o.positionCS = clip;
            o.uv = input.uv * _UvRect.xy + _UvRect.zw;
            o.facing = n.z;
            return o;
        }
        half4 OutlineFragment(OutlineVaryings input) : SV_Target
        {
            if (input.facing > _DonOutline.y) discard;
            half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
            if (a <= 0) discard;
            return half4(0.05, 0.05, 0.05, a);
        }
        ENDHLSL

        Pass
        {
            Name "Main"
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex MainVertex
            #pragma fragment MainFragment
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Cull [_OutlineCull]
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            ENDHLSL
        }

        Pass
        {
            Name "Mask"
            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex MainVertex
            #pragma fragment MainFragment
            ENDHLSL
        }

        Pass
        {
            Name "OutlineMask"
            Cull [_OutlineCull]
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            ENDHLSL
        }
    }
}
