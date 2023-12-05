#ifndef CUSTOM_COMMON_INCLUDED
#define CUSTOM_COMMON_INCLUDED

#if defined(_SHADOW_MASK_ALWAYS) || defined(_SHADOW_MASK_DISTANCE)
    #define SHADOWS_SHADOWMASK
#endif

float Square(float v)
{
    return v * v;
}

float DistanceSquared(float3 pA, float3 pB)
{
    return dot(pA - pB, pA - pB);
}

//通过Clip来实现两个LOD之间的过渡，原理类似于Dithering模拟半透明物体的阴影。因为ShadowCaster和CustomLit两个Pass都会使用，所以写在Common中
void ClipLOD(float2 positionCS, float fade)
{
    #if defined(LOD_FADE_CROSSFADE)
        //基于CS进行处理，在竖直方向上每32个像素作为一个影响区间进行融合
        float dither = (positionCS.y % 32) / 32;
        clip(fade - dither);
    #endif
}

#endif