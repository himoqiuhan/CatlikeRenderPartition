using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[CreateAssetMenu(menuName = "Rendering/Custom Post FX Settings")]
public class PostFXSettings : ScriptableObject
{
    [SerializeField] private Shader shader = default;
    //只需要在有需求时创建这个mat，不需要序列化
    [System.NonSerialized] private Material material;
    public Material Material
    {
        get
        {
            if (material == null && shader != null)
            {
                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;
            }

            return material;
        }
        
    }
    
    //---------------Bloom---------------
    [System.Serializable]
    public struct BloomSettings
    {
        [Range(0f, 16f)] public int maxIterations;
        [Min(1f)] public int downscaleLimit;
        public bool bicubicUpsampling;
        [Min(0f)] public float threshold;
        [Range(0f, 1f)] public float thresholdKnee;
        [Min(0f)] public float intensity;
        public bool fadeFireflies;
        
        public enum Mode
        {
            Additive, Scattering
        }
        public Mode mode;
        [Range(0.05f, 0.95f)] public float scatter;
    }

    [SerializeField] private BloomSettings bloom = new BloomSettings
    {
        scatter = 0.7f
    };
    public BloomSettings Bloom => bloom;

        
    //---------------ToneMapping---------------
    [System.Serializable]
    public struct ToneMappingSettings
    {
        public enum Mode
        {
            None = -1, ACES, Neutral, Reinhard
        }
        public Mode mode;
    }
    
    [SerializeField] private ToneMappingSettings toneMapping = default;
    public ToneMappingSettings ToneMapping => toneMapping;
    
}