Shader "ChronoWild/UIChromaKey"
{
    Properties { [PerRendererData]_MainTex("Texture",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _KeyColor("Key",Color)=(0,1,0,1) _Tolerance("Tolerance",Range(0,1))=0.25 _Softness("Softness",Range(0.001,0.5))=0.08 _Halo("Halo",Range(0,1))=0.6 _KeyRegion("Key Region",Vector)=(0,0,1,1) _StencilComp("Stencil Comparison",Float)=8 _Stencil("Stencil ID",Float)=0 _StencilOp("Stencil Operation",Float)=0 _StencilWriteMask("Stencil Write Mask",Float)=255 _StencilReadMask("Stencil Read Mask",Float)=255 _ColorMask("Color Mask",Float)=15 }
    SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" } Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] } Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
        Pass { HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float4 _Color,_KeyColor,_ClipRect; float _Tolerance,_Softness,_Halo; float4 _KeyRegion;
        struct A { float4 pos:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; }; struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float2 world:TEXCOORD1; };
        V vert(A v) { V o; o.world=v.pos.xy; o.pos=TransformObjectToHClip(v.pos.xyz); o.uv=v.uv; o.color=v.color*_Color; return o; }
        half4 frag(V i):SV_Target { half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*i.color; float d=distance(c.rgb,_KeyColor.rgb); float keep=smoothstep(_Tolerance,_Tolerance+_Softness,d); float inside=step(_KeyRegion.x,i.uv.x)*step(i.uv.x,_KeyRegion.z)*step(_KeyRegion.y,i.uv.y)*step(i.uv.y,_KeyRegion.w); keep=lerp(1,keep,inside); c.rgb=lerp(c.rgb,min(c.rgb,float3(c.r,c.g*(1-_Halo),c.b)),1-keep); c.a*=keep;
        #ifdef UNITY_UI_CLIP_RECT
        c.a*=step(_ClipRect.x,i.world.x)*step(i.world.x,_ClipRect.z)*step(_ClipRect.y,i.world.y)*step(i.world.y,_ClipRect.w);
        #endif
        return c; }
        ENDHLSL } }
}
