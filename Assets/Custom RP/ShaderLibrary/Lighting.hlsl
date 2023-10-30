#ifndef CUSTOM_LIGHTING_INCLUDED
#define CUSTOM_LIGHTING_INCLUDED

float3 GetLighting(Surface surfaceWS, BRDF brdf, GI gi);
float3 GetLighting(Surface surface, BRDF brdf, Light light);
float3 InComingLight(Surface surface, Light light);

//全部光源的统一lighting计算
float3 GetLighting(Surface surfaceWS, BRDF brdf, GI gi)
{
    CustomShadowData shadowData = GetShadowData(surfaceWS);
    float3 color = gi.diffuse * brdf.diffuse;//GI -- GI颜色乘上物体的漫反射率
    for (int i = 0; i < GetDirectionalLightCount(); i++)
    {
        Light light = GetDirectionalLight(i, surfaceWS, shadowData);
        color += GetLighting(surfaceWS, brdf, light);
    }
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