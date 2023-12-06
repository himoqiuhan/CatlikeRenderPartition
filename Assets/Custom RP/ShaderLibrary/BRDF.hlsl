#ifndef CUSTOM_BRDF_INCLUDED
#define CUSTOM_BRDF_INCLUDED

#define MIN_REFLECTIVITY 0.04 //非金属的平均反射率为0.04

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

struct BRDF
{
    float3 diffuse;
    float3 specular;
    float roughness;
    float perceptualRoughness;//用于获取environment map的mip等级
    float fresnel;
};

float OneMinusReflectivity(float metallic)
{
    float range = 1.0 - MIN_REFLECTIVITY;
    return range - range * metallic;//以0.04作为最小的反射率，否则非金属会完全没有高光，不符合现实
}

BRDF GetBRDF(Surface surface, bool applyAlphaToDiffuse = false)
{
    BRDF brdf;
    float oneMinusReflectivity = OneMinusReflectivity(surface.metallic);
    brdf.diffuse = surface.color * oneMinusReflectivity;
    if (applyAlphaToDiffuse)
    {
        brdf.diffuse *= surface.alpha;
    }
    brdf.specular = lerp(MIN_REFLECTIVITY, surface.color, surface.metallic);//金属表面会影响高光反射颜色，而非金属表面不会影响高光反射颜色
    brdf.perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(surface.smoothness);
    brdf.roughness = PerceptualRoughnessToRoughness(brdf.perceptualRoughness);
    brdf.fresnel = saturate(surface.smoothness + 1.0 - oneMinusReflectivity);
    return brdf;
}

float SpecularStrengh(Surface surface, BRDF brdf, Light light)
//Use minimalist CookTorrance BRDF
{
    float3 h = SafeNormalize(light.direction + surface.viewDirection);
    float nh2 = Square(saturate(dot(surface.normal, h)));
    float lh2 = Square(saturate(dot(surface.normal, light.direction)));
    float r2 = Square(brdf.roughness);
    float d2 = Square(nh2 * (r2 - 1.0) + 1.00001);
    float normalization =  brdf.roughness * 4.0 + 2.0;
    return r2 / (d2 * max(0.1, lh2) * normalization);
}

float3 DirectBRDF(Surface surface, BRDF brdf, Light light)
{
    return SpecularStrengh(surface, brdf, light) * brdf.specular + brdf.diffuse;
}

float3 IndirectBRDF(Surface surface, BRDF brdf, float3 diffuse, float3 specular)
{
    //Schlick's approximation for Fresnel
    float fresnelStrength = Pow4(1.0 - saturate(dot(surface.normal, surface.viewDirection))) * surface.fresnelStrength;
    float3 reflection = specular * lerp(brdf.specular, brdf.fresnel, fresnelStrength);
    //粗糙度影响Reflection强度，除以粗糙度的平方加一，使得高粗糙度不易显示反射，而低粗糙度会产生反射
    reflection /= brdf.roughness * brdf.roughness + 1.0;
    
    return diffuse * brdf.diffuse + reflection;
}

#endif