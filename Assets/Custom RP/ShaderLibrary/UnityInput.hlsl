#ifndef CUSTOM_UNITY_INPUT_INCLUDE
#define CUSTOM_UNITY_INPUT_INCLUDE

// CBUFFER_START(UnityPerDraw)
//  //GPU接收light map的相关计算参数
//  float4 unity_LightmapST;
//  float4 unity_ProbesOcclusion; //ShadowMask的Occlusion Probes
//  // float4 unity_DynamicLightmapST; //For SRP batcher compatibility
//
//  //GPU接收light probe的相关计算参数：
//  float4 unity_SHAr;
//  float4 unity_SHAg;
//  float4 unity_SHAb;
//  float4 unity_SHBr;
//  float4 unity_SHBg;
//  float4 unity_SHBb;
//  float4 unity_SHC;
//
//  //GPU接收light probe proxy volume(LPPV)的相关计算参数
// float4 unity_ProbeVolumeParams;
// float4x4 unity_ProbeVolumeWorldToObject;
// float4 unity_ProbeVolumeSizeInv;
// float4 unity_ProbeVolumeMin;
// CBUFFER_END
//
// //volume数据存储在3D float Texture中
// TEXTURE3D_FLOAT(unity_ProbeVolumeSH);
// SAMPLER(samplerunity_ProbeVolumeSH);

#endif