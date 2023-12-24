using UnityEngine;
using UnityEngine.Rendering;
// using System.Collections.Generic;

public partial class CustomRenderPipeline : RenderPipeline
{
    private CameraRenderer renderer = new CameraRenderer();
    //自己创建的CameraRenderer，此处的CameraRenderer几乎等同于URP中的Scriptable Renderer
    private bool allowHDR;
    private bool useDynamicBatching, useGPUInstancing, useLightPerObject;
    private ShadowSettings shadowSettings;
    private PostFXSettings postFXSettings;

    public CustomRenderPipeline(bool allowHDR, bool useDynamicBatching, bool useGPUInstancing, bool useSRPBatcher, 
        bool useLightPerObject, ShadowSettings shadowSettings, 
        PostFXSettings postFXSettings)
    {
        this.allowHDR = allowHDR;
        this.useDynamicBatching = useDynamicBatching;
        this.useGPUInstancing = useGPUInstancing;
        GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
        GraphicsSettings.lightsUseLinearIntensity = true;
        this.useLightPerObject = useLightPerObject;
        this.shadowSettings = shadowSettings;
        this.postFXSettings = postFXSettings;
        InitializeForEditor();
    }

    protected override void
        Render(ScriptableRenderContext context, Camera[] cameras) //后续更高的Unity版本中使用List<Camera>代替Camera[]来优化内存分配情况
    {
        for (int i = 0; i < cameras.Length; i++)
        {
            renderer.Render(context, cameras[i], allowHDR,
                useDynamicBatching, useGPUInstancing, 
                useLightPerObject, shadowSettings,
                postFXSettings);
        }
    }
}