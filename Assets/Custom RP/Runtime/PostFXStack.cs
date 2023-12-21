using UnityEngine;
using UnityEngine.Rendering;

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

    private int fxSourceId = Shader.PropertyToID("_PostFXSource");

    private const int maxBloomPyramidLevels = 16;

    private int bloomPyramidId;
    
    //用于添加Pass的Enum
    enum Pass
    {
        Copy
    }

    //PostFX是否启用，由是否有PostFXSettings来判断
    public bool IsActive => settings != null;

    public PostFXStack()
    {
        //PropertyToID会按照顺序执行，所以我们只需要获取第一个Pyramid的ID就可以获取到后续的ID
        bloomPyramidId = Shader.PropertyToID("_BloomPyramid0");
        for (int i = 1; i < maxBloomPyramidLevels; i++)
        {
            Shader.PropertyToID("_BloomPyramid" + i);
        }
    }
    
    public void Setup(ScriptableRenderContext context, Camera camera, PostFXSettings settings)
    {
        this.context = context;
        this.camera = camera;
        this.settings = 
            camera.cameraType <= CameraType.SceneView ? settings : null;
        ApplySceneViewState();
    }

    public void Render(int sourceId)
    {
        //运用效果的原理是借助shader绘制一个包含整个图像的三角形
        //通过Blit函数来执行，此处以sourceId和CameraTarget作为参数，传入图像源与渲染的目标
        //buffer.Blit(sourceId, BuiltinRenderTextureType.CameraTarget);
        //Draw(sourceId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);
        DoBloom(sourceId);
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
    void DoBloom(int sourceId)
    {
        buffer.BeginSample("Bloom");
        int width = camera.pixelWidth / 2, height = camera.pixelHeight / 2;
        RenderTextureFormat format = RenderTextureFormat.Default;
        int fromId = sourceId, toId = bloomPyramidId;

        int i;
        for (i = 0; i < maxBloomPyramidLevels; i++)
        {
            if (height < 1 || width < 1)
            {
                break;
            }
            buffer.GetTemporaryRT(toId, width, height, 0, FilterMode.Bilinear, format);
            Draw(fromId, toId, Pass.Copy);
            fromId = toId;
            toId += 1;
            width /= 2;
            height /= 2;
        }
        
        Draw(fromId, BuiltinRenderTextureType.CameraTarget, Pass.Copy);

        for (i -= 1; i >= 0; i--)
        {
            buffer.ReleaseTemporaryRT(bloomPyramidId + i);
        }
        
        buffer.EndSample("Bloom");
    }
    
}