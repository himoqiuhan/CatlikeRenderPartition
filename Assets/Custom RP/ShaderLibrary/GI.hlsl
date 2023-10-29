#ifndef CUSTOM_GI_INCLUDE
#define CUSTOM_GI_INCLUDE


//为了便捷控制GI的开启和关闭，使用宏定义来对GI相关信息进行处理
#if defined(LIGHTMAP_ON)
    #define GI_ATTRIBUTE_DATA float2 lightMapUV : TEXCOORD1;
    #define GI_VARYINGS_DATA float2 lightMapUV : VAR_LIGHT_MAP_UV;
    #define TRANSFER_GI_DATA(input, output) \
        output.lightMapUV = input.lightMapUV * \
        unity_LightmapST.xy + unity_LightmapST.zw;
    #define GI_FRAGMENT_DATA(input) input.lightMapUV
#else
    #define GI_ATTRIBUTE_DATA
    #define GI_VARYINGS_DATA
    #define TRANSFER_GI_DATA(input, output)
    #define GI_FRAGMENT_DATA(input) float2(0.0,0.0)
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

// TEXTURE2D(unity_Lightmap);
SAMPLER(samplerunity_lightmap);

struct GI
{
    float3 diffuse;
};


float3 SampleLightMap(float2 lightMapUV)
{
    #if defined(LIGHTMAP_ON)
        return SampleSingleLightmap(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap), lightMapUV,
            float4(1.0, 1.0, 0.0, 0.0),
            #if defined(UNITY_LIGHTMAP_FULL_HDR)
                false,
            #else
                true,
            #endif
                float4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0, 0.0));
    #else
        return 0.0;
    #endif
}

float SampleLightProbe(Surface surfaceWS)
{
    #if defined(LIGHTMAP_ON)
        return 0.0;//如果对象使用了Lightmap，则不计算SH的light probe
    #else
    // if(unity_ProbeVolumeParams.x)
    // {
    //     //URP管线下不支持LVVP，如果要写LVVP需要使用HDRP的管线模板
    //     //在UnityInput.hlsl文件中写UnityPerDraw的Cbuffer会导致和URP自带的CBUFFER(UnityPerDraw)重复定义
    //     //这一块暂时就先搁置一下
    //     //https://docs.unity3d.com/cn/2021.3/Manual/render-pipelines-feature-comparison.html
    //     return SampleProbeVolumeSH4(TEXTURE3D_ARGS(unity_ProbeVolumeSH, samplerunity_ProbeVolumeSH),
    //         surfaceWS.position, surfaceWS.normal,
    //         unity_ProbeVolumeWorldToObject,
    //         unity_ProbeVolumeParams.y, unity_ProbeVolumeParams.z,
    //         unity_ProbeVolumeMin.xyz, unity_ProbeVolumeSizeInv.xyz
    //         );
    // }
    // else
    // {
        float4 coefficients[7];
        coefficients[0] = unity_SHAr;
        coefficients[1] = unity_SHAg;
        coefficients[2] = unity_SHAb;
        coefficients[3] = unity_SHBr;
        coefficients[4] = unity_SHBg;
        coefficients[5] = unity_SHBb;
        coefficients[6] = unity_SHC;
        return max(0.0, SampleSH9(coefficients, surfaceWS.normal));//通过SampleSH9函数来采样Light Probe，需要传Probe data和normal data
    // }
    #endif
}


GI GetGI(float2 lightMapUV, Surface surfaceWS)
{
    GI gi;
    gi.diffuse = SampleLightMap(lightMapUV) + SampleLightProbe(surfaceWS);
    return gi;
}

#endif