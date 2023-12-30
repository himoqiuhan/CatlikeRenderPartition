using System;
using UnityEngine.Rendering;

//Camera Settings中控制启用后处理时，对不同相机的Blend模式的处理
//我们需要将最上层的Camera设置为One-OneMinusSrcAlpha的Blend，同时为了避免最底层的Camera不会与RT Initial后的结果混合，我们需要设置不同相机的混合模式
//（除非编辑器提供了一个Cleared Target，否则Initial的结果可能是random或者前帧的计算结果 -- 所以最底部的Camera需要设置为One-Zero）
//默认设置为One-Zero
[Serializable]
public class CameraSettings
{
    [Serializable]
    public struct FinalBlendMode
    {
        public BlendMode source, destination;
    }

    public FinalBlendMode finalBlendMode = new FinalBlendMode
    {
        source = BlendMode.One,
        destination = BlendMode.Zero
    };
}
