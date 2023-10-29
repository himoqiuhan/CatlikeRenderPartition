#ifndef CUSTOM_LIGHT_INCLUDE
#define CUSTOM_LIGHT_INCLUDE

#define MAX_DIRECTIONAL_LIGHT_COUNT 4

CBUFFER_START(_CustomLight)
int _DirectionalLightCount;
float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
float4 _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHT_COUNT];
float4 _DirectionalLightShadowData[MAX_DIRECTIONAL_LIGHT_COUNT];
CBUFFER_END

struct Light
{
    float3 color;
    float3 direction;
    float attenuation;
};

DirectionalShadowData GetDirectionalShadowData(int lightIndex, CustomShadowData shadowData)
{
    DirectionalShadowData data;
    data.strength = _DirectionalLightShadowData[lightIndex].x * shadowData.strength;//_DirectionalLightShadowData.x存储的是阴影强度
    //如果在cascade-4之外，则直接将用于计算attenuation的阴影强度设置为0，最终获得的亮度值就为1，获得不接收阴影的效果
    data.tileIndex = _DirectionalLightShadowData[lightIndex].y + shadowData.cascadeIndex;//加上cascadeIndex，获得当前光源正确的cascade阴影
    data.normalBias = _DirectionalLightShadowData[lightIndex].z;
    return data;
}

int GetDirectionalLightCount()
{
    return _DirectionalLightCount;
}

Light GetDirectionalLight(int index, Surface surfaceWS, CustomShadowData shadowData)
{
    Light light;
    light.color = _DirectionalLightColors[index].rgb;
    light.direction = _DirectionalLightDirections[index].xyz;
    DirectionalShadowData dirShadowData = GetDirectionalShadowData(index, shadowData);
    light.attenuation = GetDirectionalShadowAttenuation(dirShadowData, shadowData,surfaceWS);
    // light.attenuation = shadowData.cascadeIndex * 0.25;//使Cascade分级可视化的工具，用cascade来代替实际的阴影贴图采样计算出的attenuation    
    return light;
}

#endif