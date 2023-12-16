#ifndef CUSTOM_UNITY_INPUT_INCLUDE
#define CUSTOM_UNITY_INPUT_INCLUDE

// CBUFFER_START(UnityPerDraw)
//  //GPU接收light map的相关计算参数
//  float4 unity_LightmapST;
//  float4 unity_ProbesOcclusion; //ShadowMask的Occlusion Probes
//  float4 unity_SpecCube0_HDR; //For Probes Decoding
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
//
//  //处理Per Object Light -- unity_LightData.y存储光源的数量，unity_LightIndices[2]的所有通道用于存储光源索引（所以PerObjectLight最多有8个）
//  real4 unity_LightData;
//  real4 unity_LightIndices[2];
// CBUFFER_END
//
// //volume数据存储在3D float Texture中
// TEXTURE3D_FLOAT(unity_ProbeVolumeSH);
// SAMPLER(samplerunity_ProbeVolumeSH);

#endif