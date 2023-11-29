using UnityEngine;
using UnityEngine.Rendering;
using Matrix4x4 = UnityEngine.Matrix4x4;
using Random = UnityEngine.Random;

public class MeshBall : MonoBehaviour
{
    private static int 
        baseColorId = Shader.PropertyToID("_BaseColor"),
        metallicId = Shader.PropertyToID("_Metallic"),
        smoothnessId = Shader.PropertyToID("_Smoothness");
    [SerializeField] public Mesh mesh = default;
    [SerializeField] public Material material = default;
    [SerializeField] public LightProbeProxyVolume lightProbeVolume = null;

    private Matrix4x4[] matrices = new Matrix4x4[1023];
    private Vector4[] baseColors = new Vector4[1023];
    private float[] 
        metallic = new float[1023], 
        smoothness = new float[1023];

    private MaterialPropertyBlock block;

    private void Awake()
    {
        for (int i = 0; i < matrices.Length; i++)
        {
            matrices[i] = Matrix4x4.TRS(
                Random.insideUnitSphere * 10f, Quaternion.Euler(Random.value * 360f, Random.value * 360f, Random.value * 360f), Vector3.one * Random.Range(0.5f, 1.5f)
            );
            baseColors[i] = new Vector4(Random.value, Random.value, Random.value, Random.Range(0.5f, 1f));
            metallic[i] = Random.value < 0.25f ? 1f : 0f;
            smoothness[i] = Random.Range(0.05f, 0.95f);
        }
    }

    private void Update()
    {
        if (block == null)
        {
            block = new MaterialPropertyBlock();
            block.SetVectorArray(baseColorId, baseColors);
            block.SetFloatArray(metallicId, metallic);
            block.SetFloatArray(smoothnessId, smoothness);

            if (!lightProbeVolume)
            {
                var positions = new Vector3[1023];
                for (int i = 0; i < matrices.Length; i++)
                {
                    positions[i] = matrices[i].GetColumn(3);
                }

                var lightProbes = new SphericalHarmonicsL2[1023];//Light Probes必须由这个类型的数组提供，并且由以下函数计算
                var occlusionProbes = new Vector4[1023];
                LightProbes.CalculateInterpolatedLightAndOcclusionProbes(positions, lightProbes, occlusionProbes);//第三个参数用于控制occlusion
                block.CopySHCoefficientArraysFrom(lightProbes);
                block.CopyProbeOcclusionArrayFrom(occlusionProbes);
            }
        }
        Graphics.DrawMeshInstanced(mesh, 0, material, matrices, 1023, block,
            ShadowCastingMode.On, true, 0, null, 
            lightProbeVolume ? LightProbeUsage.UseProxyVolume : LightProbeUsage.CustomProvided, lightProbeVolume
            //此行前两个参数控制是否CastShadows；第三个控制渲染的Layer；第四个控制渲染到的摄像机，设置为null表示渲染到所有摄像机上；
            //第五个参数，如果存在lightProbeVolume（使用LPPV），则设置为UseProxyVolume；如果不适用LPPV，则设置为CustomProvided
            );
    }
}