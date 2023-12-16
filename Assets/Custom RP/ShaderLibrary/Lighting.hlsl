#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

float3 GetLighting(Surface surfaceWS, BRDF brdf, GI gi);
float3 GetLighting(Surface surface, BRDF brdf, Light light);
float3 InComingLight(Surface surface, Light light);

//全部光源的统一lighting计算
float3 GetLighting(Surface surfaceWS, BRDF brdf, GI gi)
{
    CustomShadowData shadowData = GetShadowData(surfaceWS);
    //在GetLighting时将GI中shadowMask的数据传递给shadowData，用于后续实际的光照计算
    shadowData.shadowMask = gi.shadowMask;
    //return gi.shadowMask.shadows;//Debug for shadowmask
    // float3 color = gi.diffuse * brdf.diffuse;//GI -- GI颜色乘上物体的漫反射率
    float3 color = IndirectBRDF(surfaceWS, brdf, gi.diffuse, gi.specular);//GI统一在IndirectBRDF中处理，计算GI的漫反射和反射
    for (int i = 0; i < GetDirectionalLightCount(); i++)
    {
        Light light = GetDirectionalLight(i, surfaceWS, shadowData);
        color += GetLighting(surfaceWS, brdf, light);
    }

#if defined(_LIGHTS_PER_OBJECT)
    for (int j = 0; j < min(unity_LightData.y, 8); j++)
    {
        int lightIndex = unity_LightIndices[(uint)j / 4][(uint)j % 4];
        Light light = GetOtherLight(lightIndex, surfaceWS, shadowData);
        color += GetLighting(surfaceWS, brdf, light);
    }
#else
    for(int j = 0; j < GetOtherLightCount(); j++)
    {
        Light light = GetOtherLight(j, surfaceWS, shadowData);
        color += GetLighting(surfaceWS, brdf, light);
    }
#endif    
    return color;
}

//逐个光源lighting的计算
float3 GetLighting(Surface surface, BRDF brdf, Light light)
{
    //光照计算与BRDF解耦
    return InComingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

//光照计算
float3 InComingLight(Surface surface, Light light)
{
    return saturate(dot(surface.normal, light.direction)) * light.attenuation * light.color;
}

#endif