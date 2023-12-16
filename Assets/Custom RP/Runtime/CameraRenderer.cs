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

    //Light
    private Lighting lighting = new Lighting();

    public void Render(ScriptableRenderContext context, Camera camera, 
        bool useDynamicBatching, bool useGPUInstancing, bool useLightPerObject,
        ShadowSettings shadowSettings)
    {
        this.context = context;
        this.camera = camera;

        PrepareBuffer();
        PrepareForSceneWindow();

        if (!Cull(shadowSettings.maxDistance))
        {
            return;
        }
        
        //lighting中除了设置光源外，还收阴影绘制。阴影绘制是一个独立的过程，需要在场景的主要渲染流程进行之前完成，以便于主渲染流程能够调用阴影渲染得到的阴影贴图
        buffer.BeginSample(SampleName);
        ExecuteBuffer();
        lighting.Setup(this.context, cullingResults, shadowSettings, useLightPerObject);//应该在调用CameraRenderer.SetUp之前渲染阴影贴图
        buffer.EndSample(SampleName);

        Setup(); //在渲染命令之前设置一些准备信息，例如摄像机的透视信息，摄像机的未知信息等->否则渲染出的SkyBox无法随着视角改变

        DrawVisibleGeometry(useDynamicBatching, useGPUInstancing, useLightPerObject);
        DrawUnsupportedShaders();
        DrawGizmos();

        lighting.CleanUp();//清楚光照相关的RT
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
        buffer.ClearRenderTarget(true, true, Color.clear);
        buffer.BeginSample(SampleName);
        ExecuteBuffer();
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