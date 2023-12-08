#ifndef CUSTOM_SURFACE_INCLUDED
#define CUSTOM_SURFACE_INCLUDED

struct Surface
{
    float3 position;
    float3 normal;
    float3 interpolatedNormal;
    float3 viewDirection;
    float depth;//maxDistance最大阴影距离是VS的深度，不是到相机的距离，所以我们需要在surface中加入一个depth信息
    float3 color;
    float alpha;
    float metallic;
    float occlusion;
    float smoothness;
    float fresnelStrength;
    float dither;//用于处理抖动--单次采样实现阴影cascade之间的过渡
};

#endif