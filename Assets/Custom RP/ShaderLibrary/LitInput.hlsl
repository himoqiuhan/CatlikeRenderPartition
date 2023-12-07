#ifndef CUSTOM_LIT_INPUT_INCLUDED
#define CUSTOM_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#define INPUT_PROP(name) UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, name)

TEXTURE2D(_MainTex);
TEXTURE2D(_EmissionMap);
TEXTURE2D(_MaskMap);
TEXTURE2D(_NormalMap);
SAMPLER(sampler_MainTex);
TEXTURE2D(_DetailMap);
SAMPLER(sampler_DetailMap);
UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
UNITY_DEFINE_INSTANCED_PROP(float4, _MainTex_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _DetailMap_ST)
UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
UNITY_DEFINE_INSTANCED_PROP(float4, _EmissionColor)
UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
UNITY_DEFINE_INSTANCED_PROP(float, _Occlusion)
UNITY_DEFINE_INSTANCED_PROP(float, _Smoothness)
UNITY_DEFINE_INSTANCED_PROP(float, _NormalScale)
UNITY_DEFINE_INSTANCED_PROP(float, _Fresnel)
UNITY_DEFINE_INSTANCED_PROP(float, _DetailAlbedo)
UNITY_DEFINE_INSTANCED_PROP(float, _DetailSmoothness)
UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

float2 TransformBaseUV(float2 baseUV)
{
    float4 baseST = INPUT_PROP(_MainTex_ST);
    return baseST.xy * baseUV + baseST.zw;
}

float2 TransformDetailUV(float2 detailUV)
{
    float4 detailST = INPUT_PROP(_DetailMap_ST);
    return detailST.xy * detailUV + detailST.zw;
}

float4 GetDetail(float2 detailUV)
{
    float4 map = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, detailUV);
    return map * 2.0 - 1.0;
}

float4 GetMask(float2 baseUV)
{
    return SAMPLE_TEXTURE2D(_MaskMap, sampler_MainTex, baseUV);
}


float4 GetBase(float2 baseUV, float2 detailUV = 0.0)
{
    float4 map = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, baseUV);
    float4 color = INPUT_PROP(_BaseColor);

    float detail = GetDetail(detailUV).r * INPUT_PROP(_DetailAlbedo);
    float mask = GetMask(baseUV).b;
    //因为Detail贴图没有开启sRGB，直接使用gamma空间计算得到的效果会过亮，通过sqrt模拟转换到线性空间得到效果会更好
    map.rgb = lerp(sqrt(map.rgb), detail < 0.0 ? 0.0 : 1.0, abs(detail) * mask);
    map.rgb *= map.rgb;
    
    return map * color;
}

float GetCutOff(float2 baseUV)
{
    return INPUT_PROP(_Cutoff);
}

float GetMetallic(float2 baseUV)
{
    float metallic = INPUT_PROP(_Metallic);
    metallic *= GetMask(baseUV).r;
    return metallic;
}

float GetSmoothness(float2 baseUV, float2 detailUV = 0.0)
{
    float smoothness = INPUT_PROP(_Smoothness);
    smoothness *= GetMask(baseUV).a;

    float detail = GetDetail(detailUV).b * INPUT_PROP(_DetailSmoothness);
    float mask = GetMask(baseUV).b;
    smoothness = lerp(smoothness, detail < 0.0 ? 0.0 : 1.0, abs(detail) * mask);
    
    return smoothness;
}

float GetFresnel(float2 baseUV)
{
    return INPUT_PROP(_Fresnel);
}

float3 GetEmission(float2 baseUV)
{
    float4 map = SAMPLE_TEXTURE2D(_EmissionMap, sampler_MainTex, baseUV);
    float4 color = INPUT_PROP(_EmissionColor);
    return map.rgb * color.rgb;
}

float GetOcclusiton(float2 baseUV)
{
    float strength = INPUT_PROP(_Occlusion);
    float occlusion = GetMask(baseUV).g;
    occlusion = lerp(1.0, occlusion, strength);
    return occlusion;
}

float3 GetNormalTS(float2 baseUV)
{
    float4 map = SAMPLE_TEXTURE2D(_NormalMap, sampler_MainTex, baseUV);
    float scale = INPUT_PROP(_NormalScale);
    float3 normal = DecodeNormal(map, scale);
    return normal;
}

#endif
