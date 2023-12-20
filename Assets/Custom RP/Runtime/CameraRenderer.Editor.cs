using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public partial class CameraRenderer
//此处使用Partial Class来区分开仅在Editor中起作用的报错部分
{
    //因为在Render函数中调用了DrawUnsupportedShaders，在编辑器内使用时因为就是在UNITY_EDITOR的宏定义之下执行的，所以也不会报错。
    //但是在Build时就会报错，为了避免Build时错误，需要给Editor Only的函数额外添加一个空的声明，此时就需要用到partial
    //partial同时也可用在函数的声明上，在build时，编译器会自动断开那些函数的执行：被partial标识的、只有声明没有完整实现的函数
    partial void DrawUnsupportedShaders();
    partial void PrepareForSceneWindow();
    partial void DrawGizmos();
    partial void DrawGizmosBeforeFX();
    partial void DrawGizmosAfterFX();

    partial void PrepareBuffer();
    
    #if UNITY_EDITOR

    private string SampleName { get; set; }

    private static ShaderTagId[] legacyShaderTagIds =
    {
        new ShaderTagId("Always"),
        new ShaderTagId("ForwardBase"),
        new ShaderTagId("PrepassBase"),
        new ShaderTagId("Vertex"),
        new ShaderTagId("VertexLMRGBM"),
        new ShaderTagId("VertexLM"),
        new ShaderTagId("SRPDefaultUnlit")
    };

    //Additional Settings
    private static Material errorMaterial;

    partial void DrawGizmos()//用于渲染线框
    {
        if (Handles.ShouldRenderGizmos())
        {
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
    }

    partial void PrepareForSceneWindow()//用于让UI作为几何体渲染到Scene中
    {
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
    }

    partial void PrepareBuffer()
    {
        Profiler.BeginSample("Editor Only:");
        buffer.name = SampleName = camera.name;
        Profiler.EndSample();
    }

    partial void DrawUnsupportedShaders()
    {
        if (errorMaterial == null)
        {
            errorMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
        }
        var drawingSettings = new DrawingSettings(legacyShaderTagIds[0], new SortingSettings(camera))
        {
            overrideMaterial = errorMaterial //用bug材质override渲染这些shader的材质
        };
        for (int i = 1; i < legacyShaderTagIds.Length; i++)
        {
            drawingSettings.SetShaderPassName(i, legacyShaderTagIds[i]);//i不要超过16,DrawingSettings中的ShaderPassName数组长度为16
        }
        var filteringSettings = FilteringSettings.defaultValue;
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
    }
    
    
    partial void DrawGizmosBeforeFX()
    {
        if (Handles.ShouldRenderGizmos())
        {
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
        }
    }

    partial void DrawGizmosAfterFX()
    {
        if (Handles.ShouldRenderGizmos())
        {
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
    }
    
    #else
    const string SampleName = bufferName;
#endif
}
