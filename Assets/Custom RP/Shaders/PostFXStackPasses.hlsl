#ifndef CUSTOM_POST_FX_PASSES_INCLUDED
#define CUSTOM_POST_FX_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"

TEXTURE2D(_PostFXSource);
TEXTURE2D(_PostFXSource2);
SAMPLER(sampler_linear_clamp);

float4 _ProjectionParams;
float4 _PostFXSource_TexelSize;

float4 GetSourceTexelSize()
{
    return _PostFXSource_TexelSize;
}

float4 GetSource(float2 screenUV)
{
    return SAMPLE_TEXTURE2D_LOD(_PostFXSource, sampler_linear_clamp, screenUV, 0);
}

float4 GetSource2(float2 screenUV)
{
    return SAMPLE_TEXTURE2D_LOD(_PostFXSource2, sampler_linear_clamp, screenUV, 0);
}

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : VAR_SCREEN_UV;
};

Varyings DefaultPassVertex(uint vertexID : SV_VertexID)
{
    Varyings output;
    output.positionCS = float4(
        vertexID <= 1 ? -1.0 : 3.0,
        vertexID == 1 ? 3.0 : -1.0,
        0.0, 1.0
    );
    output.screenUV = float2(
        vertexID <= 1 ? 0.0 : 2.0,
        vertexID == 1 ? 2.0 : 0.0
    );
    //对于一些目标平台的V坐标是反过来的(OpenGL和DX的差异)，所以得到的结果可能是反过来的，这里需要进行一步针对不同平台的处理
    //（但是由于前期架构不完善，动用了Core.hlsl的东西，导致我没有启用UnityInput，所以这里用不了）
    if (_ProjectionParams.x < 0.0)
    {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
    return output;
}

float4 CopyPassFragment(Varyings input) : SV_Target
{
    return GetSource(input.screenUV);
}

//用于处理ACES颜色空间下亮度的Luminance函数变体
float Luminance(float3 color, bool useACES)
{
    return useACES ? AcesLuminance(color) : Luminance(color);
}

//---------------Bloom---------------
float4 BloomHorizontalPassFragment(Varyings input) : SV_Target
{
    float3 color = 0.0;
    //9x9的高斯盒
    float offsets[] = {
        -4.0, -3.0, -2.0, -1.0, 0.0, 1.0, 2.0, 3.0, 4.0
    };
    //Weights来自帕斯卡三角（杨辉三角）
    float weights[] = {
        0.01621622, 0.05405405, 0.12162162, 0.19459459, 0.22702703,
        0.19459459, 0.12162162, 0.05405405, 0.01621622
    };
    for (int i = 0; i < 9; i++)
    {
        //*2.0是为了实现降采样
        float offset = offsets[i] * 2.0 * GetSourceTexelSize().x;
        color += GetSource(input.screenUV + float2(offset, 0.0)).rgb * weights[i];
    }
    return float4(color, 1.0);
}

//因为是后进行的Vertical，所以在Vertical的采样上可以进行优化，使用5x5的高斯核处理（采样的是原图经过Bilinear降采样后得到的图）
float4 BloomVerticalPassFragment(Varyings input) : SV_Target
{
    float3 color = 0.0;
    //9x9的高斯盒
    float offsets[] = {
        -3.23076923, -1.38461538, 0.0, 1.38461538, 3.23076923
    };
    //Weights来自帕斯卡三角（杨辉三角）
    float weights[] = {
        0.07027027, 0.31621622, 0.22702703, 0.31621622, 0.07027027
    };
    for (int i = 0; i < 5; i++)
    {
        //在Horizontal的Pass中已经完成了降采样，所以此处不需要再进行*2.0
        //处理Vertical时的输入已经是降采样过的贴图了
        float offset = offsets[i] * GetSourceTexelSize().y;
        color += GetSource(input.screenUV + float2(0.0, offset)).rgb * weights[i];
    }
    return float4(color, 1.0);
}

//Bloom的混合
bool _BloomBicubicUpsampling;
float _BloomIntensity;

float4 GetSourceBicubic(float2 screenUV)
{
    //Bicubic采样能减少Bloom效果的方块感，但是需要4次采样
    return SampleTexture2DBicubic(
        TEXTURE2D_ARGS(_PostFXSource, sampler_linear_clamp), screenUV,
        _PostFXSource_TexelSize.zwxy, 1.0, 0.0
    );
}

float4 BloomAddPassFragment(Varyings input) : SV_Target
{
    float3 lowRes;
    //Bicubic采样会带来一定的性能消耗，所以将其设置为静态分支（URP和HDRP中的High Quality Bloom就是控制这个的）
    if (_BloomBicubicUpsampling)
    {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else
    {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float3 highRes = GetSource2(input.screenUV).rgb;
    //Add是基于强度对低层级进行叠加
    return float4(lowRes * _BloomIntensity + highRes, 1.0);
}

//使用Knee Curve计算Bloom区域
float4 _BloomThreshold;

float3 ApplyBloomThreshold(float3 color)
{
    float brightness = Max3(color.r, color.g, color.b);
    float soft = brightness + _BloomThreshold.y;
    soft = clamp(soft, 0.0, _BloomThreshold.z);
    soft = soft * soft * _BloomThreshold.w;
    float contribution = max(soft, brightness - _BloomThreshold.x);
    contribution /= max(brightness, 0.00001);
    return color * contribution;
}

float4 BloomPrefilterPassFragment(Varyings input) : SV_Target
{
    float3 color = ApplyBloomThreshold(GetSource(input.screenUV).rgb);
    return float4(color, 1.0);
}

float4 BloomPrefilterFirefliesPassFragment(Varyings input) : SV_Target
{
    float3 color = 0.0;
    float weightSum = 0.0;
    float2 offsets[] = {
        float2(0.0, 0.0),
        float2(-1.0, -1.0), float2(-1.0, 1.0), float2(1.0, -1.0), float2(1.0, 1.0),
        //简单优化：因为后续会对Prefilter的结果进行高斯模糊，其处理会横向+纵向处理；维持效果的同时，我们在这里可以省去“上下左右”四个点的采样
        //得到的Prefilter一个像素处理后会呈现出X的样子，但是在第一次高斯模糊完成后其结果与9个采样点的效果差别很小
        //float2(-1.0, 0.0), float2(1.0, 0.0), float2(0.0, -1.0), float2(0.0, 1.0)
    };
    for (int i = 0; i < 5; i++)
    {
        //因为输出的目标是半分辨率，所以偏移值需要*2
        float3 c = GetSource(input.screenUV + offsets[i] * GetSourceTexelSize().xy * 2.0).rgb;
        c = ApplyBloomThreshold(c);
        float w = 1.0 / (Luminance(c) + 1.0);
        color += c * w;
        weightSum += w;
    }
    color /= weightSum;
    return float4(color, 1.0);
}

//用于Bloom Scatter的Fragment混合函数
float4 BloomScatterPassFragment(Varyings input) : SV_Target
{
    float3 lowRes;
    if (_BloomBicubicUpsampling)
    {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else
    {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float3 highRes = GetSource2(input.screenUV).rgb;
    //Scatter是基于BloomIntensity进行逐级混合之间的插值，并不再是简单的相加
    return float4(lerp(highRes, lowRes, _BloomIntensity), 1.0);
}

float4 BloomScatterFinalPassFragment(Varyings input) : SV_Target
{
    float3 lowRes;
    if (_BloomBicubicUpsampling)
    {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else
    {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float3 highRes = GetSource2(input.screenUV).rgb;
    //在这一套流程中，需要通过原图与混合后的图像的lerp来得到最终的结果，所以需要额外对lowRes的处理
    //此时输入的lowRes只是Bloom区域Scatter的混合结果，非Bloom区域是全黑的，需要将非Bloom区域加入到lowRes中
    lowRes += highRes - ApplyBloomThreshold(highRes);
    return float4(lerp(highRes, lowRes, _BloomIntensity), 1.0);
}

//---------------Color Grading---------------

float4 _ColorAdjustments;
float4 _ColorFilter;
float4 _WhiteBalance;
float4 _SplitToningShadows, _SplitToningHighlights;
float4 _ChannelMixerRed, _ChannelMixerGreen, _ChannelMixerBlue;
float4 _SMHShadows, _SMHMidtones, _SMHHighlights, _SMHRange;

//调整曝光--模拟相机的曝光，在所有后处理FX之后、在所有Color Grading之前运用
float3 ColorGradePostExposure(float3 color)
{
    return color * _ColorAdjustments.x;
}

//调整白平衡
float3 ColorGradeWhiteBalance(float3 color)
{
    color = LinearToLMS(color);
    color *= _WhiteBalance.rgb;
    return LMSToLinear(color);
}

//调整对比度--通过减去中灰色，对结果进行缩放，然后再加上中灰色来实现，这里使用的中灰色是ACEScc(ACES颜色空间的一个对数子集)的中灰色
float3 ColorGradingContrast(float3 color, bool useACES)
{
    //使用ACES时，Contract一般是在ACES的颜色空间中进行计算处理的
    color = useACES ? ACES_to_ACEScc(unity_to_ACES(color)) : LinearToLogC(color);
    color = (color - ACEScc_MIDGRAY) * _ColorAdjustments.y + ACEScc_MIDGRAY;
    //ACEScg是ACES颜色空间的一个线性子集
    return useACES ? ACES_to_ACEScg(ACEScc_to_ACES(color)) : LogCToLinear(color);
}

//调整颜色滤镜,直接乘颜色值即可
float3 ColorGradeColorFilter(float3 color)
{
    return color * _ColorFilter.rgb;
}

//调整亮部/暗部各自的颜色
float3 ColorGradeSplitToning(float3 color, bool useACES)
{
    //在Gamma空间中调整,更为直观
    color = PositivePow(color, 1.0 / 2.2);
    float t = saturate(Luminance(saturate(color), useACES) + _SplitToningShadows.w);
    float3 shadows = lerp(0.5, _SplitToningShadows.rgb, 1.0 - t);
    float3 highlights = lerp(0.5, _SplitToningHighlights.rgb, t);
    //使用SoftLight来运用color的改变
    color = SoftLight(color, shadows);
    color = SoftLight(color, highlights);
    return PositivePow(color, 2.2);
}

//调整色相
float3 ColorGradingHueShift(float3 color)
{
    color = RgbToHsv(color);
    float hue = color.x + _ColorAdjustments.z;
    //此处将色盘定义在0-1之间,RotateHue的作用就是确保值的改变在色盘范围内(似乎源码是用来区分三个部分变化的)
    color.x = RotateHue(hue, 0.0, 1.0);
    return HsvToRgb(color);
}

//调整饱和度
float3 ColorGradingSaturation(float3 color, bool useACES)
{
    float luminance = Luminance(color, useACES);
    return (color - luminance) * _ColorAdjustments.w + luminance;
}

//Channel Mixer
float3 ColorGradingChannelMixer(float3 color)
{
    return mul(
        float3x3(_ChannelMixerRed.rgb, _ChannelMixerGreen.rgb, _ChannelMixerBlue.rgb),
        color
    );
}

//Shadows Midtones Highlights
float3 ColorGradingShadowsMidtonesHighlights(float3 color, bool useACES)
{
    float luminance = Luminance(color, useACES);
    float shadowsWeight = 1.0 - smoothstep(_SMHRange.x, _SMHRange.y, luminance);
    float highlightsWeight = smoothstep(_SMHRange.z, _SMHRange.w, luminance);
    float midtonesWeight = 1.0 - shadowsWeight - highlightsWeight;
    return
        color * _SMHShadows.rgb * shadowsWeight +
            color * _SMHMidtones.rgb * midtonesWeight +
                color * _SMHHighlights.rgb * highlightsWeight;
}

float3 ColorGrade(float3 color, bool useACES = false)
{
    //color = min(color, 60.0); -- 60的限制源自于LogC空间最大值为59，但是当我们使用LUT后，颜色值的范围已经被LUT限制了，所以不再需要此处多加限制
    color = ColorGradePostExposure(color);
    color = ColorGradeWhiteBalance(color);
    color = ColorGradingContrast(color, useACES);
    color = ColorGradeColorFilter(color); //Color Filter的处理可以接受负数
    color = max(color, 0.0); //处理完Contrast之后，颜色值可能会有负数，会影响之后的步骤，所以在这里加一个限制
    color = ColorGradeSplitToning(color, useACES);
    color = ColorGradingChannelMixer(color);
    color = max(color, 0.0);
    color = ColorGradingShadowsMidtonesHighlights(color, useACES);
    color = ColorGradingHueShift(color);
    color = ColorGradingSaturation(color, useACES);
    //处理完Saturation之后同样可能会出现负值;同时如果使用了ACES颜色空间，最后输出的颜色应该是ACES颜色空间下的值
    color = max(useACES ? ACEScg_to_ACES(color):color, 0.0); 
    return color;
}

//---------------LUT---------------
float4 _ColorGradingLUTParameters;
bool _ColorGradingLUTInLogC;
TEXTURE2D(_ColorGradingLUT);

float3 GetColorGradeLUT(float2 uv, bool useACES = false)
{
    float3 color = float3(uv, 0.0);
    return ColorGrade(color, 1.0);
}

float3 GetColorGradedLUT(float2 uv, bool useACES = false)
{
    float3 color = GetLutStripValue(uv, _ColorGradingLUTParameters);
    //如果是使用ColorGradingLUTInLogC，则有关color的计算会被视作在LogC空间中完成的，在输出时需要转换到线性空间进行存储
    return ColorGrade(_ColorGradingLUTInLogC ? LogCToLinear(color) : color, useACES);
}

float3 ApplyColorGradingLUT(float3 color)
{
    return ApplyLut2D(
        TEXTURE2D_ARGS(_ColorGradingLUT, sampler_linear_clamp),
        saturate(_ColorGradingLUTInLogC ? LinearToLogC(color) : color),
        _ColorGradingLUTParameters.xyz
        );
}

//---------------Tone Mapping---------------
float4 ColorGradingNonePassFragment(Varyings input) : SV_Target
{
    float3 color = GetColorGradedLUT(input.screenUV);
    return float4(color, 1.0);
}

float4 ColorGradingACESPassFragment(Varyings input) : SV_Target
{
    float3 color = GetColorGradedLUT(input.screenUV, true);
    color = AcesTonemap(color);
    return float4(color, 1.0);
}

float4 ColorGradingNeutralPassFragment(Varyings input) : SV_Target
{
    float3 color = GetColorGradedLUT(input.screenUV);
    color = NeutralTonemap(color);
    return float4(color, 1.0);
}

float4 ColorGradingReinhardPassFragment(Varyings input) : SV_Target
{
    float3 color = GetColorGradedLUT(input.screenUV);
    color /= color + 1.0;
    return float4(color, 1.0);
}

//---------------Final Pass---------------
float4 FinalPassFragment(Varyings input) : SV_Target
{
    float4 color = GetSource(input.screenUV);
    color.rgb = ApplyColorGradingLUT(color.rgb);
    return color;
}

#endif
