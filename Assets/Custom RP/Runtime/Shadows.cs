using System.Numerics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Vector4 = UnityEngine.Vector4;

public class Shadows
{
    private const string bufferName = "Shadows";

    private CommandBuffer buffer = new CommandBuffer
    {
        name = bufferName
    };

    private ScriptableRenderContext context;
    private CullingResults cullingResults;
    private ShadowSettings settings;

    private const int MaxShadowedDirectionalLightCount = 4, MaxShadowedOtherLightCount = 16;
    private const int MaxCascades = 4;

    private Vector4 atlasSizes;

    struct ShadowedDirectionalLight
    {
        public int visibleLightIndex;
        public float slopeScaleBias;
        public float nearPlaneOffset;
    }
    
    struct ShadowedOtherLight
    {
        public int visibleLightIndex;
        public float slopeScaleBias;
        public float normalBias;
        public bool isPoint;
    }
    
    private int ShadowedDirectionalLightCount, ShadowedOtherLightCount;

    private ShadowedDirectionalLight[] ShadowedDirectionalLights =
        new ShadowedDirectionalLight[MaxShadowedDirectionalLightCount];

    private ShadowedOtherLight[] shadowedOtherLights =
        new ShadowedOtherLight[MaxShadowedOtherLightCount];

    private static int
        dirShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
        dirShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices"),
        otherShadowAtlasId = Shader.PropertyToID("_OtherShadowAtlas"),
        otherShadowMatricesId = Shader.PropertyToID("_OtherShadowMatrices"),
        otherShadowTilesId = Shader.PropertyToID("_OtherShadowTiles"),
        cascadeCountId = Shader.PropertyToID("_CascadeCount"), //需要把Cascade的数量和Culling Sphere传递给GPU
        cascadeCullingSphereId = Shader.PropertyToID("_CascadeCullingSpheres"),
        cascadeDataId = Shader.PropertyToID("_CascadeData"),
        shadowAtlasSizeId = Shader.PropertyToID("_ShadowAtlasSize"), //shadow atlas传入的大小，用于进行软阴影的采样（PCF Filter）
        shadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade"), //最后一个cascade culling的球体会略微超出我们定义的最大阴影，
        //会带来那个片元通过了culling sphere的检测去进行贴图采样，但是最终计算出来的ShadowTile采样点超出应有的范围，
        //意味着可能会采样到其他cascade shadow tile部分上（个人的理解），所以我们需要加一步限制最后一层cascade阴影的最大阴影距离限制。
        //使用_ShadowDistanceFade去控制阴影的最大距离和阴影衰减
        shadowPancakingID = Shader.PropertyToID("_ShadowPancaking");//控制ShadowPancaking是否开启，对于Other Light需要关闭Pancaking
        //因为Other Light绘制Shadowmap时是透视投影，clamping近平面顶点时会扰乱阴影的表现效果

        private static Vector4[]
            cascadeCullingSpheres = new Vector4[MaxCascades], //Culling Sphere实际的存储方式是XYZ（位置）W（半径）
            cascadeData = new Vector4[MaxCascades],
            otherShadowTiles = new Vector4[MaxShadowedOtherLightCount];

    private static Matrix4x4[]
        dirShadowMatrices = new Matrix4x4[MaxShadowedDirectionalLightCount * MaxCascades],
        otherShadowMatrices = new Matrix4x4[MaxShadowedOtherLightCount];

    private static string[] directionalFilterKeywords =
    {
        "_DIRECTIONAL_PCF3",
        "_DIRECTIONAL_PCF5",
        "_DIRECTIONAL_PCF7"
    };

    private static string[] otherFilterKeywords =
    {
        "_OTHER_PCF3",
        "_OTHER_PCF5",
        "_OTHER_PCF7"
    };

    private static string[] cascadeBlendKeywords =
    {
        "_CASCADE_BLEND_SOFT",
        "_CASCADE_BLEND_DITHER"
    };

    static string[] shadowMaskKeywords =
    {
        "_SHADOW_MASK_ALWAYS",
        "_SHADOW_MASK_DISTANCE"
    };

    private bool useShadowMask;

    public void Setup(ScriptableRenderContext context, CullingResults cullingResults, ShadowSettings shadowSettings)
    {
        this.context = context;
        this.cullingResults = cullingResults;
        this.settings = shadowSettings;
        ShadowedDirectionalLightCount = ShadowedOtherLightCount = 0;
        useShadowMask = false; //每一帧设置时默认将useShadowMask设置为false
    }

    void ExecuteBuffer()
    {
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
    }

    void SetOtherTileData(int index, Vector2 offset, float scale, float bias)
    {
        float border = atlasSizes.w * 0.5f;
        Vector4 data;
        //Data.xy存储当前Tile Space的边缘
        data.x = offset.x * scale + border;
        data.y = offset.y * scale + border;
        //Data.z存储缩放信息
        data.z = scale - border - border;
        //Data.w存储bias信息
        data.w = bias;
        otherShadowTiles[index] = data;
    }

    public Vector4 ReserveDirectionalShadows(Light light, int visibleLightIndex)
    {
        //判断条件：
        //1.产生阴影的Directional Light数量限制
        //2.光源的阴影：shadows设置不为None、阴影强度大于零--光源本身的设置
        // XXX 3.光源不影响投射阴影的物体（被设置为这样，或是影响的物体距离超过了最大阴影距离，目前暂时只考虑了距离超出最大阴影距离的情况)--即光源对于实际的阴影渲染是否有效
        //替换为不影响物体时换用Baked Shadowmask
        if (ShadowedDirectionalLightCount < MaxShadowedDirectionalLightCount &&
            light.shadows != LightShadows.None && light.shadowStrength > 0f //&&
            //cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b)
           )
        {
            //maskChannel用于传递光源对应shadowmap所以用的通道
            float maskChannel = -1;
            //check是否有光源使用shadowmask,判断条件：
            //1.光源的Mode为Mixed
            //2.LightSetting中的Mixed Lighting--Lighting Mode为Shadowmask
            //每个光源的Light.backingOutput属性中均含有Baked Data，通过该属性来获取相关信息
            LightBakingOutput lightBaking = light.bakingOutput;
            if (lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
                lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask)
            {
                useShadowMask = true;
                //获取当前light的shadowmask所在通道
                maskChannel = lightBaking.occlusionMaskChannel;
            }

            //如果光源不影响投射阴影的物体，则直接返回light的shadowStrength
            if (!cullingResults.GetShadowCasterBounds(
                    visibleLightIndex, out Bounds b
                ))
            {
                //还是不太理解这里传入-的原因 ---- 进一步理解：如果光源不影响投射阴影的物体，传入负值在Shader中处理的结果是使用Baked Shadow Mask
                return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
            }

            ShadowedDirectionalLights[ShadowedDirectionalLightCount] = new ShadowedDirectionalLight
            {
                visibleLightIndex = visibleLightIndex,
                slopeScaleBias = light.shadowBias,
                nearPlaneOffset = light.shadowNearPlane
            };
            return new Vector4(light.shadowStrength,
                settings.directional.cascadeCount * ShadowedDirectionalLightCount++,
                light.shadowNormalBias,
                maskChannel);
        }

        return new Vector4(0f, 0f, 0f, -1f);
    }

    //用于处理Other Light's Shadow Mask，实际上只用处理Shadow强度和对应光源所用的Shadow Mask的通道
    public Vector4 ReserveOtherShadows(Light light, int visibleLightIndex)
    {
        // Debug.Log("Light Type Is: " + light.type);
        //对于不使用ShadowMask，以及阴影强度小于0的光源，我们直接返回光源阴影的默认信息（不产生阴影影响）
        if (light.shadows == LightShadows.None || light.shadowStrength <= 0f)
        {
            return new Vector4(0f, 0f, 0f, -1f);
        }

        float maskChannel = -1f;
        LightBakingOutput lightBaking = light.bakingOutput;
        if(
            lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
             lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask
            )
        {
            useShadowMask = true;
            maskChannel = lightBaking.occlusionMaskChannel;
        }
        
        //处理实时点光源阴影：一个点光源如果要生成实时阴影需要动用6个Tile来渲染一个Cubemap
        bool isPoint = light.type == LightType.Point;
        int newLightCount = ShadowedOtherLightCount + (isPoint ? 6 : 1);
        // Debug.Log("IS Point Light: " + isPoint);

        //如果visible list中产生实时阴影的other light数量超过了最大数量，或者光源出了裁剪空间，
        //则返回用于shadow mask的相关信息，以便其处理baked shadow mask
        //if (ShadowedOtherLightCount >= MaxShadowedOtherLightCount ||
        if (newLightCount >= MaxShadowedOtherLightCount ||
            !cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b))
        {
            return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
        }

        shadowedOtherLights[ShadowedOtherLightCount] = new ShadowedOtherLight
        {
            visibleLightIndex = visibleLightIndex,
            slopeScaleBias = light.shadowBias,
            normalBias = light.shadowNormalBias,
            isPoint = isPoint
        };

        Vector4 data = new Vector4(
            light.shadowStrength, ShadowedOtherLightCount, isPoint ? 1f : 0f,
            maskChannel
        );
        ShadowedOtherLightCount = newLightCount;
        return data;
    }

    public void Render()
    {
        if (ShadowedDirectionalLightCount > 0)
        {
            RenderDirectionalShadows();
        }
        else
        {
            //即使没有阴影投射，也需要创建一张默认的Shadowmap传递给GPU，原因有二：
            //1.只能清除存在的RT，我们在最后是统一调用的CleanUp，并且只在ShadowDirectionalLightCount>0时才创建RT，所以需要在else中也创建RT
            //2.不渲染shadowmap给dirShadowAtlasId的话，shader会获取一张默认的texture，但是其无法兼容shadow sampler -- 另一个解决方法：可以添加额外的shader keyword生成shader变体，以适用于采用阴影的代码
            //这里选择了创建一个模拟的Shadowmap来统一解决问题
            buffer.GetTemporaryRT(dirShadowAtlasId, 1, 1, 32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
            //将RenderTarget设置为我们创建的RT，并在渲染完成后进行Clear
            buffer.SetRenderTarget(dirShadowAtlasId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            buffer.ClearRenderTarget(true, false, Color.clear);
            ExecuteBuffer();
        }

        if (ShadowedOtherLightCount > 0)
        {
            RenderOtherShadows();
        }
        else
        {
            //如果没有Other Light使用阴影，我们需要一个dummy Texture来作为替代。
            //可以像上面directional Light那样处理，也可以直接用dirShadowAtlas来替代
            buffer.SetGlobalTexture(otherShadowAtlasId, dirShadowAtlasId);
        }
        
        // Debug.Log("OtherShadowedLightCount: " + ShadowedOtherLightCount);

        //在Render的最后设置Keywords，无论是否启用实时阴影都需要进行设置
        buffer.BeginSample(bufferName);
        //在QualitySetting中可以获取使用shadowmask的模式
        SetKeywords(shadowMaskKeywords,
            useShadowMask ? QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask ? 0 : 1 : -1);

        //因为阴影的采样统一使用Cascade的数据，尽管Other Light没有cascade阴影，但是使用的是Cascade0作为shadowmap
        //所以我们需要为Other Light指定Cascade0，并且为其指定distance fade value
        buffer.SetGlobalInt(cascadeCountId,
            ShadowedDirectionalLightCount > 0 ? settings.directional.cascadeCount : 0);
        float f = 1f - settings.directional.cascadeFade;
        buffer.SetGlobalVector(shadowDistanceFadeId,
            new Vector4(
                1f / settings.maxDistance, 1f / settings.distanceFade,
                1f / (1f - f * f)
            )
        );
        //统一传输AtlasSize的相关信息，xy是Directional Light的size信息，zw是Other Light的size信息
        buffer.SetGlobalVector(shadowAtlasSizeId, atlasSizes);

        buffer.EndSample(bufferName);
        ExecuteBuffer();
    }

    void RenderDirectionalShadows()
    {
        int atlasSize = (int)settings.directional.atlasSize;
        atlasSizes.x = atlasSize;
        atlasSizes.y = 1f / atlasSize;
        buffer.GetTemporaryRT(dirShadowAtlasId, atlasSize, atlasSize, 32, FilterMode.Bilinear,
            RenderTextureFormat.Shadowmap); //只用前三个参数，我们获得的是一张默认的ARGB贴图，但是因为我们创建的是ShadowMap，所以添加后三个参数：
        //第一个是depth buffer的精度，数值远大精度越高，这里使用32（通常有16、24、32），URP使用的是16
        //第二个是贴图的FilterMode，使用默认的Bilinear
        //第三个是RT的类型，将去精确地设置为Shadowmap（不过不同的平台可能有所差异）
        //将RenderTarget设置为我们创建的RT，并在渲染完成后进行Clear
        buffer.SetRenderTarget(dirShadowAtlasId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        //设置dirShadowAtlasId为下一次执行的RenderTarget，在当前Command Buffer被执行之后，原本的RenderTarget会自动恢复
        buffer.ClearRenderTarget(true, false, Color.clear); //清除当前的RenderTarget（也就是dirShadowAtlasId对应的RT）
        
        //设置Pancaking
        buffer.SetGlobalFloat(shadowPancakingID, 1f);
        
        buffer.BeginSample(bufferName);
        ExecuteBuffer();

        int tiles = ShadowedDirectionalLightCount * settings.directional.cascadeCount; //计算tiles的总数
        int split = tiles <= 1
            ? 1
            : tiles <= 4
                ? 2
                : 4; //最多只会支持到4个Directional Light的阴影投射，所以只分1/2的拆分（1个Shadow为1，其余2、3、4个shadow为2）
        //因为是分cascade shadow map级联阴影，一个灯最多有4个cascade，所以最多可能有4个split
        //split使用2的倍数的原因：因为Atlas Size的大小也是2的倍数，这样设置能够确保可以整除，避免产生采样错位的问题
        int tileSize = atlasSize / split; //分块，计算出每个tile的大小

        for (int i = 0; i < ShadowedDirectionalLightCount; i++)
        {
            RenderDirectionalShadows(i, split, tileSize);
        }

        //在渲染完阴影之后将相关信息发送到GPU
        //发送Cascade相关信息
        // buffer.SetGlobalInt(cascadeCountId, settings.directional.cascadeCount); -- 数据发送迁移到整体Shadow数据发送内，用于处理Other Lights Shadow
        buffer.SetGlobalVectorArray(cascadeCullingSphereId, cascadeCullingSpheres);
        //发送Cascade的数据
        buffer.SetGlobalVectorArray(cascadeDataId, cascadeData);
        //发送矩阵信息
        buffer.SetGlobalMatrixArray(dirShadowMatricesId, dirShadowMatrices);
        //发送最大阴影距离给GPU
        // buffer.SetGlobalFloat(shadowDistanceId, settings.maxDistance);
        //float f = 1f - settings.directional.cascadeFade;
        //buffer.SetGlobalVector(shadowDistanceFadeId, new Vector4(1f / settings.maxDistance, 1f / settings.distanceFade,
        //1f / (1f - f * f)));  -- 数据发送迁移到整体Shadow数据发送内，用于处理Other Lights Shadow
        //maxDistance和distanceFade分别作为shadow fading计算中的m项和f项，都是在分母上的。因为是常量，所以在数据传输时直接传入其倒数，则只需要进行一次取倒数
        //可以优化逐Fragment的Shadow Fading计算中的求倒数
        //第三个参数用于计算cascade shadow的衰减，为了保证球体的衰减率不变，f需要变为1 - （ 1 - f ^2 ）
        SetKeywords(directionalFilterKeywords, (int)settings.directional.filter - 1); //设置阴影的filter mode
        SetKeywords(cascadeBlendKeywords, (int)settings.directional.cascadeBlend - 1); //设置cascade阴影的blend模式
        //由Render函数进行Directional和Other Light的统一传递
        // buffer.SetGlobalVector(shadowAtlasSizeId, new Vector4(atlasSize, 1f / atlasSize)); //传入Shadow Atlas的大小
        buffer.EndSample(bufferName);
        ExecuteBuffer();
    }

    void RenderOtherShadows()
        //复制并更改RenderDirectionalShadows()函数
    {
        int atlasSize = (int)settings.other.atlasSize;
        atlasSizes.z = atlasSize;
        atlasSizes.w = 1f / atlasSize;
        buffer.GetTemporaryRT(otherShadowAtlasId, atlasSize, atlasSize, 32, FilterMode.Bilinear,
            RenderTextureFormat.Shadowmap); 
        buffer.SetRenderTarget(otherShadowAtlasId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        buffer.ClearRenderTarget(true, false, Color.clear);
        
        buffer.SetGlobalFloat(shadowPancakingID, 0f);

        buffer.BeginSample(bufferName);
        ExecuteBuffer();

        int tiles = ShadowedOtherLightCount;
        int split = tiles <= 1? 1 : tiles <= 4 ? 2 : 4; 
        int tileSize = atlasSize / split;

        for (int i = 0; i < ShadowedOtherLightCount;)
        {
            //分别处理i的递增值
            if (shadowedOtherLights[i].isPoint)
            {
                RenderPointShadows(i, split, tileSize);
                i += 6;
            }
            else
            {
                RenderSpotShadows(i, split, tileSize);
                i += 1;
            }
        }
        // Debug.Log("Shadowed Other Light Count: " + ShadowedOtherLightCount);
        
        buffer.SetGlobalMatrixArray(otherShadowMatricesId, otherShadowMatrices);
        buffer.SetGlobalVectorArray(otherShadowTilesId, otherShadowTiles);
        SetKeywords(otherFilterKeywords, (int)settings.other.filter - 1); //设置阴影的filter mode
        
        buffer.EndSample(bufferName);
        ExecuteBuffer();
    }

    void RenderDirectionalShadows(int index, int split, int tileSize)
    {
        ShadowedDirectionalLight light = ShadowedDirectionalLights[index];
        ShadowDrawingSettings shadowDrawSettings = new ShadowDrawingSettings(cullingResults, light.visibleLightIndex);
        int cascadeCount = settings.directional.cascadeCount;
        int tileOffset = index * cascadeCount;
        //存储4个Directional Light的4层Cascade Shadow Map的数组结构如下：
        // ( L0C0, L0C1, L0C2, L0C3
        //   L1C0, L1C1, L1C2, L1C3
        //   L2C0, L2C1, L2C2, L2C3
        //   L3C0, L3C1, L3C2, L3C3 )
        //用这个就很好理解计算ShadowMatrices函数中的i、cascadeCount参数，以及tileIndex所表示的行索引的作用
        Vector3 ratios = settings.directional.CascadeRatios;

        float tileScale = 1f / split;

        float cullingFactor =
            Mathf.Max(0f, 0.8f - settings.directional.cascadeFade); //使用0.8控制是为了减小对处于cascade交界处shadow caster的影响

        for (int i = 0; i < cascadeCount; i++)
        {
            //shadow map的思想是渲染光源看向场景的深度信息
            //但是对于Directional Light来说，是没有光源位置的。所以我们需要计算出与光源方向相匹配的V矩阵和P矩阵，并且获得一个能够包含“摄像机可见并且投射阴影的所有物体”的裁剪空间
            //可以通过内置函数来实现
            // cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
            //     light.visibleLightIndex, 0, 1, Vector3.zero, tileSize,
            //     0f, //第一个参数是可见光的index，后三个参数是控制shadow cascade的，下一个是贴图大小，第六个是shadow的近平面
            //     out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix, //然后是V、P矩阵
            //     out ShadowSplitData splitData); //最后一个是split data，包含shadow-casting的物体该如何被剔除的信息
            cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex,
                i, cascadeCount, ratios, tileSize,
                light.nearPlaneOffset,
                out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix,
                out ShadowSplitData splitData);

            if (index == 0) //因为所有光源的cascade是等效的（都是相对于同一个相机），所以只需要对第一个光源进行处理即可
            {
                // // cascadeCullingSpheres[i] = splitData.cullingSphere;//Culling Sphere的信息从splitData中获取
                // Vector4 cullingSphere = splitData.cullingSphere;
                // cullingSphere.w *= cullingSphere.w;
                // cascadeCullingSpheres[i] = cullingSphere;
                // //因为判断cascade等级是通过判断fragment是否在这个cascade求体内进行的，最后是将fragment到球心的距离与球的半径进行比较
                // //避免计算低效的平方根，优化为比较平方值；并且在CPU中完成对距离平方的计算
                SetCascadeData(i, splitData.cullingSphere, tileSize); //通过SetCascadeData函数进行统一的Cascade相关数据处理
            }

            splitData.shadowCascadeBlendCullingFactor =
                cullingFactor; //半径的乘数，用于控制cascade culling sphere的半径缩放。缩小culling sphere的大小，提高shadow caster
            //的渲染效率，在交界处用更高层级的cascade替代当前层级
            shadowDrawSettings.splitData = splitData; //将Split data传输给shadowSettings
            int tileIndex = tileOffset + i;
            // SetTileViewport(index, split, tileSize);
            dirShadowMatrices[tileIndex] =
                ConvertToAtlasMatrix(
                    projectionMatrix * viewMatrix,
                    SetTileViewport(tileIndex, split, tileSize),
                    tileScale); //获得从世界空间到光源裁剪空间的变化矩阵（矩阵相乘是左乘）,同时把矩阵转换为运用了Atlas多组Shadowmap的形态，并在内部调用SetTileViewport函数，既实现Tile的buffer设置，又获得了offset的返回值
            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            // buffer.SetGlobalDepthBias(0f, 3f); -- Depth Bias会造成Perter-Panning的Artifact；使用Slope Bias能获得还算不错的效果，但是不够直观
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias); //此处的Slope Bias是逐光源设置的，用于解决阴影采样溢出，导致不同物体间接缝初产生异样阴影的问题
            ExecuteBuffer();
            context.DrawShadows(ref shadowDrawSettings);
            // buffer.SetGlobalDepthBias(0f, 0f);
            buffer.SetGlobalDepthBias(0f, 0f);
        }
    }

    void RenderSpotShadows(int index, int split, int tileSize)
    {
        ShadowedOtherLight light = shadowedOtherLights[index];
        var shadowSettings = new ShadowDrawingSettings(
            cullingResults, light.visibleLightIndex
        );
        cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
            light.visibleLightIndex, out Matrix4x4 viewMatrix,
            out Matrix4x4 projectionMatrix, out ShadowSplitData splitData);
        shadowSettings.splitData = splitData;
        //处理Shadow Bias: 因为other light的shadowmap是透视投影获得的，运用到实际采样时使用的texelSize需要基于透视进行进一步三角函数处理
        float texelSize = 2f / (tileSize * projectionMatrix.m00);
        float filterSize = texelSize * ((float)settings.other.filter + 1f);
        float bias = light.normalBias * filterSize * 1.4142136f;
        Vector2 offset = SetTileViewport(index, split, tileSize);
        float tileScale = 1f / split;
        SetOtherTileData(index, offset, tileScale, bias);
        otherShadowMatrices[index] = ConvertToAtlasMatrix(
            projectionMatrix * viewMatrix, offset, tileScale);
        buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
        buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
        ExecuteBuffer();
        context.DrawShadows(ref shadowSettings);
        buffer.SetGlobalDepthBias(0f, 0f);
    }
    //与RenderSpotShadows有两点不同之处：
    //1.需要遍历6次，分别渲染cubemap的每个面
    //2.使用 ComputePointShadowMatricesAndCullingPrimitives 函数，多两个参数，CubemapFace Index和Fov bias
    void RenderPointShadows(int index, int split, int tileSize)
    {
        ShadowedOtherLight light = shadowedOtherLights[index];
        var shadowSettings = new ShadowDrawingSettings(
            cullingResults, light.visibleLightIndex
        );
        
        //因为Cubemap的FOV通常是90°，因此世界空间下的Tile Size为texelSize的两倍。可以将重复的计算移除循环
        float texelSize = 2f / tileSize;
        float filterSize = texelSize * ((float)settings.other.filter + 1f);
        float bias = light.normalBias * filterSize * 1.4142136f;
        float tileScale = 1f / split;
        
        //在渲染point light的阴影时，不同面片之间如果相差角度过大，会造成面之间部分的阴影不连续（例如90°的墙角）
        //Cubemap对于此的解决是在不同的Face之间做采样插值，但是这里我们只是使用了Cubemap的思想，而非真正意义上的使用Cubemap，对于每一个Fragment我们只是采样一个Tile
        //所以无法直接解决这个问题。因为没有采样衰减，Spot Light也会有同样的问题。
        //对于Point Light可以使用fovBias来处理，其原理是渲染shadowmap时适当增加world-space的tile size，使得我们永远不会采样到一个tile的边缘
        float fovBias = Mathf.Atan(1f + bias + filterSize) * Mathf.Rad2Deg * 2f - 90f;
        //这个处理方法并不完美，因为tilesize一旦改变，就需要fovBias进一步增加。不过差异在不使用large normal bias + small atlas size时不会太明显。
        //对于spot light可以使用同样的原理，但是因为spotlight的函数中没有fovBias的处理，需要自己改相应的数据，写自己的函数变体（但是原函数闭源）
        
        for (int i = 0; i < 6; i++)
        {
            cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex, (CubemapFace)i,  fovBias,
                out Matrix4x4 viewMatrix,
                out Matrix4x4 projectionMatrix, out ShadowSplitData splitData);
            //造成漏光的原因：Unity在渲染Point Light的Shadow map时，会将阴影上下反转进行绘制，使得三角形的组成顺序被反转
            //造成的结果是物体相对于光源的背面被渲染(Back-face shadow)，但通常情况渲染Shadowmap是渲染相对于光源的正面的深度（Front-face shadow）
            //Back-face shadow这样可以避免大多数的acne（因为渲染的是物体的背面，相当于在渲染shadowmap时就进行了bias），但是会造成物体与阴影的接触面漏光的问题
            //通过反转viewMatrix的第二行可以反转渲染（上下颠倒回来）（因为这一行的第一列是0，所以不需要反转）
            viewMatrix.m11 = -viewMatrix.m11;
            viewMatrix.m12 = -viewMatrix.m12;
            viewMatrix.m13 = -viewMatrix.m13;
            //然后再调整光源的Normal Bias来减少acne
            //不过当物体Mesh Renderer-Lighting-Cast Shadows被设置为Two Side时，不管是否反转，得到的结果都是一样的（因为在渲染shadowmap时物体的两个面都被渲染了）
            
            shadowSettings.splitData = splitData;
            int tileIndex = index + i;//获取当前CubemapFace在Shadow Tiles中的Index
            Vector2 offset = SetTileViewport(tileIndex, split, tileSize);
            SetOtherTileData(tileIndex, offset, tileScale, bias);
            otherShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                projectionMatrix * viewMatrix, offset, tileScale);
            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
            ExecuteBuffer();
            context.DrawShadows(ref shadowSettings);
            buffer.SetGlobalDepthBias(0f, 0f);
        }
        // Debug.Log("Render Point Shadows");
    }

    void SetCascadeData(int index, Vector4 cullingSphere, float tileSize)
    {
        float texelSize = 2f * cullingSphere.w / tileSize;
        float filterSize = texelSize * ((float)settings.directional.filter + 1f); //优化使用PCF制作软阴影后的深度采样问题

        cullingSphere.w -= filterSize; //避免在culling范围之外采样
        cullingSphere.w *= cullingSphere.w;

        texelSize *= 1.4142136f; //得到的TexelSize是平方后的值，在最坏的情况下我们需要沿着45度角进行偏移，所以需要乘上一个根号二
        cascadeCullingSpheres[index] = cullingSphere;
        // cascadeData[index].x = 1f / cullingSphere.w;//传入一个CullingSphere的半径的倒数，避免在shader中进行逐片元的除法计算
        cascadeData[index] =
            new Vector4(1f / cullingSphere.w, texelSize,
                filterSize * 1.4142136f); //所以最终的偏移值取决于每个tile的shadowAtlasMap的大小，以及当前cascade下culling sphere的半径大小
        //至于2f和根号二，我认为是一个权衡后取的数据；同时注意，这里的tileSize指的是每一个小分块的分辨率，而不是整个atlas的分辨率
    }

    Vector2 SetTileViewport(int index, int split, float tileSize)
    {
        Vector2 offset = new Vector2(index % split, index / split);
        buffer.SetViewport(new Rect(offset.x * tileSize, offset.y * tileSize, tileSize,
            tileSize)); //通过设置Viewport的分块，来实现不同光源的shadow map渲染到不同区块中
        return offset;
    }

    Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 m, Vector2 offset, float scale)
    {
        if (SystemInfo.usesReversedZBuffer) //适配不同图形API对深度的存储方法，（为了准确性，一般将近处存为1，远处存为0；但OpenGL是反过来的）
        {
            m.m20 = -m.m20;
            m.m21 = -m.m21;
            m.m22 = -m.m22;
            m.m23 = -m.m23;
        }

        //Clip Space下的深度信息是存储在-1~1之间的，需要缩放到0~1之间
        //并且需要基于Atlas进行调整
        //手动设置矩阵，避免因矩阵相乘而得到的一些0值和无必要的操作
        // float scale = 1f / split;
        m.m00 = (0.5f * (m.m00 + m.m30) + offset.x * m.m30) * scale;
        m.m01 = (0.5f * (m.m01 + m.m31) + offset.x * m.m31) * scale;
        m.m02 = (0.5f * (m.m02 + m.m32) + offset.x * m.m32) * scale;
        m.m03 = (0.5f * (m.m03 + m.m33) + offset.x * m.m33) * scale;
        m.m10 = (0.5f * (m.m10 + m.m30) + offset.y * m.m30) * scale;
        m.m11 = (0.5f * (m.m11 + m.m31) + offset.y * m.m31) * scale;
        m.m12 = (0.5f * (m.m12 + m.m32) + offset.y * m.m32) * scale;
        m.m13 = (0.5f * (m.m13 + m.m33) + offset.y * m.m33) * scale;
        m.m20 = 0.5f * (m.m20 + m.m30);
        m.m21 = 0.5f * (m.m21 + m.m31);
        m.m22 = 0.5f * (m.m22 + m.m32);
        m.m23 = 0.5f * (m.m23 + m.m33);
        return m;
    }

    void SetKeywords(string[] keywords, int enableIndex)
    {
        // int enableIndex = (int)settings.directional.filter - 1;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (i == enableIndex)
            {
                buffer.EnableShaderKeyword(keywords[i]);
            }
            else
            {
                buffer.DisableShaderKeyword(keywords[i]);
            }
        }
    }

    public void CleanUp()
        //回收创建的RT的内存
    {
        buffer.ReleaseTemporaryRT(dirShadowAtlasId);
        if (ShadowedOtherLightCount > 0)
        {
            buffer.ReleaseTemporaryRT(otherShadowAtlasId);
        };
        ExecuteBuffer();
    }
}