Shader "CustomRP/Unlit"
{
    Properties
    {
        _MainTex("Main Texture", 2D) = "white"{}
        _BaseColor("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [Toggle(_CLIPPING)] _Clipping ("Alpha Clipping", Float) = 0
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
            Tags{"LightMode"="SRPUnlit"}
            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma shader_feature _CLIPPING
            #pragma vertex BaseVertexProgram
            #pragma fragment BaseFragmentProgram
            #include "../ShaderLibrary/UnlitPass.hlsl"
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
    }
}