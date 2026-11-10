Shader "ChronoWild/CollectibleOutline"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) _Width ("Width", Range(0,0.05)) = 0.012 }
    SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" } Pass {
        Cull Front ZWrite Off ZTest LEqual
        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4 _Color; float _Width;
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
        struct Varyings { float4 positionCS:SV_POSITION; };
        Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz + normalize(v.normalOS)*_Width); return o; }
        half4 frag(Varyings i):SV_Target { return _Color; }
        ENDHLSL
    } }
}
