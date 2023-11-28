Shader "CustomRP/Lit"
{
    HLSLINCLUDE
    #include "../ShaderLibrary/Common.hlsl"
    #include "../ShaderLibrary/LitInput.hlsl"
    ENDHLSL

    Properties
    {
        _MainTex("Main Texture", 2D) = "white"{}
        _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        //PBR
        _Metallic("Metallic", Range(0.0, 1.0)) = 0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        //Emission
        [NoScaleOffset]_EmissionMap("Emission", 2D) = "white"{}
        [HDR]_EmissionColor("Emission", Color) = (0.0, 0.0, 0.0, 0.0)
        //Settings
        [Toggle(_PREMULTIPLY_ALPHA)] _PreMulAlpha("Premultiply Alpha", Float) = 0
        [Toggle(_CLIPPING)] _Clipping ("Alpha Clipping", Float) = 0
        [Toggle(_RECEIVE_SHADOWS)] _ReceiveShadows("Receive Shadows", Float) = 1
        [KeywordEnum(On, Clip, Dither, Off)] _Shadows("Shadows", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite("Z Write", Float) = 1
    }

    CustomEditor "CustomShaderGUI"

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Tags
            {
                "LightMode"="CustomLit"
            }
            HLSLPROGRAM
            #pragma target 3.5
            //SRP Settings
            #pragma multi_compile _ _DIRECTIONAL_PCF3 _DIRECTIONAL_PCF5 _DIRECTIONAL_PCF7
            #pragma multi_compile _ _CASCADE_BLEND_SOFT _CASCADE_BLEND_DITHER
            #pragma multi_compile _ _SHADOW_MASK_DISTANCE
            #pragma multi_compile _ LIGHTMAP_ON // Unity会对具有LIGHTMAP_ON关键字的shader变体进行Lightmap的渲染
            //Material Settings
            #pragma multi_compile_instancing
            #pragma shader_feature _CLIPPING
            #pragma shader_feature _PREMULTIPLY_ALPHA
            #pragma shader_feature _RECEIVE_SHADOWS
            #pragma vertex LitPassVertexProgram
            #pragma fragment LitPassFragmentProgram
            #include "../ShaderLibrary/LitPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "ShadowCaster"
            }
            
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma shader_feature _ _SHADOWS_CLIP _SHADOWS_DITHER
            #pragma multi_compile_instancing
            #pragma vertex ShadowCasterPassVertexProgram
            #pragma fragment ShadowCasterPassFragmentProgram
            #include "../ShaderLibrary/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        
        //MetaPass用于烘焙Light Map
        Pass
        {
            Tags
            {
                "LightMode" = "Meta"
            }
            
            Cull Off
            
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MetaPassVertexProgram
            #pragma fragment MetaPassFragmentProgram
            #include "../ShaderLibrary/MetaPass.hlsl"
            ENDHLSL
        }
    }
}