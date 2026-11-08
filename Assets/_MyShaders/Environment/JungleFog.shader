Shader "Hidden/CHRONO WILD/Jungle Fog"
{
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline"}
  ZWrite Off ZTest Always Cull Off Blend Off
  Pass
  {
   Name "JungleFog"
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment FogFragment
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

   #include "Assets/_MyShaders/Environment/JungleAtmosphere.hlsl"
   // x noise amount, y noise scale
   float4 _JungleFogNoise;
   float4 _JungleFogWind;
   // x sun scattering, y anisotropy, z march distance, w steps
   float4 _JungleFogMarch;

   // Volumetric sky globals (VolumetricSky component), used for cloud shadows on the ground.
   TEXTURE3D(_VCloudShapeTex); SAMPLER(sampler_VCloudShapeTex);
   float4 _VCloudLayer;   // x bottom, y top, z coverage, w density
   float4 _VCloudNoise;   // x shape scale, y detail scale, z weather scale
   float4 _VCloudWind;    // xyz wind, w coverage variation
   float _VCloudShadow;   // strength

   float CloudRemap(float v, float a, float b, float c, float d) { return c + (v - a) / max(b - a, 1e-5) * (d - c); }

   // Crytek-style screen-space cloud shadow: project the pixel along the sun onto the middle of
   // the cloud layer and evaluate the same coverage field the sky marches through.
   float CloudShadowAt(float3 p, float3 sunDir)
   {
    if (_VCloudShadow <= 0.0 || _VCloudLayer.w <= 0.0)
     return 0.0;
    float mid = lerp(_VCloudLayer.x, _VCloudLayer.y, 0.3);
    float3 q = p + sunDir * ((mid - p.y) / max(sunDir.y, 0.15));
    float3 wind = _VCloudWind.xyz * _Time.y;
    float2 wuv = (q.xz + wind.xz * 0.6) * _VCloudNoise.z;
    float weather = SAMPLE_TEXTURE3D_LOD(_VCloudShapeTex, sampler_VCloudShapeTex, float3(wuv.x, 0.37, wuv.y), 0).r;
    float coverage = saturate(_VCloudLayer.z + (weather - 0.5) * _VCloudWind.w);
    float3 sq = (float3(q.x, mid, q.z) + wind) * _VCloudNoise.x;
    sq.y += 0.06;
    float4 n = SAMPLE_TEXTURE3D_LOD(_VCloudShapeTex, sampler_VCloudShapeTex, sq, 0);
    float fbm = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
    float shape = saturate(CloudRemap(n.r, fbm - 1.0, 1.0, 0.0, 1.0));
    float base = saturate(CloudRemap(shape, 1.0 - coverage, 1.0, 0.0, 1.0));
    return saturate(base * 4.0);
   }

   float Hash(float3 p)
   {
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
   }

   float ValueNoise(float3 x)
   {
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                     lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                     lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
   }

   float Density(float3 p, bool withNoise)
   {
    // Exponential height profile, capped so hollows don't turn into solid walls.
    float height = min(exp(-(p.y - _JungleFogParams.y) * _JungleFogParams.z), 3.0);
    float d = _JungleFogParams.x * height;
    // Dense fog only under the forest; open ground (beach, clearings outside) keeps a light haze.
    float region = 1.0 - smoothstep(_JungleFogRegion.z, _JungleFogRegion.z + _JungleFogRegion.w, distance(p.xz, _JungleFogRegion.xy));
    d *= lerp(_JungleFogOpenDensity, 1.0, region);
    // Local banks: swamp ground mist, moor haze.
    d += JungleExtraFogDensity(p);
    if (withNoise)
    {
     float3 q = p * _JungleFogNoise.y + _JungleFogWind.xyz * _Time.y * _JungleFogNoise.y;
     float n = ValueNoise(q) * 0.65 + ValueNoise(q * 2.7) * 0.35;
     d *= lerp(1.0, saturate(n * 1.8 - 0.2) * 1.6, _JungleFogNoise.x);
    }
    return d;
   }

   float Phase(float cosTheta, float g)
   {
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
   }

   half4 FogFragment(Varyings input) : SV_Target
   {
    float2 uv = input.texcoord;
    half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
    float rawDepth = SampleSceneDepth(uv);
    #if UNITY_REVERSED_Z
     bool sky = rawDepth <= 1e-6;
    #else
     bool sky = rawDepth >= 1.0 - 1e-6;
     rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
    #endif
    float3 camera = _WorldSpaceCameraPos;
    float3 world = ComputeWorldSpacePosition(uv, sky ? 0.5 : rawDepth, UNITY_MATRIX_I_VP);
    float3 dir = normalize(world - camera);
    float dist = sky ? _JungleFogParams.w : distance(world, camera);

    Light sun = GetMainLight();
    // Under a cloud the light shafts dim too.
    float cameraCloud = CloudShadowAt(camera, sun.direction);
    float3 sunLight = sun.color * Phase(dot(dir, sun.direction), _JungleFogMarch.y) * _JungleFogMarch.x * 4.0 * PI * (1.0 - cameraCloud * _VCloudShadow);
    if (!sky)
    {
     // Cloud shadows only remove direct sun, so they only darken pixels the sun actually reaches.
     half sunVisible = 1.0;
     #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
      sunVisible = MainLightRealtimeShadow(TransformWorldToShadowCoord(world));
     #endif
     scene.rgb *= 1.0 - CloudShadowAt(world, sun.direction) * _VCloudShadow * sunVisible;
    }
    float3 ambient = _JungleFogAmbient.rgb;

    // Zero steps (low quality) skips the shadow-marched shafts; only the analytic fog remains.
    int steps = (int)_JungleFogMarch.w;
    float marchLength = steps > 0 ? min(dist, _JungleFogMarch.z) : 0.0;
    float stepLength = marchLength / max(steps, 1);
    // Fixed per-pixel jitter: an animated pattern needs TAA, without it the shafts shimmer while walking.
    float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);

    float transmittance = 1.0;
    float3 inscatter = 0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
     float t = (i + jitter) * stepLength;
     float3 p = camera + dir * t;
     float sigma = Density(p, true);
     half visibility = 1.0;
     #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
      visibility = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
     #endif
     float absorbed = 1.0 - exp(-sigma * stepLength);
     inscatter += transmittance * absorbed * (ambient + sunLight * visibility);
     transmittance *= 1.0 - absorbed;
    }

    // Beyond the marched range: analytic height fog, sun mostly blocked by the canopy.
    float remaining = dist - marchLength;
    if (remaining > 0.0)
    {
     float3 mid = camera + dir * (marchLength + remaining * 0.5);
     float absorbed = 1.0 - exp(-Density(mid, false) * remaining);
     inscatter += transmittance * absorbed * (ambient + sunLight * 0.2);
     transmittance *= 1.0 - absorbed;
    }

    float3 color = scene.rgb * transmittance + inscatter;

    // Distance haze (aerial perspective): far tree lines and hills dissolve into the sky color
    // regardless of height. The sky itself only hazes toward the horizon.
    float hazeDist = max(dist - _JungleHaze.y, 0.0);
    float midHeight = camera.y + dir.y * min(dist, 400.0) * 0.5;
    float thin = exp(-max(midHeight - _JungleFogParams.y, 0.0) * _JungleHaze.z);
    // Humid grey-green haze under the canopy, clear blue air when the camera is on open ground.
    float inForest = 1.0 - smoothstep(_JungleFogRegion.z, _JungleFogRegion.z + _JungleFogRegion.w, distance(camera.xz, _JungleFogRegion.xy));
    float hazeDensity = lerp(_JungleOpenHaze.a, _JungleHaze.x, inForest);
    float haze = 1.0 - exp(-hazeDist * hazeDensity * thin);
    if (sky)
        haze *= saturate(1.0 - dir.y * 2.5);
    float3 hazeColor = lerp(_JungleOpenHaze.rgb, _JungleHazeColor.rgb, inForest) + sun.color * Phase(dot(dir, sun.direction), 0.45) * _JungleHazeColor.a * 4.0 * PI;
    color = lerp(color, hazeColor, haze);

    return half4(color, scene.a);
   }
   ENDHLSL
  }
 }
}
