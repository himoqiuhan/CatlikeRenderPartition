#ifndef CUSTOM_META_PASS_INCLUDED
#define CUSTOM_META_PASS_INCLUDED

//Meta Pass中需要得到物体表面的漫反射率

#include "../ShaderLibrary/LitInput.hlsl"
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Shadows.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"

bool4 unity_MetaFragmentControl;//MetaPass可以用来生成不同的数据，通过这个参数来获取flags
//X:设置是否使用漫反射率
float unity_OneOverOutputBoost;
float unity_MaxOutputValue;


struct Attributes
{
    float3 positionOS : POSITION;
    float2 baseUV : TEXCOORD0;
    float2 lightMapUV : TEXCOORD1;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 baseUV : VAR_BASE_UV;
};

Varyings MetaPassVertexProgram(Attributes input)
{
    Varyings output;
    input.positionOS.xy = input.lightMapUV * unity_LightmapST.xy + unity_LightmapST.zw;
    input.positionOS.z = input.positionOS.z > 0.0 ? FLT_MIN : 0.0;
    output.positionCS = TransformWorldToHClip(input.positionOS);//我也没看懂，先写先写
    output.baseUV = TransformBaseUV(input.baseUV);
    return output;
}

float4 MetaPassFragmentProgram(Varyings input) : SV_Target
{
    InputConfig config = GetInputConfig(input.baseUV);
    float4 base = GetBase(config);
    Surface surface;
    ZERO_INITIALIZE(Surface, surface);//用0初始化surface的所有信息
    surface.color = base.rgb;
    surface.metallic = GetMetallic(config);
    surface.smoothness = GetSmoothness(config);
    BRDF brdf = GetBRDF(surface);
    float4 meta = 0.0;
    if(unity_MetaFragmentControl.x) // unity_MetaFragmentControl.x控制是否使用漫反射率
    {
        meta = float4(brdf.diffuse, 1.0);
    }
    else if(unity_MetaFragmentControl.y) // unity_MetaFragmentControl控制是否返回自发光，但是自发光不会自动烘焙
        //需要到物体那边进行进一步的控制
    {
        meta = float4(GetEmission(config), 1.0);
    }
    meta.rgb += brdf.specular * brdf.roughness * 0.5;//Idea来自高度反光但是粗糙的物体同样会反射出一些间接光
    meta.rgb = min(PositivePow(meta.rgb, unity_OneOverOutputBoost), unity_MaxOutputValue);
    //通过unity_OneOverOutputBoost对颜色进行一次增强，PositivePow的底层是确保底数为正的Pow
    return meta;
}

#endif