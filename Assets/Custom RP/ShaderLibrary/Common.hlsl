#ifndef CUSTOM_COMMON_INCLUDED
#define CUSTOM_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
// #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

#if defined(_SHADOW_MASK_ALWAYS) || defined(_SHADOW_MASK_DISTANCE)
    #define SHADOWS_SHADOWMASK
#endif


real3x3 CustomCreateTangentToWorld(real3 normal, real3 tangent, real flipSign)
{
    // For odd-negative scale transforms we need to flip the sign
    real sgn = flipSign; // Have Some Bugs
    real3 bitangent = cross(normal, tangent) * sgn;

    return real3x3(tangent, bitangent, normal);
}

real3 CustomTransformTangentToWorld(float3 dirTS, real3x3 tangentToWorld)
{
    // Note matrix is in row major convention with left multiplication as it is build on the fly
    return mul(dirTS, tangentToWorld);
}

float InterleavedGradientNoiseForLOD(float2 pixCoord, int frameCount)
{
    const float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    float2 frameMagicScale = float2(2.083f, 4.867f);
    pixCoord += frameCount * frameMagicScale;
    return frac(magic.z * frac(dot(pixCoord, magic.xy)));
}

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
        // float dither = (positionCS.y % 32) / 32;
        //或者借助计算噪声来作为两个LOD之间的过度
        float dither = InterleavedGradientNoiseForLOD(positionCS.xy, 0);
        //由LOD n切换到LOD n+1时，LOD n+1区域的Fade Factor是负的
        //关于Fade Factor这一部分有点不理解，在两个LOD层级之间做可视化，似乎unity_LODFade.x的值是由一个LOD内0-1，下一个LOD内1-0，再下一个LOD内0-1这样变化的
        clip(fade + (fade < 0.0 ? dither : -dither));
    #endif
}

float3 DecodeNormal(float4 sample, float scale)
{
    #if defined(UNITY_NO_DXT5nm)
        return normalize(UnpackNormalRGB(sample, scale));
    #else
        return normalize(UnpackNormalmapRGorAG(sample, scale));
    #endif
}

float3 NormalTangentToWorld(float3 normalTS, float3 normalWS, float4 tangentWS)
{
    float3x3 tangentToWorld = CustomCreateTangentToWorld(normalWS, tangentWS.xyz, tangentWS.w);
    return CustomTransformTangentToWorld(normalTS, tangentToWorld);
}

#endif