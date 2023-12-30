using UnityEditor.TerrainTools;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CameraRenderer
//使用Partial Class来整理代码：在编辑器看来，Partial Class中的代码都在同一个类中，使用的目的只是方便整理代码，最常用于将自动生成的代码与手写的代码区分开
{
    //Basic Settings
    private ScriptableRenderContext context;
    private Camera camera;
    private const string bufferName = "Render Camera";

    private CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
        //这样书写可以在不调用构造函数、以及显式给类成员变量赋值的前提下，给特定的类成员变量进行数值的初始化；比起构造函数，可以不需要传入所有构造函数的参数，只需要传入必要的构造函数
    };

    private CullingResults cullingResults;

    //ShaderTags
    private static ShaderTagId unlitShaderTagId = new ShaderTagId("SRPUnlit"),
        litShaderTagId = new ShaderTagId("CustomLit");

    private static int frameBufferId = Shader.PropertyToID("_CameraFrameBuffer");

    private static CameraSettings defaultCameraSettings = new CameraSettings();

    //Light
    private Lighting lighting = new Lighting();
    //PostFX
    private PostFXStack postFXStack = new PostFXStack();
    //HDR Settings
    private bool useHDR;

    public void Render(ScriptableRenderContext context, Camera camera, bool allowHDR,
        bool useDynamicBatching, bool useGPUInstancing, 
        bool useLightPerObject, ShadowSettings shadowSettings,
        PostFXSettings postFXSettings, int colorLUTResolution)
    {
        this.context = context;
        this.camera = camera;

        var crpCamera = camera.GetComponent<CustomRenderPipelineCamera>();
        CameraSettings cameraSettings = crpCamera ? crpCamera.Settings : defaultCameraSettings;
        
        this.useHDR = allowHDR && camera.allowHDR;

        PrepareBuffer();
        PrepareForSceneWindow();

        if (!Cull(shadowSettings.maxDistance))
        {
            return;
        }
        
        //lighting中除了设置光源外，还收阴影绘制。阴影绘制是一个独立的过程，需要在场景的主要渲染流程进行之前完成，以便于主渲染流程能够调用阴影渲染得到的阴影贴图
        buffer.BeginSample(SampleName);
        ExecuteBuffer();
        lighting.Setup(context, cullingResults, shadowSettings, useLightPerObject);//应该在调用CameraRenderer.SetUp之前渲染阴影贴图
        postFXStack.Setup(context, camera, postFXSettings, useHDR, colorLUTResolution,
            cameraSettings.finalBlendMode);
        buffer.EndSample(SampleName);

        Setup(); //在渲染命令之前设置一些准备信息，例如摄像机的透视信息，摄像机的未知信息等->否则渲染出的SkyBox无法随着视角改变

        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing, useLightPerObject);
        DrawUnsupportedShaders();
        DrawGizmosBeforeFX();
        if (postFXStack.IsActive)
        {
            postFXStack.Render(frameBufferId);
        }
        DrawGizmosAfterFX();

        Cleanup();
        //lighting.Cleanup();//清除光照相关的RT
        //在次这些指令只是存在buffer上，不会被提交。需要通过执行Submit来提交到队列
        Submit();


        // context.SetupCameraProperties(this.camera);
        // buffer.ClearRenderTarget(true, true, Color.clear);
        // buffer.BeginSample(bufferName);
        // context.ExecuteCommandBuffer(buffer);
        // buffer.Clear();
        //
        // context.DrawSkybox(camera);
        //
        // buffer.EndSample(bufferName);
        // context.ExecuteCommandBuffer(buffer);
        // buffer.Clear();
        // context.Submit();
    }

    void Setup()
    {
        context.SetupCameraProperties(this.camera);
        CameraClearFlags flags = camera.clearFlags;

        //用于处理PostFX的RT，如果开启PostFX则这张RT会作为最终的RenderTarget
        if (postFXStack.IsActive)
        {
            if (flags > CameraClearFlags.Color)
            {
                //为了避免用于处理PostFX的RT上有随机的数据，需要确保清除depth和color
                //但是这样的处理会使得当启用后处理时，无法实现一个相机在另一个相机已渲染的结果上进行进一步渲染（例如小地图的绘制）
                flags = CameraClearFlags.Color;
            }
            //HDR渲染只在开启后处理时使用，因为我们能改变用于后处理输入的RT的格式，但是不能改变最终真正的frameBuffer的格式
            buffer.GetTemporaryRT(
                frameBufferId, camera.pixelWidth, camera.pixelHeight,
                32, FilterMode.Bilinear, useHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);
            buffer.SetRenderTarget(
                frameBufferId,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        }
        
        buffer.ClearRenderTarget(true, true, Color.clear);
        buffer.BeginSample(SampleName);
        ExecuteBuffer();
    }

    void Cleanup()
    {
        lighting.Cleanup();
        if (postFXStack.IsActive)
        {
            buffer.ReleaseTemporaryRT(frameBufferId);
        }
    }

    void Submit()
    {
        buffer.EndSample(SampleName);
        ExecuteBuffer();

        context.Submit();
    }

    void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    bool Cull(float maxShadowDistance)
    {
        if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            p.shadowDistance = maxShadowDistance;
            p.shadowDistance = Mathf.Min(maxShadowDistance, camera.farClipPlane);
            cullingResults = context.Cull(ref p);
            return true;
        }

        return false;
    }

    void DrawVisibleGeometry(bool useDynamicBatching, bool useGPUInstancing, bool useLightsPerObject)
    {
        //Per-Object Light Data
        PerObjectData lightsPerObjectFlags =
            useLightsPerObject ? PerObjectData.LightData | PerObjectData.LightIndices : PerObjectData.None;
            
        //SortingSettings描述渲染时对物体进行排序的方法
        //通常通过设置类成员criteria来进行排序的设置，常用的是SortingCriteria.CommonOpaque和SortingCriteria.CommonTransparent，
        //其余见文档 -- https://docs.unity3d.com/ScriptReference/Rendering.SortingCriteria.html
        var sortingSetings = new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque };
        //DrawingSettings描述如何对可见的物体排序 (sortingSettings) 、以及使用哪个shader (shaderPassName)
        var drawingSettings = new DrawingSettings(unlitShaderTagId, sortingSetings)
        {
            enableDynamicBatching = useDynamicBatching,
            enableInstancing = useGPUInstancing,
            perObjectData = 
                PerObjectData.ReflectionProbes | //反射探针
                PerObjectData.Lightmaps | PerObjectData.ShadowMask | //告诉管线使用lightmap(/ShadowMask),用于获取并传输lightmap(/ShadowMask)的UV到shader中
                PerObjectData.LightProbe |  PerObjectData.OcclusionProbe | //Light(/Occlusion) Probe的object
                PerObjectData.LightProbeProxyVolume | //Light Probe Proxy Volume的object
                PerObjectData.OcclusionProbeProxyVolume |
                lightsPerObjectFlags                                                              
        };
        //添加一个新的光照模型LightMode
        drawingSettings.SetShaderPassName(1, litShaderTagId);
        //FilteringSettings描述如何过滤物体，把对象基于设置分为不同的集合去进行渲染；使用默认构造函数则表明不会进行任何的过滤
        //常用为根据Queue和LayerMask进行分组：Queue有RenderQueue.all/opaque/transparent
        var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
        context.DrawRenderers(cullingResults, ref drawingSettings,
            ref filteringSettings); //DrawRenderers进行实际的绘制，但是需要传入两个设置和之前得到的裁剪后的数据

        context.DrawSkybox(camera);

        sortingSetings.criteria = SortingCriteria.CommonTransparent;
        drawingSettings.sortingSettings = sortingSetings;
        filteringSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
    }
}