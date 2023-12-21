using UnityEditor;
using UnityEngine;

partial class PostFXStack
{
    //用于处理特定Scene窗口不开启后处理的情况，可由Scene的下拉菜单控制
    partial void ApplySceneViewState();
    
#if UNITY_EDITOR

    partial void ApplySceneViewState()
    {
        if (camera.cameraType == CameraType.SceneView &&
            !SceneView.currentDrawingSceneView.sceneViewState.showImageEffects)
        {
            settings = null;
        }
    }
    
#endif
}