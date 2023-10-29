Shader "CustomRP/Always"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color("Color", Color) = (1.0, 1.0, 1.0, 1.0)
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        CBUFFER_END

        struct Attributes
        {
            float4 posOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 posCS : SV_POSITION;
        };

        Varyings BaseVertexProgram(Attributes vertexInput)
        {
            Varyings vertexOutput;
            vertexOutput.posCS = TransformObjectToHClip(vertexInput.posOS.xyz);
            return vertexOutput;
        }

        half4 BaseFragmentProgram(Varyings i) : SV_Target
        {
            return _Color;
        }
        ENDHLSL
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Tags{"LightMode"="Always"}
            HLSLPROGRAM
            #pragma vertex BaseVertexProgram
            #pragma fragment BaseFragmentProgram
            ENDHLSL
        }
    }
}