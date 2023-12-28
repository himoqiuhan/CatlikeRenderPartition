using System;
using UnityEngine;
using UnityEngine.Rendering;
//using static功能类似于using一个namespace，这个是用于变量的
using static PostFXSettings;

public partial class PostFXStack
{
    //同Lighting、Shadow一致的Stack结构
    
    private const string bufferName = "Post FX";

    private CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };

    private ScriptableRenderContext context;

    private Camera camera;

    private PostFXSettings settings;

    private int 
        bloomBicubicUpsamplingId = Shader.PropertyToID("_BloomBicubicUpsampling"),
        bloomPrefilterId = Shader.PropertyToID("_BloomPrefilter"),
        bloomResultId = Shader.PropertyToID("_BloomResult"),
        bloomThresholdId = Shader.PropertyToID("_BloomThreshold"),
        bloomIntensityId = Shader.PropertyToID("_BloomIntensity"),
        colorAdjustmentsId = Shader.PropertyToID("_ColorAdjustments"),
        colorFilterId = Shader.PropertyToID("_ColorFilter"),
        whiteBalanceId = Shader.PropertyToID("_WhiteBalance"),
        splitToningShadowsId = Shader.PropertyToID("_SplitToningShadows"),
        splitToningHighlightsId = Shader.PropertyToID("_SplitToningHighlights"),
        channelMixerRedId = Shader.PropertyToID("_ChannelMixerRed"),
        channelMixerGreenId = Shader.PropertyToID("_ChannelMixerGreen"),
        channelMixerBlueId = Shader.PropertyToID("_ChannelMixerBlue"),
        smhShadowsId = Shader.PropertyToID("_SMHShadows"),
        smhMidtonesId = Shader.PropertyToID("_SMHMidtones"),
        smhHighlightId = Shader.PropertyToID("_SMHHighlights"),
        smhRangeId = Shader.PropertyToID("_SMHRange"),
        fxSourceId = Shader.PropertyToID("_PostFXSource"),
        fxSource2Id = Shader.PropertyToID("_PostFXSource2");

    private const int maxBloomPyramidLevels = 16;

    private int bloomPyramidId;
    
    //用于添加Pass的Enum
    enum Pass
    {
        BloomAdd, 
        BloomHorizontal,
        BloomPrefilter , BloomPrefilterFireflies , 
        BloomScatter, BloomScatterFinal, 
        BloomVertical,
        Copy,
        ToneMappingNone,
        ToneMappingACES,
        ToneMappingNeural,
        ToneMappingReinhard,
    }

    //PostFX是否启用，由是否有PostFXSettings来判断
    public bool IsActive => settings != null;
    //Post FX HDR处理
    private bool useHDR;

    public PostFXStack()
    {
        //PropertyToID会按照顺序执行，所以我们只需要获取第一个Pyramid的ID就可以获取到后续的ID
        bloomPyramidId = Shader.PropertyToID("_BloomPyramid0");
        for (int i = 1; i < maxBloomPyramidLevels * 2; i++)
        {
            Shader.PropertyToID("_BloomPyramid" + i);
        }
    }
    
    public void Setup(ScriptableRenderContext context, Camera camera, PostFXSettings settings, bool useHDR)
    {
        this.context = context;
        this.camera = camera;
        this.settings = 
            camera.cameraType <= CameraType.SceneView ? settings : null;
        ApplySceneViewState();
        this.useHDR = useHDR;
    }

    public void Render(int sourceId)
    {
        //运用效果的原理是借助shader绘制一个包含整个图像的三角形
        //通过Blit函数来执行，此处以sourceId和CameraTarget作为参数，传入图像源与渲染的目标
        //buffer.Blit(sourceId, BuiltinRenderTextureType.CameraTarget);
        //Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);
        if (DoBloom(sourceId))
            //if判断，如果执行了Bloom则以Bloom的结果为源进行ToneMapping；如果没有执行Bloom，则直接将原输入作为ToneMapping的输入
        {
            DoColorGradingAndToneMapping(bloomResultId);
            buffer.ReleaseTemporaryRT(bloomResultId);
        }
        else
        {
            DoColorGradingAndToneMapping(sourceId);
        }
        //然后通过Context.ExecuteCommandBuffer和Clear来执行并清空Buffer
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    //使用自己的绘制函数来替换Blit，实现构造一个大三角形绘制后处理，而非像Blit那样的通过两个三角形进行绘制
    void Draw(RenderTargetIdentifier from, RenderTargetIdentifier to, Pass pass)
    {
        buffer.SetGlobalTexture(fxSourceId, from);
        buffer.SetRenderTarget(to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        buffer.DrawProcedural(
            Matrix4x4.identity, settings.Material, (int)pass,
            MeshTopology.Triangles, 3);
    }
    
    //执行Bloom
    bool DoBloom(int sourceId)
        //基于ToneMapping的处理，将DoBloom返回值设置为Bool，区分是否使用Bloom来处理ToneMapping的输入源
    {
        //buffer.BeginSample("Bloom");
        BloomSettings bloom = settings.Bloom;
        int width = camera.pixelWidth / 2, height = camera.pixelHeight / 2;

        //迭代次数为0，或是贴图Texel小于最低大小限制的情况，直接使用Copy，不再进行模糊与混合
        //可能是为了代码的一致性，不考虑在外关闭DoBloom
        if (bloom.maxIterations == 0 || bloom.intensity <= 0f || 
            height < bloom.downscaleLimit * 2 || width < bloom.downscaleLimit * 2)
        {
            // Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);
            // buffer.EndSample("Bloom");
            return false;
        }
        
        buffer.BeginSample("Bloom");
        
        //计算Bloom区域阈值的Knee Curve
        Vector4 threshold;
        threshold.x = Mathf.GammaToLinearSpace(bloom.threshold);
        threshold.y = threshold.x * bloom.thresholdKnee;
        threshold.z = 2f * threshold.y;
        threshold.w = 0.25f / (threshold.y + 0.00001f);
        threshold.y -= threshold.x;
        buffer.SetGlobalVector(bloomThresholdId, threshold);
        
        //区分LDR Bloom和HDR Bloom的处理，即使用不同的RT
        RenderTextureFormat format = useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        //通过一个半分辨率的Prefilter来优化性能，同时处理Bloom区域的Filter
        buffer.GetTemporaryRT(bloomPrefilterId, width, height, 0, FilterMode.Bilinear, format);
        Draw(sourceId, bloomPrefilterId, 
            bloom.fadeFireflies ? Pass.BloomPrefilterFireflies : Pass.BloomPrefilter);
        //复制完后，进一步减半分辨率，用于后续BloomPyramid的计算
        width /= 2;
        height /= 2;
        
        int fromId = bloomPrefilterId, toId = bloomPyramidId + 1;
        int i;
        for (i = 0; i < bloom.maxIterations; i++)
        {
            if (height < bloom.downscaleLimit || width < bloom.downscaleLimit)
            {
                break;
            }

            int midId = toId - 1;
            buffer.GetTemporaryRT(midId, width, height, 0, FilterMode.Bilinear, format);
            buffer.GetTemporaryRT(toId, width, height, 0, FilterMode.Bilinear, format);
            Draw(fromId, midId, Pass.BloomHorizontal);
            Draw(midId, toId, Pass.BloomVertical);
            fromId = toId;
            toId += 2;
            width /= 2;
            height /= 2;
        }
        
        //后续需要使用的的Mips都在BloomPyramid中，所以此处可以释放Prefilter
        buffer.ReleaseTemporaryRT(bloomPrefilterId);
        
        //在升采样前将Bicubic设置传递给GPU
        buffer.SetGlobalFloat(bloomBicubicUpsamplingId, bloom.bicubicUpsampling ? 1f : 0f);
        
        
        //Scattering开启/关闭
        Pass combinePass, finalPass;
        float finalIntensity;
        if (bloom.mode == BloomSettings.Mode.Additive)
        {
            combinePass = finalPass = Pass.BloomAdd;
            //在同一个Combine Pass中实现对Intensity的控制，但是使用Additive模式时，Intensity只用于控制最终的混合，所以在升采样的时候设置为1f
            buffer.SetGlobalFloat(bloomIntensityId, 1f);
            finalIntensity = bloom.intensity;
        }
        else
        {
            combinePass = Pass.BloomScatter;
            finalPass = Pass.BloomScatterFinal;
            buffer.SetGlobalFloat(bloomIntensityId, bloom.scatter);
            finalIntensity = Mathf.Min(bloom.intensity, 0.95f);
        }
        
        
        // Draw(fromId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);
        if (i > 1)
        {
            //迭代次数>=2时的情况
            //进行升采样
            //释放最后一次用于Horizontal模糊的目标RT
            buffer.ReleaseTemporaryRT(fromId - 1);
            //然后将目标设置为上一层级（金字塔下一层）的mid RT（Horizontal Draw的目标RT）
            toId -= 5;//-5的原因：常规理解是-3，但是降采样过程中，最后得到最低限度的RT（金字塔最高RT）之后，对toId再进行了一次+2，所以是-5

            //进入升采样的循环前，toId停留在上一级Mip的Horizontal Draw Target上，fromId停留在当前Mip的模糊结果上
            for (i -= 1; i > 0; i--)
            {
                //逻辑是混合当前mip结果与上一级mip结果，存到计算上一级mip使用的Horizontal Draw Target上
                buffer.SetGlobalTexture(fxSource2Id, toId + 1);//此时的toId+1为上一级的mip
                Draw(fromId, toId, combinePass);
                //在循环中释放的是当前mip混合结果与上一级的mip结果（已经与上一级混合完成，所以上一级结果可以释放）
                //（注：fromId第一次释放的是mip，后续均为Horizontal Target形式的mid mip，所以在循环开始前需要释放当前的mid mip）
                buffer.ReleaseTemporaryRT(fromId);
                buffer.ReleaseTemporaryRT(toId + 1);
                fromId = toId;
                toId -= 2;
            }
        }
        else
        {
            //迭代次数=1，即只进行一次高斯模糊计算的情况，不再需要升采样
            buffer.ReleaseTemporaryRT(bloomPyramidId);
        }
        
        //最终混合前设置Bloom Intensity为BloomSetting中的值
        buffer.SetGlobalFloat(bloomIntensityId, finalIntensity);
        //当i=0时，得到最终的结果是只剩下_BloomPyramid0，其中存储所有mips的混合结果
        buffer.SetGlobalTexture(fxSource2Id, sourceId);
        buffer.GetTemporaryRT(
            bloomResultId, camera.pixelWidth, camera.pixelHeight, 0,
            FilterMode.Bilinear, format
            );
        Draw(fromId, bloomResultId, finalPass);
        buffer.ReleaseTemporaryRT(fromId);
        
        buffer.EndSample("Bloom");

        return true;
    }

    void ConfigureColorAdjustments()
    {
        ColorAdjustmentsSettings colorAdjustments = settings.ColorAdjustments;
        buffer.SetGlobalVector(colorAdjustmentsId, new Vector4(
            Mathf.Pow(2f, colorAdjustments.postExposure),
            colorAdjustments.contrast * 0.01f + 1f, // Range 0 ~ 2
            colorAdjustments.hueShift * (1f / 360f), // Range -1 ~ 1
            colorAdjustments.saturation * 0.01f + 1f // Range 0 ~ 2
        ));
        //Color Filter必须要是线性空间的颜色
        buffer.SetGlobalColor(colorFilterId, colorAdjustments.colorFilter.linear);
    }

    void ConfigureWhiteBalance()
    {
        WhiteBalanceSettings whiteBalance = settings.WhiteBalance;
        buffer.SetGlobalVector(whiteBalanceId, ColorUtils.ColorBalanceToLMSCoeffs(
            whiteBalance.temperature, whiteBalance.tint
            ));
    }

    void ConfigureSplitToning()
    {
        SplitToningSettings splitToning = settings.SplitToning;
        Color splitColor = splitToning.shadows;
        splitColor.a = splitToning.balance * 0.01f;
        //将两个Gamma空间下Color的RGB传入Shader,其中可以将balance数值存入其中一个颜色的A通道
        buffer.SetGlobalColor(splitToningShadowsId, splitColor);
        buffer.SetGlobalColor(splitToningHighlightsId, splitToning.hightlights);
    }

    void ConfigureChannelMixer()
    {
        ChannelMixerSettings channelMixer = settings.ChannelMixer;
        buffer.SetGlobalVector(channelMixerRedId, channelMixer.red);
        buffer.SetGlobalVector(channelMixerGreenId, channelMixer.green);
        buffer.SetGlobalVector(channelMixerBlueId, channelMixer.blue);
    }

    void ConfigureShadowsMidtonesHighlights()
    {
        ShadowsMidtonesHighlightsSettings smh = settings.ShadowsMidtonesHightlights;
        buffer.SetGlobalColor(smhShadowsId, smh.shadows.linear);
        buffer.SetGlobalColor(smhMidtonesId, smh.midtones.linear);
        buffer.SetGlobalColor(smhHighlightId, smh.highlights.linear);
        buffer.SetGlobalVector(smhRangeId, new Vector4(
            smh.shadowsStart, smh.shadowsEnd, smh.highlightsStart, smh.highlightsEnd
            ));
    }
    
    //ColorGrading和ToneMapping放在一起执行
    void DoColorGradingAndToneMapping(int sourceId)
    {
        ConfigureColorAdjustments();
        ConfigureWhiteBalance();
        ConfigureSplitToning();
        ConfigureChannelMixer();
        ConfigureShadowsMidtonesHighlights();
        
        ToneMappingSettings.Mode mode = settings.ToneMapping.mode;
        Pass pass = Pass.ToneMappingNone + (int)mode;
        Draw(sourceId, BuiltinRenderTextureType.CameraTarget, pass);
    }
}