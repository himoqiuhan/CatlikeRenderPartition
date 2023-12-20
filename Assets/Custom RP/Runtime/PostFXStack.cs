using UnityEngine;
using UnityEngine.Rendering;

public class PostFXStack
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

    //PostFX是否启用，由是否有PostFXSettings来判断
    public bool IsActive => settings != null;

    public void Setup(ScriptableRenderContext context, Camera camera, PostFXSettings settings)
    {
        this.context = context;
        this.camera = camera;
        this.settings = settings;
    }

    public void Render(int sourceId)
    {
        //运用效果的原理是借助shader绘制一个包含整个图像的三角形
        //通过Blit函数来执行，此处以sourceId和CameraTarget作为参数，传入图像源与渲染的目标
        buffer.Blit(sourceId, BuiltinRenderTextureType.CameraTarget);
        //然后通过Context.ExecuteCommandBuffer和Clear来执行并清空Buffer
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }
}