#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

// CBUFFER_START(UnityPerMaterial)
// half4 _BaseColor;
// CBUFFER_END

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
UNITY_DEFINE_INSTANCED_PROP(float4, _MainTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

struct Attributes
{
    float4 posOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 posCS : SV_POSITION;
    float2 uv : VAR_MAIN_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings BaseVertexProgram(Attributes vertexInput)
{
    Varyings vertexOutput;
    UNITY_SETUP_INSTANCE_ID(vertexInput);
    UNITY_TRANSFER_INSTANCE_ID(vertexInput, vertexOutput);
    vertexOutput.posCS = TransformObjectToHClip(vertexInput.posOS.xyz);
    float4 mainTexST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MainTex_ST);
    vertexOutput.uv = vertexInput.uv * mainTexST.xy + mainTexST.zw;
    return vertexOutput;
}

half4 BaseFragmentProgram(Varyings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    float4 mainTexColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
    float4 base = baseColor * mainTexColor;
            
    #if defined(_CLIPPING)
    clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));
    #endif

    return base;
}

#endif
