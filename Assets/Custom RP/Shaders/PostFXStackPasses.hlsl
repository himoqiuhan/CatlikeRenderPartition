#ifndef CUSTOM_POST_FX_PASSES_INCLUDED
#define CUSTOM_POST_FX_PASSES_INCLUDED

TEXTURE2D(_PostFXSource);
SAMPLER(sampler_linear_clamp);

float4 _ProjectionParams;

float4 GetSource(float2 screenUV)
{
    return SAMPLE_TEXTURE2D_LOD(_PostFXSource, sampler_linear_clamp, screenUV, 0);
}

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : VAR_SCREEN_UV;
};

Varyings DefaultPassVertex(uint vertexID : SV_VertexID)
{
    Varyings output;
    output.positionCS = float4(
        vertexID <= 1 ? -1.0 :  3.0,
        vertexID == 1 ?  3.0 : -1.0,
        0.0, 1.0
        );
    output.screenUV = float2(
        vertexID <= 1 ? 0.0 : 2.0,
        vertexID == 1 ? 2.0 : 0.0
        );
    //对于一些目标平台的V坐标是反过来的(OpenGL和DX的差异)，所以得到的结果可能是反过来的，这里需要进行一步针对不同平台的处理
    //（但是由于前期架构不完善，动用了Core.hlsl的东西，导致我没有启用UnityInput，所以这里用不了）
    if (_ProjectionParams.x < 0.0)
    {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
    return output;
}

float4 CopyPassFragment(Varyings input) : SV_Target
{
    return GetSource(input.screenUV);
}

#endif