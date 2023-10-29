using System.Drawing.Drawing2D;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.Rendering;

public class CustomShaderGUI : ShaderGUI
{
    private MaterialEditor editor; //负责显示和编辑Material的Editor Object
    private Object[] materials; //通过MaterialEditor.target获取的Editor的属性，多重material的原因是使用同一个shader的材质可以同时进行编辑
    private MaterialProperty[] properties; //存放可供修改的属性
    private bool showPresets;

    enum ShadowMode
    {
        On, Clip, Dither, Off
    }


    //用于检查对应属性是否存在
    bool HasProperty(string name) =>
        FindProperty(name, properties, false) != null;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties); //执行基类的OnGUI函数，显示基础的属性
        editor = materialEditor;
        materials = materialEditor.targets;
        this.properties = properties;

        //制作FoldOut折叠显示Preset按钮
        EditorGUILayout.Space();
        showPresets = EditorGUILayout.Foldout(showPresets, "Presets", true);
        if (showPresets)
        {
            //在OnGUI的最后执行各个预设函数
            OpaquePreset();
            ClipPreset();
            FadePreset();
            TransparentPreset();
        }
    }

    //设置属性值
    bool SetProperty(string name, float value)
    {
        //提供对不存在属性的适配性，提高该GUI类的泛用性
        MaterialProperty
            property = FindProperty(name, properties, false); //加入最后一个false参数，表明不对属性进行强制查找，如果没找到返回一个空的property
        if (property != null)
        {
            property.floatValue = value;
            return true;
        }

        return false;
        // FindProperty(name, properties).floatValue = value;
        //FindProperty在properties中找到对应名称的属性，并返回MaterialProperty
        //通过MaterialProperty.floatValue对其数值进行设置
    }

    //设置Keyword
    void SetKeyword(string keyword, bool enable)
    {
        if (enable)
        {
            foreach (Material m in materials)
            {
                m.EnableKeyword(keyword);
            }
        }
        else
        {
            foreach (Material m in materials)
            {
                m.DisableKeyword(keyword);
            }
        }
    }

    //用于设置Toggle（Property和Keyword的结合设置）
    void SetProperty(string name, string keyword, bool value)
    {
        if (SetProperty(name, value ? 1.0f : 0.0f))
        {
            //只对能够设置属性值的属性（存在的Toggle）进行Keyword的设置
            SetKeyword(keyword, value);
        }
    }

    //对具体的关键词的设置函数，通过设置属性来调用函数
    private bool Clipping
    {
        set => SetProperty("_Clipping", "_CLIPPING", value);
    }

    bool PremultiplyAlpha
    {
        set => SetProperty("_PreMulAlpha", "_PREMULTIPLY_ALPHA", value);
    }

    private BlendMode SrcBlend
    {
        set => SetProperty("_SrcBlend", (float)value);
    }

    private BlendMode DstBlend
    {
        set => SetProperty("_DstBlend", (float)value);
    }

    private bool ZWrite
    {
        set => SetProperty("_ZWrite", value ? 1f : 0f);
    }

    RenderQueue RenderQueue
    {
        set
        {
            foreach (Material m in materials)
            {
                m.renderQueue = (int)value;
            }
        }
    }

    ShadowMode Shadows
    {
        set
        {
            if (SetProperty("_Shadows", (float)value))
            {
                SetKeyword("_SHADOWS_CLIP", value == ShadowMode.Clip);
                SetKeyword("_SHADOWS_DITHER", value == ShadowMode.Dither);
            }
        }
    }

    //借助上述的设置制作预设Buttons
    bool PresetButton(string name)
    {
        if (GUILayout.Button(name))
        {
            editor.RegisterPropertyChangeUndo(name); //
            return true;
        }

        return false;
    }

    //应用实际的预设按键，并进行对应预设的设置
    //预设的Opaque
    void OpaquePreset()
    {
        if (PresetButton("Opaque"))
        {
            Clipping = false;
            PremultiplyAlpha = false;
            SrcBlend = BlendMode.One;
            DstBlend = BlendMode.Zero;
            ZWrite = true;
            RenderQueue = RenderQueue.Geometry;
        }
    }

    //预设的Clip
    void ClipPreset()
    {
        if (PresetButton("Clip"))
        {
            Clipping = true;
            PremultiplyAlpha = false;
            SrcBlend = BlendMode.One;
            DstBlend = BlendMode.Zero;
            ZWrite = true;
            RenderQueue = RenderQueue.AlphaTest;
        }
    }

    //预设的Fade（线性减淡）
    void FadePreset()
    {
        if (PresetButton("Fade"))
        {
            Clipping = false;
            PremultiplyAlpha = false;
            SrcBlend = BlendMode.SrcAlpha;
            DstBlend = BlendMode.OneMinusSrcAlpha;
            ZWrite = false;
            RenderQueue = RenderQueue.Transparent;
        }
    }

    //检查PremultiplyAlpha属性是否存在，如果不存在则不显示该Button
    private bool HasPremultiplyAlpha => HasProperty("_PreMulAlpha");
    //预设的Transparent（半透明）
    void TransparentPreset()
    {
        if (HasPremultiplyAlpha && PresetButton("Transparent"))//&&连接的if判断执行时，如果第一个布尔（HasPremultiplyAlpha）为false，则不会再去执行PresetButton（从左向右执行）
        {
            Clipping = false;
            PremultiplyAlpha = false;
            SrcBlend = BlendMode.One;
            DstBlend = BlendMode.OneMinusSrcAlpha;
            ZWrite = false;
            RenderQueue = RenderQueue.Transparent;
        }
    }
    
    //统一对逐个材质进行阴影是否产生的设置
    void SetShadowCasterPass()
    {
        MaterialProperty shadows = FindProperty("_Shadows", properties, false);
        //如果当前物体的所有材质中没有_Shadows这个属性，或者不同材质的_Shadows属性不同，则不进行处理
        if (shadows == null || shadows.hasMixedValue)
        {
            return;
        }
        //对Shadow进行统一处理，如果关闭ShadowMode为Off，则停用ShadowCaster这个Pass，反之开启
        bool enable = shadows.floatValue < (float)ShadowMode.Off;
        foreach (Material m in materials)
        {
            m.SetShaderPassEnabled("ShadowCaster", enable);
        }
    }
}