using System;
using UnityEngine;

[DisallowMultipleComponent]//如果在一个类前添加了这个属性，那么这个类的实例不能被添加到同一个GameObject上多次
public class PerObjectMaterialProperties : MonoBehaviour
{
    private static int baseColorId = Shader.PropertyToID("_BaseColor");
    private static int cutoffId = Shader.PropertyToID("_Cutoff");
    private static int metallicId = Shader.PropertyToID("_Metallic");
    private static int smoothnessId = Shader.PropertyToID("_Smoothness");

    [SerializeField] public Color baseColor = Color.white;
    [SerializeField, Range(0f, 1f)] public float cutoff = 0.5f;
    [SerializeField, Range(0f, 1f)] public float metallic = 0.0f;
    [SerializeField, Range(0f, 1f)] public float smoothness = 0.5f;

    //实现逐对象的材质属性需要通过MaterialPropertyBlock对象来完成，因为这个对象我们只需要一个，且可以复用，所以声明为一个static变量
    private static MaterialPropertyBlock block;

    private void Awake()
    {
        //OnValidate在Build的时候不会被调用，所以现在Awake中调用OnValid
        OnValidate();
    }

    private void OnValidate()
    {
        if (block == null)
        {
            block = new MaterialPropertyBlock();
        }
        block.SetColor(baseColorId, baseColor);
        block.SetFloat(cutoffId, cutoff);
        block.SetFloat(metallicId, metallic);
        block.SetFloat(smoothnessId, smoothness);
        GetComponent<Renderer>().SetPropertyBlock(block);
    }
}