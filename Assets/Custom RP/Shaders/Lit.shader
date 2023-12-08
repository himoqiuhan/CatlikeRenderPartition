Shader "CustomRP/Lit"
{
    Properties
    {
        _MainTex("Main Texture", 2D) = "white"{}
        _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [Toggle(_MASK_MAP)] _MaskMapToggle("Mask Map", Float) = 0.0
        [NoScaleOffset] _MaskMap("Mask (MODS)", 2D) = "white"{}
        //PBR
        _Metallic("Metallic", Range(0.0, 1.0)) = 0
        _Occlusion("Occlusion", Range(0.0,  1.0)) = 1.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _Fresnel("Fresnel", Range(0.0, 1.0)) = 1.0
        
        //Normal
        [Toggle(_NORMAL_MAP)] _NormalMapToggle("Normal Map", Float) = 0
        [NoScaleOffset] _NormalMap("Normals", 2D) = "bump"{}
        _NormalScale("Normal Scale", Range(0.0, 1.0)) = 1.0
        
        //Emission
        [NoScaleOffset]_EmissionMap("Emission", 2D) = "white"{}
        [HDR]_EmissionColor("Emission", Color) = (0.0, 0.0, 0.0, 0.0)
        
        //Detail Map
        [Toggle(_DETAIL_MAP)] _DetailMapToggle("Detail Maps", Float) = 0
        _DetailMap("Details", 2D) = "linearGrey"{}
        [NoScaleOffset]_DetailNormalMap("Detail Normals", 2D) = "bump"{}
        _DetailAlbedo("Detail Albedo", Range(0.0, 1.0)) = 1.0
        _DetailSmoothness("Detial Smoothness", Range(0.0, 1.0)) = 1.0
        _DetailNormalScale("Detial Normal Scale", Range(0.0, 1.0)) = 1.0
        
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
        HLSLINCLUDE
        #include "../ShaderLibrary/Common.hlsl"
        #include "../ShaderLibrary/LitInput.hlsl"
        ENDHLSL
        
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
            #pragma multi_compile _ LOD_FADE_CROSSFADE //LOD切换模式
            //SRP Settings
            #pragma multi_compile _ _DIRECTIONAL_PCF3 _DIRECTIONAL_PCF5 _DIRECTIONAL_PCF7
            #pragma multi_compile _ _CASCADE_BLEND_SOFT _CASCADE_BLEND_DITHER
            #pragma multi_compile _ _SHADOW_MASK_ALWAYS _SHADOW_MASK_DISTANCE
            #pragma multi_compile _ LIGHTMAP_ON // Unity会对具有LIGHTMAP_ON关键字的shader变体进行Lightmap的渲染
            //Material Settings
            #pragma multi_compile_instancing
            #pragma shader_feature _DETAIL_MAP
            #pragma shader_feature _MASK_MAP
            #pragma shader_feature _NORMAL_MAP
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
            #pragma multi_compile _ LOD_FADE_CROSSFADE //LOD切换模式
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