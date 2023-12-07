#ifndef CUSTOM_LIT_PASS_INCLUDED
#define CUSTOM_LIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "../ShaderLibrary/Common.hlsl"
#include "../ShaderLibrary/UnityInput.hlsl"
#include "../ShaderLibrary/Surface.hlsl"
#include "../ShaderLibrary/Shadows.hlsl"
#include "../ShaderLibrary/Light.hlsl"
#include "../ShaderLibrary/BRDF.hlsl"
#include "../ShaderLibrary/GI.hlsl"
#include "../ShaderLibrary/Lighting.hlsl"

//TEXTURE2D(_MainTex);
//SAMPLER(sampler_MainTex);
//UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
//UNITY_DEFINE_INSTANCED_PROP(float4, _MainTex_ST)
//UNITY_DEFINE_INSTANCED_PROP(half4, _BaseColor)
//UNITY_DEFINE_INSTANCED_PROP(float, _Cutoff)
//UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
//UNITY_DEFINE_INSTANCED_PROP(float, _Smoothness)
//UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    GI_ATTRIBUTE_DATA
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : VAR_PISITION;
    float3 normalWS : VAR_NORMAL;
    float2 baseUV : VAR_MAIN_UV;
    float2 detailUV : VAR_DETAIL_UV;
    GI_VARYINGS_DATA
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings LitPassVertexProgram(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    TRANSFER_GI_DATA(input, output);
    VertexPositionInputs vertexPositionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    //Get Vertex Position in WS VS CS NDC
    output.positionCS = vertexPositionInputs.positionCS;
    output.positionWS = vertexPositionInputs.positionWS;
    VertexNormalInputs vertexNormalInputs = GetVertexNormalInputs(input.normalOS);
    //Only Normal Can Use if just input normalOS
    output.normalWS = vertexNormalInputs.normalWS;
    // float4 mainTexST = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MainTex_ST);
    // output.uv = input.uv * mainTexST.xy + mainTexST.zw;
    output.baseUV = TransformBaseUV(input.uv);
    output.detailUV = TransformDetailUV(input.uv);
    return output;
}

half4 LitPassFragmentProgram(Varyings i) : SV_Target0
{
    UNITY_SETUP_INSTANCE_ID(i);
    //LOD之间的切换
    // #if defined(LOD_FADE_CROSSFADE)
    //     return -unity_LODFade.x;
    // #endif
    ClipLOD(i.positionCS.xy, unity_LODFade.x);
    // half4 baseColor = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _BaseColor);
    // half4 mainTexColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
    // half4 base = baseColor * mainTexColor;
    float4 base = GetBase(i.baseUV, i.detailUV);
    // color.rgb = normalize(i.normalWS);
    //在进行线性插值时，每个片元得到的法线并不是等长的
    // color.rgb = abs(length(i.normalWS)-1.0) * 10.0;
    //所以在计算光照时需要进行normalize
    // color.rgb = abs(length(normalize(i.normalWS))-1.0) * 10.0;

    //Deal with Clipping
    #if defined(_CLIPPING)
        // clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));
        clip(base.a - GetCutOff(i.baseUV));
    #endif

    //Deal with surface attributes
    Surface surface;
    surface.position = i.positionWS;
    surface.normal = normalize(i.normalWS);
    surface.viewDirection = normalize(_WorldSpaceCameraPos - i.positionWS);
    surface.depth = -TransformWorldToView(i.positionWS).z;
    surface.color = base.rgb;
    surface.alpha = base.a;
    surface.occlusion = GetOcclusiton(i.baseUV);
    // surface.metallic = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Metallic);
    surface.metallic = GetMetallic(i.baseUV);
    // surface.smoothness = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Smoothness);
    surface.smoothness = GetSmoothness(i.baseUV, i.detailUV);
    surface.fresnelStrength = GetFresnel(i.baseUV);
    surface.dither = InterleavedGradientNoise(i.positionCS.xy, 0);//使用Core.hlsl中的函数来计算dither扰动采样位置
    //输入的第一个参数是SS的XY position，Fragment Shader中等效于CS的XY position；第二个参数用于控制其动画，不需要动画则直接使用0

    //Deal with GI
    //GI gi = GetGI(GI_FRAGMENT_DATA(i), surface);
    // return half4(gi.diffuse,1.0);
    
    float4 color;
    #if defined(_PREMULTIPLY_ALPHA)
    BRDF brdf = GetBRDF(surface, true);
    #else
    BRDF brdf = GetBRDF(surface);
    #endif
    GI gi = GetGI(GI_FRAGMENT_DATA(i), surface, brdf);
    color.rgb = GetLighting(surface, brdf, gi) + GetEmission(i.baseUV);
    color.a = surface.alpha;

    return color;
}

#endif
