using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using LightType = UnityEngine.LightType;


public partial class CustomRenderPipeline
{
    partial void InitializeForEditor();
    
    //在Editor内提供一个委托，让函数在调用Unity的lightmapping前执行
#if UNITY_EDITOR
    
    //转换Light类型数据为LightDataGI类型数据，委托类型是Lightmapping.RequestLightsDelegate
    //因为我们在其他地方不会再用到这个委托，所以使用Lambda表达式书写逻辑
    private static Lightmapping.RequestLightsDelegate lightsDelegate =
        (Light[] lights, NativeArray<LightDataGI> output) =>
        {
            //我们需要为每一个光源配置LightDataGI结构体用于输出，并且需要对不同类型的光源进行不同处理
            var lightData = new LightDataGI();
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                switch (light.type)
                {
                    //通过LightmapperUtils.Extract函数将light中的信息对应光源类型进行提取
                    case LightType.Directional:
                        var directionalLight = new DirectionalLight();
                        LightmapperUtils.Extract(light, ref directionalLight);
                        lightData.Init(ref directionalLight);
                        break;
                    case LightType.Point:
                        var pointLight = new PointLight();
                        LightmapperUtils.Extract(light, ref pointLight);
                        lightData.Init(ref pointLight);
                        break;
                    case LightType.Spot:
                        var spotLight = new SpotLight();
                        LightmapperUtils.Extract(light, ref spotLight);
                        //Unity 2022中还可以对innerConeAngle和angularFalloff进行设置
                        //spotLight.innerConeAngle = light.innerSpotAngle * Mathf.Deg2Rad;
                        //spotLight.angularFalloff = AngularFalloffType.AnalyticAndInnerAngle;
                        lightData.Init(ref spotLight);
                        break;
                    case LightType.Area:
                        var rectangleLight = new RectangleLight();
                        LightmapperUtils.Extract(light, ref rectangleLight);
                        //因为我们没提供对实时面光源的支持，所以将其LightMode设置为Baked
                        rectangleLight.mode = LightMode.Baked;
                        lightData.Init(ref rectangleLight);
                        break;
                    default:
                        lightData.InitNoBake(light.GetInstanceID());
                        break;
                }

                //设置光源的falloff类型，这一段是特殊于使用Unity自带RP中的关键
                lightData.falloff = FalloffType.InverseSquared;
                
                output[i] = lightData;
            }
        };
    
    partial void InitializeForEditor()
    {
        Lightmapping.SetDelegate(lightsDelegate);
        // Debug.Log("Reset Light Delegate");
    }

    //在配置我们的管线时，调用Dispose来清除和设置委托：先调用基础实现，再执行ResetDelegate
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Lightmapping.ResetDelegate();
        // Debug.Log("Reset Light Delegate From Dispose");
    }
    
#endif
}