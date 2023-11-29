#ifndef CUSTOM_UNLIT_PASS_INCLUDED
#define CUSTOM_UNLIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

// CBUFFER_START(UnityPerMaterial)
// half4 _BaseColor;
// CBUFFER_END

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
    vertexOutput.uv = TransformBaseUV(vertexInput.uv);
    return vertexOutput;
}

half4 BaseFragmentProgram(Varyings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    float4 base = GetBase(i.uv);
            
    #if defined(_CLIPPING)
    clip(base.a - GetCutOff(i.uv));
    #endif

    return base;
}

#endif
