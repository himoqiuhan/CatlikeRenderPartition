#ifndef CUSTOM_LIGHT_INCLUDE
#define CUSTOM_LIGHT_INCLUDE

#define MAX_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_OTHER_LIGHT_COUNT 64

CBUFFER_START(_CustomLight)
int _DirectionalLightCount;
float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
float4 _DirectionalLightDirections[MAX_DIRECTIONAL_LIGHT_COUNT];
float4 _DirectionalLightShadowData[MAX_DIRECTIONAL_LIGHT_COUNT];

int _OtherLightCount;
float4 _OtherLightColors[MAX_OTHER_LIGHT_COUNT];
float4 _OtherLightPositions[MAX_OTHER_LIGHT_COUNT];
float4 _OtherLightDirections[MAX_OTHER_LIGHT_COUNT];
float4 _OtherLightSpotAngles[MAX_OTHER_LIGHT_COUNT];
float4 _OtherLightShadowData[MAX_OTHER_LIGHT_COUNT];
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
    //因为要进行Baked Shadowmask和Realtime Shadow的混合，所以不能只是简单地在此处进行阴影的相乘（并且从架构上来说，也不应该在逐光源的GetDirectionalShadowData中运用逐片元控制的shadowData
    data.strength = _DirectionalLightShadowData[lightIndex].x;// * shadowData.strength;//_DirectionalLightShadowData.x存储的是阴影强度
    //如果在cascade-4之外，则直接将用于计算attenuation的阴影强度设置为0，最终获得的亮度值就为1，获得不接收阴影的效果
    data.tileIndex = _DirectionalLightShadowData[lightIndex].y + shadowData.cascadeIndex;//加上cascadeIndex，获得当前光源正确的cascade阴影
    data.normalBias = _DirectionalLightShadowData[lightIndex].z;
    data.shadowMaskChannel = _DirectionalLightShadowData[lightIndex].w;
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
    light.attenuation = GetDirectionalShadowAttenuation(dirShadowData, shadowData, surfaceWS);
    // light.attenuation = shadowData.cascadeIndex * 0.25;//使Cascade分级可视化的工具，用cascade来代替实际的阴影贴图采样计算出的attenuation    
    return light;
}

int GetOtherLightCount()
{
    return _OtherLightCount;
}

OtherShadowData GetOtherShadowData(int lightIndex)
{
    OtherShadowData data;
    data.strength = _OtherLightShadowData[lightIndex].x;
    data.tileIndex = _OtherLightShadowData[lightIndex].y;
    data.isPoint = _OtherLightShadowData[lightIndex].z == 1.0;
    data.shadowMaskChannel = _OtherLightShadowData[lightIndex].w;
    data.lightPositionWS = 0.0;
    data.lightDirectionWS = 0.0;
    data.spotDirectionWS = 0.0;
    return data;
}

Light GetOtherLight(int index, Surface surfaceWS, CustomShadowData shadowData)
{
    Light light;
    light.color = _OtherLightColors[index].rgb;
    float3 position = _OtherLightPositions[index].xyz;
    float3 ray = position - surfaceWS.position;
    light.direction = normalize(ray);
    //计算距离衰减
    float distanceSqr = max(dot(ray, ray), 0.00001);
    float rangeAttenuation = Square(
        saturate(1.0 - Square(distanceSqr * _OtherLightPositions[index].w))
    );
    float4 spotAngles = _OtherLightSpotAngles[index];
    float3 spotDirection = _OtherLightDirections[index];
    float spotAttenuation = Square(
        saturate(dot(spotDirection.xyz, light.direction) *
        spotAngles.x + spotAngles.y)
        );
    OtherShadowData otherShadowData = GetOtherShadowData(index);
    otherShadowData.lightPositionWS = position;
    otherShadowData.lightDirectionWS = light.direction;
    otherShadowData.spotDirectionWS = spotDirection;
    light.attenuation =
        //处理来自ShadowMaks的Light Attenuation
        GetOtherShadowAttenuation(otherShadowData, shadowData, surfaceWS) *
            //处理光源自身的Attenuation
        spotAttenuation * rangeAttenuation / distanceSqr;
    return light;
}

#endif