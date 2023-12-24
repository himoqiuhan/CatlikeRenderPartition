using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Render Pipeline")]//使这段代码可以作为模板在菜单中创建
public class CustomRenderPipelineAsset : RenderPipelineAsset
//RP Asset的作用是获取用以控制rendering的Pipeline Object Instance，Asset本身只适用于存储一些设置
{
    [SerializeField] private bool allowHDR = true;
    [SerializeField] private bool 
        useDynamicBatching = true, 
        useGPUInstancing = true, 
        useSRPBatcher = true, 
        useLightsPerObject = true;
    
    [SerializeField] private ShadowSettings shadows = default;
    [SerializeField] private PostFXSettings postFXSettings = default;
    
    protected override RenderPipeline CreatePipeline()
    //用于获取Pipeline Object Instance，使用protected保护，意味着只有这个类及其派生才能调用这个函数
    {
        return new CustomRenderPipeline(
            allowHDR, useDynamicBatching, useGPUInstancing, useSRPBatcher, 
            useLightsPerObject, shadows, postFXSettings);
    }
}
