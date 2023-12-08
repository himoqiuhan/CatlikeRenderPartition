#ifndef CUSTOM_SHADOWS_INCLUDE
#define CUSTOM_SHADOWS_INCLUDE

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"//为了简洁是不该写的，但是为了良好的hlsl变成体验，写过来确保每次打开都不会有一堆的报错
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"//使用其中的tent filter卷积核进行采样控制

#if defined(_DIRECTIONAL_PCF3)
    #define DIRECTIONAL_FILTER_SAMPLES 4
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#elif defined(_DIRECTIONAL_PCF5)
    #define DIRECTIONAL_FILTER_SAMPLES 9
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#elif defined(_DIRECTIONAL_PCF7)
    #define DIRECTIONAL_FILTER_SAMPLES 16
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
#endif

#define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_CASCADE_COUNT 4

TEXTURE2D_SHADOW(_DirectionalShadowAtlas);//使用TEXTURE_SHADOW宏来特殊声明，能够让代码更清晰（这一点在我们支持的平台上不会有影响，底层就是一个TEXTURE的宏定义转换）
#define SHADOW_SAMPLER sampler_linear_clamp_compare //compare为DX10新引入的宏，用作深度比较
SAMPLER_CMP(SHADOW_SAMPLER);//使用SAMPLER_CMP宏对采样器状态进行设置，这个宏定义了一个不同的采样方法去采样shadow map，因为常规的双线性采样对深度数据来说没有意义
//同时，我们可以显式定义一个更为准确的采样器状态，不需要unity基于贴图类型为我们推断出采样器状态：https://docs.unity.cn/2020.3/Documentation/Manual/SL-SamplerStates.html
// “Point”, “Linear” or “Trilinear” (required) set up texture filtering mode.
// “Clamp”, “Repeat”, “Mirror” or “MirrorOnce” (required) set up texture wrap mode.
// Wrap modes can be specified per-axis (UVW), e.g. “ClampU_RepeatV”.
// “Compare” (optional) set up sampler for depth comparison; use with HLSL SamplerComparisonState type and SampleCmp / SampleCmpLevelZero functions.

CBUFFER_START(_CustomShadows)
int _CascadeCount;
float4 _CascadeCullingSpheres[MAX_CASCADE_COUNT];
float4x4 _DirectionalShadowMatrices[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT * MAX_CASCADE_COUNT];
float4 _CascadeData[MAX_CASCADE_COUNT];
float4 _ShadowAtlasSize;
float4 _ShadowDistanceFade;
CBUFFER_END

struct DirectionalShadowData
{
    float strength;
    int tileIndex;
    float normalBias;
    //Shadowmask是逐光源的属性，应当存储在光源的shadow data中
    int shadowMaskChannel;
};

//Shader端存储ShadowMask的信息，其中包括一个bool来指明是否使用distance shadow mask
struct CustomShadowMask
{
    bool always;
    bool distance;
    float4 shadows;
};

//因为Cascade Shadow是逐Fragment的信息，不是逐光源的信息，所以新建一个存储Shadow数据的结构体来处理
struct CustomShadowData
{
    int cascadeIndex;
    float cascadeBlend;//用于处理临近cascade之间的插值计算
    float strength;//用于处理超出cascade分级的阴影，实际上如果Fragment阴影超出最远Cascade范围就不该采样阴影贴图了
    CustomShadowMask shadowMask;//将ShadowMask作为字段添加到ShadowData中
};

float SampleDirectionalShadowAtlas(float3 positionSTS)
{
    //STS指的是Shadow Tile Space，也就是光源看向场景的Clip Space
    return SAMPLE_TEXTURE2D_SHADOW(_DirectionalShadowAtlas, SHADOW_SAMPLER, positionSTS);
    //对应特定采样器状态的SAMPLE_TEXTURE2D_SHADOW，如果在阴影中其返回0，如果不在阴影中则返回1
};

//通过这个函数来进行软阴影的最终控制，如果使用PCF2x2，则是直接采样一次贴图；如果是PCF3x3、PCF5x5、PCF7x7，则是采样4、9、16次
float FilterDirectionalShadow(float3 positionSTS)
{
    #if defined(DIRECTIONAL_FILTER_SETUP)
        float weights[DIRECTIONAL_FILTER_SAMPLES];
        float2 positions[DIRECTIONAL_FILTER_SAMPLES];
        float4 size = _ShadowAtlasSize.yyxx;
        DIRECTIONAL_FILTER_SETUP(size, positionSTS.xy, weights, positions);//第一个参数为float4，XY是texel的size，ZW是整个texture的size
        //weight为out的参数，输出一个权重数组；position为out的参数，输出一个UV空间的xy坐标数组
        float shadow = 0;
        for (int i = 0; i < DIRECTIONAL_FILTER_SAMPLES; i++)
        {
            shadow += weights[i] * SampleDirectionalShadowAtlas(float3(positions[i].xy, positionSTS.z));//用得到的weight和position进行采样
        }
        return shadow;
    #else
        return SampleDirectionalShadowAtlas(positionSTS);
    #endif
}

//为了混合Baked Shadow和Realtime Shadow，以及提高可读性，需要拆分函数
//Realtime Shadow
float GetCascadedShadow(DirectionalShadowData directional, CustomShadowData global, Surface surfaceWS)
{
    
    float3 normalBias = surfaceWS.interpolatedNormal * (directional.normalBias * _CascadeData[global.cascadeIndex].y);//在世界空间下进行偏移，偏移的数值由CPU发送而来
    //normal bias的控制既有全局控制，又有逐个光源的控制
    float3 positionSTS = mul(_DirectionalShadowMatrices[directional.tileIndex], float4(surfaceWS.position + normalBias, 1.0)).xyz;
    // float shadow = SampleDirectionalShadowAtlas(positionSTS);
    float shadow = FilterDirectionalShadow(positionSTS);
    //进行不同cascade shadow之间的融合处理
    if(global.cascadeBlend < 1.0)
    {
        //计算当前像素点下一个cascade的阴影强度
        normalBias = surfaceWS.interpolatedNormal * (directional.normalBias * _CascadeData[global.cascadeIndex + 1].y);
        positionSTS = mul(_DirectionalShadowMatrices[directional.tileIndex + 1],
            float4(surfaceWS.position + normalBias, 1.0)
            ).xyz;
        //基于计算出的cascadeBlend（当前cascade Sphere的Fade值）对两个阴影进行插值
        shadow = lerp(FilterDirectionalShadow(positionSTS), shadow, global.cascadeBlend);
    }
    return shadow;
}

//Baked Shadow
float GetBakedShadow(CustomShadowMask mask, int channel)
{
    float shadow = 1.0;
    if (mask.always || mask.distance)
    {
        if(channel >= 0)
        {
            shadow = mask.shadows[channel];
        }
    }
    return shadow;
}
//通过多传入strength实现函数的Variation：用于处理完全使用Baked Shadow的情况（超出Max Shadow Distance）
float GetBakedShadow(CustomShadowMask mask, int channel, float strength)
{
    if (mask.always || mask.distance)
    {
        return lerp(1.0, GetBakedShadow(mask, channel), strength);
    }
    return 1.0;
}

//Blend Realtime Shadows and Baked Shadows
//传入ShadowData、Realtime Shadow数据、Shadow强度
float MixBakedAndRealtimeShadows(CustomShadowData global, float shadow, int shadowMaskChannel, float strength)
{
    float baked = GetBakedShadow(global.shadowMask, shadowMaskChannel);
    if(global.shadowMask.always)
    {
        shadow = lerp(1.0, shadow, global.strength);
        shadow = min(baked, shadow);
        return lerp(1.0, shadow, strength);
    }
    if (global.shadowMask.distance)
    {
        shadow = lerp(baked, shadow, global.strength);//基于shadowData的strength进行阴影的混合，shadowData中strength是通过深度来进行的强度调整，即远处强度低，转换到使用Baked Shadow，近处强度高，使用Realtime Shadow
        return lerp(1.0, shadow, strength);//基于DirectionalLight本身来控制阴影的强弱（整体的阴影强度）
    }
    return lerp(1.0, shadow, strength * global.strength);
}

float GetDirectionalShadowAttenuation(DirectionalShadowData directional, CustomShadowData global, Surface surfaceWS)
{
    #if !defined(_RECEIVE_SHADOWS)
     return 1.0f;
    #endif

    float shadow;
    if (directional.strength * global.strength <= 0.0)//在Shader中用动态分支，在过去是低效的，但是现代GPU能妥善处理他们。但是需要记住，所有fragment都会执行每一个动态分支中的全部内容
    {
        shadow = GetBakedShadow(global.shadowMask, directional.shadowMaskChannel, abs(directional.strength));
    }
    else
    {
        shadow = GetCascadedShadow(directional, global, surfaceWS);
        shadow = MixBakedAndRealtimeShadows(global, shadow, directional.shadowMaskChannel, directional.strength);
    }
    
    return shadow;
}

float FadeShadowStrength(float distance, float scale, float fade)
{
    //线性衰减的计算公式：
    // shadowStrength = (1 - d / m ) / f
    //其中，d表示当前片元的深度，m表示最远的阴影深度-- d / m 获得的是当前片元深度占最远阴影深度的比例，由oneMinus后得到近处为1远处为0的结果
    //f控制的是衰减速度，其本质上是一个strength关于distance的一次函数，函数模型为 y = ( 1 - x / m ) / f
    //最后进行saturate后，将结果钳制在0-1之间，使得近处得到的超过1的阴影强度值为1
    return saturate((1.0 - distance * scale) * fade);
}

CustomShadowData GetShadowData (Surface surfaceWS)
{
    CustomShadowData data;
    //默认设置shadowMask不启用
    data.shadowMask.always = false;
    data.shadowMask.distance = false;
    data.shadowMask.shadows = 1.0;
    data.cascadeBlend = 1.0;//默认当前的cascade是满强度的
    // data.strength = 1.0;//默认返回1，表示在cascade阴影内
    // data.strength = surfaceWS.depth < _ShadowDistance ? 1.0 : 0.0;//进行深度判断来确定shadow strength的默认值
    data.strength = FadeShadowStrength(surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y);//控制阴影强度、范围，并计算阴影衰减
    int i;
    for (i = 0; i < _CascadeCount; i++)
    {
        float4 sphere = _CascadeCullingSpheres[i];
        float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
        if (distanceSqr < sphere.w)
        {
            float fade = FadeShadowStrength(distanceSqr, _CascadeData[i].x, _ShadowDistanceFade.z);
            if(i == _CascadeCount - 1)
            {
                //计算基于sphere culling的cascade shadow的衰减值，公式为 ( 1 - d^2 / r^2 ) / 1 - ( 1 - f^2)，其中分母可以在CPU中处理完再传到GPU中
                // data.strength *= FadeShadowStrength(distanceSqr, _CascadeData[i].x, _ShadowDistanceFade.z);
                data.strength *= fade;
            }
            else
            {
                data.cascadeBlend = fade;
            }
            break;
        }
    }
    //如果i=0，意味着当前片元在Cascade-4的范围之外，则不应该接收阴影
    if (i == _CascadeCount)
    {
        data.strength = 0.0;
    }
    //如果启用了Dither，那么在阴影范围内，在过渡的区域中，进行扰动
    #if defined(_CASCAD_BLEND_DITHER)
    else if (data.cascadeBlend < surfaceWS.dither)
        {
             i += 1;
        }
    #endif
    //处理cascade之间的阴影过渡，如果Soft Blend没有启用，则在最后将阴影强度重新设回1
    #if !defined(_CASCADE_BLEND_SOFT)
        data.cascadeBlend = 1.0;
    #endif
    // data.cascadeIndex = 0;//默认的Cascade Index设置为0
    data.cascadeIndex = i;
    return data;
}



#endif