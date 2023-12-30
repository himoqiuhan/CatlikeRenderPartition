using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(Camera))]
//The RequireComponent attribute automatically adds required components as dependencies.
//当你将这个子类作为组件添加到一个游戏对象时，Unity会自动检查这个游戏对象是否已经有一个Camera组件
//如果没有，Unity会自动为这个游戏对象添加一个Camera组件
public class CustomRenderPipelineCamera : MonoBehaviour
{
    [SerializeField] private CameraSettings settings = default;

    public CameraSettings Settings => settings ?? (settings = new CameraSettings());
}
