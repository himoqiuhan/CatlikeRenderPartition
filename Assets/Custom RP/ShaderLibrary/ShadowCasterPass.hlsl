#ifndef CUSTOM_SHADOW_CASTER_PASS_INCLUDED
#define CUSTOM_SHADOW_CASTER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

// TEXTURE2D(_MainTex);
// SAMPLER(sampler_MainTex);
// UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
// UNITY_DEFINE_INSTANCED_PROP(float4, _MainTex_ST)
// UNITY_DEFINE_INSTANCED_PROP(half4, _BaseColor)
// UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
// UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : VAR_MAIN_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings ShadowCasterPassVertexProgram(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    VertexPositionInputs vertexPositionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = vertexPositionInputs.positionCS;
    //Shadow Pancaking：为了减少Shadow Acne，需要提高光源STS下深度的精度，这样就需要尽可能压缩裁剪区域的大小，也就是需要向前移动近裁剪面
    //但是，这样会带来另外一个问题：那些不在摄像机VS下的物体可能会被部分裁剪，尤其是对于在STS裁剪空间中，同时存在于近裁剪面两侧的阴影投射物，
    //在近裁剪面以外的顶点会被裁剪掉，最终导致的结果是投射出去的阴影会被部分截断（或中空）
    //为此，需要在Shadow Caster Pass的顶点处理阶段，把顶点Clamp在两个裁剪平面之间
    #if UNITY_REVERSED_Z
    //如果深度反转（近1远0）
    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #else
    //如果深度没有反转（近0远1）
    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
    #endif
    // float4 mainTexST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MainTex_ST);
    // output.uv = input.uv * mainTexST.xy + mainTexST.zw;
    output.uv = TransformBaseUV(input.uv);
    return output;
}

void ShadowCasterPassFragmentProgram(Varyings i)
{
    // half4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    // half4 mainTexColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
    // half4 base = baseColor * mainTexColor;
    float4 base = GetBase(i.uv);
    #if defined(_SHADOWS_CLIP)
        // clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));
        clip(base.a - GetCutOff(i.uv));
    #elif defined(_SHADOWS_DITHER)
        float dither = InterleavedGradientNoise(i.positionCS.xy, 0);
        clip(base.a - dither);
    #endif
}

#endif
