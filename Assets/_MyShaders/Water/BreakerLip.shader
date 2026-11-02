Shader "CHRONO WILD/Water/Breaker Lip"
{
 // Curling lip of a breaking wave. The strip lies along the break line (SurfBreakers); each
 // vertex knows its seabed depth, slope and shore direction, recomputes the CoastalOcean wave
 // phase and, during the short breaking window, bends into an arc thrown forward and down over
 // the face (a barrel). Outside that window it collapses to nothing.
 Properties
 {
  _SurfHeight("Offshore Wave Height (m)",Float)=1.5
  _SurfPeriod("Wave Period (s)",Float)=9
  _BreakerIndex("Breaker Index",Float)=0.85
  _SurfStartDepth("Shoaling Start Depth (m)",Float)=14
  _WaveDirection("Swell Direction (deg)",Float)=90
  _WaterColor("Lip Water Color",Color)=(0.05,0.42,0.36,1)
  _SSSColor("Light Through Lip",Color)=(0.1,0.7,0.48,1)
  _FoamColor("Foam Color",Color)=(0.95,0.96,0.95,1)
  _LipReach("Lip Reach",Range(0.5,2))=1.2
  _Opacity("Opacity",Range(0,1))=0.92
  _DebugPhase("Debug Phase (negative = live)",Float)=-1
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+5"}
  Pass
  {
   Name "BreakerLip" Tags {"LightMode"="UniversalForward"}
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vertex
   #pragma fragment Fragment
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Assets/_MyShaders/Environment/JungleAtmosphere.hlsl"

   CBUFFER_START(UnityPerMaterial)
   float _SurfHeight, _SurfPeriod, _BreakerIndex, _SurfStartDepth, _WaveDirection;
   half4 _WaterColor, _SSSColor, _FoamColor; half _LipReach, _Opacity; float _DebugPhase;
   CBUFFER_END

   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 shore:TEXCOORD1; float4 bed:TEXCOORD2; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float4 data:TEXCOORD2; };

   float Hash1(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
   float ValueNoise(float2 p)
   {
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(Hash1(i), Hash1(i + float2(1, 0)), f.x), lerp(Hash1(i + float2(0, 1)), Hash1(i + float2(1, 1)), f.x), f.y);
   }

   // Lip curve point for parameter v (0 face top .. 1 tip) at curl amount c.
   float3 LipPoint(float3 top, float3 shore, float R, float v, float c)
   {
    float theta = v * c * PI * 1.1;
    return top + shore * (R * sin(theta) * _LipReach) + float3(0, R * (cos(theta) - 1.0), 0);
   }

   Varyings Vertex(Attributes a)
   {
    Varyings o;
    float3 base = TransformObjectToWorld(a.positionOS.xyz);
    float depth = a.bed.x, slope = a.bed.y, along = a.bed.z;
    float3 shore = float3(a.shore.x, 0, a.shore.y);
    float travel = 2.0 * sqrt(depth) / (slope * 3.132);
    float sets = 0.62 + 0.38 * sin(TWO_PI * _Time.y / (_SurfPeriod * 7.0) + along * 0.012);
    float waveH = _SurfHeight * sets;
    float phase = _DebugPhase >= 0 ? _DebugPhase : frac((_Time.y + travel) / _SurfPeriod + along * 0.0025);
    float breakDepth = waveH / _BreakerIndex;
    // Only waves big enough to break at this line curl; smaller sets just roll through.
    float breaksHere = smoothstep(0.6, 0.95, breakDepth / max(depth, 0.01));
    float faceH = waveH * pow(_SurfStartDepth / max(depth, breakDepth), 0.25) * 0.55;
    // Curl window: the lip forms as the crest arrives (0.06), throws (0.14) and falls (0.24).
    float form = smoothstep(0.05, 0.13, phase);
    float fall = smoothstep(0.15, 0.25, phase);
    float c = form * (1.0 - fall * 0.35) * breaksHere;
    float alive = form * (1.0 - fall) * breaksHere;
    // Uneven along the beach: sections peel at slightly different moments and sizes.
    float waveIndex = floor((_Time.y + travel) / _SurfPeriod + along * 0.0025);
    float sectionNoise = ValueNoise(float2(a.uv.x * 0.07, waveIndex * 3.7));
    // Only some sections of each wave pitch a lip; the rest spill without curling.
    float gate = smoothstep(0.38, 0.62, sectionNoise);
    float section = 0.75 + 0.5 * sectionNoise;
    float R = faceH * 0.45 * section;
    alive *= gate; c *= gate;
    // The crest travels a little shoreward while it throws, and sinks as it falls.
    float3 top = base + shore * (phase - 0.08) * 3.0 + float3(0, faceH * (0.95 - fall * 0.8), 0);
    float v = a.uv.y;
    float3 p = LipPoint(top, shore, R, v, max(c, 0.001));
    // Collapse completely outside the window so nothing floats over the sea.
    p = lerp(base + float3(0, -0.5, 0), p, step(0.001, alive));
    float3 pNext = LipPoint(top, shore, R, saturate(v + 0.05), max(c, 0.001));
    float3 tangentV = normalize(pNext - p + 1e-4);
    float3 side = float3(-shore.z, 0, shore.x);
    o.normalWS = normalize(cross(tangentV, side));
    o.positionWS = p;
    o.positionCS = TransformWorldToHClip(p);
    o.data = float4(v, alive, a.uv.x, fall);
    return o;
   }

   half4 Fragment(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
   {
    float v = i.data.x, alive = i.data.y, along = i.data.z, fall = i.data.w;
    if (alive < 0.01) discard;
    float3 n = normalize(i.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
    float3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
    Light sun = GetMainLight();
    // Thin water sheet: light passes through it (green), stronger toward the thin tip.
    half3 ambient = SampleSH(n);
    half3 col = _WaterColor.rgb * (ambient * 0.8 + sun.color * saturate(dot(n, sun.direction)) * 0.35);
    half through = pow(saturate(dot(view, -sun.direction) * 0.5 + 0.5), 2.0) * 0.6 + 0.2;
    col += _SSSColor.rgb * sun.color * through * lerp(0.15, 0.55, v);
    // Fresnel sky reflection.
    half fres = 0.02 + 0.98 * pow(1 - saturate(abs(dot(n, view))), 5);
    col = lerp(col, half3(0.3, 0.45, 0.62), fres * 0.6);
    // Foam: the tip is shredded white water; when the lip falls it turns to foam entirely.
    float2 fp = float2(along * 1.6, v * 6.0 + _Time.y * 1.5);
    float bub = ValueNoise(fp * 2.0) * 0.6 + ValueNoise(fp * 6.0) * 0.4;
    float foamAmt = saturate(smoothstep(0.55, 0.95, v) + fall * 1.4);
    float foam = smoothstep(1.0 - foamAmt, 1.0 - foamAmt + 0.25, bub);
    half3 foamCol = _FoamColor.rgb * (sun.color * 0.9 + ambient * 0.8) * lerp(0.7, 1.05, bub);
    col = lerp(col, foamCol, foam);
    // Ragged edges: the tip tears into fingers instead of ending on a straight line.
    float tear = smoothstep(0.82, 1.0, v) * (1.0 - ValueNoise(float2(along * 2.5, _Time.y * 3.0)) * 1.4);
    // Fade in from the face so the lip grows out of the wave instead of sitting on it.
    float alpha = _Opacity * saturate(alive * 3.0) * smoothstep(0.0, 0.25, v) * saturate(1.0 - max(tear, 0) * 2.0) * lerp(1.0, 0.0, fall * fall);
    col = ApplyJungleAtmosphere(col, i.positionWS, sun.color, sun.direction);
    return half4(col, alpha);
   }
   ENDHLSL
  }
 }
}
