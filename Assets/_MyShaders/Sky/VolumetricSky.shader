Shader "CHRONO WILD/Sky/Volumetric Sky"
{
 // Skybox with analytic atmospheric scattering (Rayleigh + Mie) and ray-marched volumetric
 // clouds in a curved shell above the island. The skybox only draws where no geometry wrote
 // depth, so the cloud march runs exclusively on the sky pixels the camera can actually see.
 // Global parameters come from the VolumetricSky component.
 Properties
 {
  [NoScaleOffset] _CloudShape("Cloud Shape (3D: perlin-worley + worley fbm)", 3D) = "white" {}
  [NoScaleOffset] _CloudDetail("Cloud Detail (3D: worley fbm)", 3D) = "white" {}
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
  Cull Off ZWrite Off
  Pass
  {
   Name "VolumetricSky"
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex SkyVertex
   #pragma fragment SkyFragment
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

   TEXTURE3D(_CloudShape); SAMPLER(sampler_CloudShape);
   TEXTURE3D(_CloudDetail); SAMPLER(sampler_CloudDetail);

   // xyz direction towards the sun, w angular radius (radians)
   float4 _VSkySun;
   // rgb sun light used for clouds and the disc, a disc intensity
   float4 _VSkySunColor;
   // x rayleigh, y turbidity, z mie coefficient, w mie anisotropy
   float4 _VSkyAtmosphere;
   // x exposure, y sun illuminance, z ground darkening, w horizon softness
   float4 _VSkyTone;
   float4 _VSkyGroundColor;
   // x bottom (m), y top (m), z coverage, w density (extinction per meter)
   float4 _VCloudLayer;
   // x shape scale (1/m), y detail scale (1/m), z weather scale (1/m), w detail erosion
   float4 _VCloudNoise;
   // xyz wind (m/s), w coverage variation from the weather map
   float4 _VCloudWind;
   // x view steps, y light steps, z max march distance (m), w fade distance (m)
   float4 _VCloudMarch;
   // x forward phase g, y back phase g, z ambient strength, w silver lining
   float4 _VCloudLight;
   // x clouds enabled, y detail enabled
   float4 _VCloudQuality;

   float4 _VSkyLightDir;  // direction the clouds are lit from (sun by day, moon by night)
   float4 _VSkyMoon;
   float4 _VSkyNight;     // x night, y overcast, z thick overcast
   float4 _VSkyGrey;      // overcast sky colour

   static const float EarthRadius = 6360000.0;

   struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

   Varyings SkyVertex(Attributes v)
   {
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    o.direction = v.positionOS.xyz;
    return o;
   }

   float CloudRemap(float v, float a, float b, float c, float d) { return c + (v - a) / max(b - a, 1e-5) * (d - c); }

   float HenyeyGreenstein(float cosTheta, float g)
   {
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4), 1.5));
   }

   // ---------------------------------------------------------------- Atmosphere
   // Preetham / Hoffman single-scattering approximation: cheap, analytic, and it gives the
   // deep blue zenith, bright horizon and warm low-sun colours without a lookup table.
   float3 TotalMie(float turbidity)
   {
    const float3 K = float3(0.686, 0.678, 0.666);
    const float3 Lambda = float3(680e-9, 550e-9, 450e-9);
    float c = (0.2 * turbidity) * 10e-18;
    return 0.434 * c * PI * pow((2.0 * PI) / Lambda, 2.0) * K;
   }

   float3 Atmosphere(float3 dir, float3 sunDir, out float3 extinction)
   {
    const float3 TotalRayleigh = float3(5.804542996e-6, 1.3562911e-5, 3.0265902e-5);
    float rayleigh = _VSkyAtmosphere.x;
    float turbidity = _VSkyAtmosphere.y;
    float sunFade = 1.0 - saturate(1.0 - exp(sunDir.y / 0.45));
    float3 betaR = TotalRayleigh * max(rayleigh - (1.0 - sunFade), 0.05);
    float3 betaM = TotalMie(turbidity) * _VSkyAtmosphere.z;

    // Optical length along the view ray (relative air mass), clamped at the horizon.
    float up = max(dir.y, 0.0);
    float zenith = acos(up);
    float denom = up + 0.15 * pow(max(93.885 - degrees(zenith), 1e-3), -1.253);
    float sR = 8400.0 / denom;
    float sM = 1250.0 / denom;
    extinction = exp(-(betaR * sR + betaM * sM));

    float cosTheta = dot(dir, sunDir);
    float rPhase = 0.0596831 * (1.0 + cosTheta * cosTheta);
    float mPhase = HenyeyGreenstein(cosTheta, _VSkyAtmosphere.w);
    float3 ratio = (betaR * rPhase + betaM * mPhase) / (betaR + betaM);

    // Sun illuminance drops as it nears the horizon.
    float zenithAngle = dot(sunDir, float3(0, 1, 0));
    float sunE = _VSkyTone.y * max(0.0, 1.0 - exp(-((PI / 1.95) - acos(clamp(zenithAngle, -1.0, 1.0))) / 1.5));

    float3 lin = pow(max(sunE * ratio * (1.0 - extinction), 0.0), 1.5);
    lin *= lerp(1.0, pow(max(sunE * ratio * extinction, 0.0), 0.5), saturate(pow(1.0 - zenithAngle, 5.0)));
    return lin * 0.04 * _VSkyTone.x;
   }

   float3 SkyColor(float3 dir, float3 sunDir)
   {
    float3 ext;
    float3 sky = Atmosphere(dir, sunDir, ext);
    // Below the horizon the sky blends into a dim ground/sea colour instead of mirroring.
    float below = saturate(-dir.y * _VSkyTone.w);
    float3 horizon = Atmosphere(normalize(float3(dir.x, 0.0, dir.z) + float3(0, 1e-3, 0)), sunDir, ext);
    return lerp(sky, horizon * _VSkyGroundColor.rgb, below * _VSkyTone.z);
   }

   // Atmosphere plus the faint night air glow and the grey of a closed sky.
   float3 FullSky(float3 dir, float3 sunDir)
   {
    float3 sky = SkyColor(dir, sunDir);
    sky += float3(0.0035, 0.006, 0.014) * _VSkyNight.x * (1.6 - saturate(dir.y));
    if (_VSkyNight.y <= 0.001)
     return sky;
    // A closed sky is never one flat grey: large darker and paler masses drift across the deck,
    // compressing toward the horizon like the underside of a real cloud layer.
    float3 wind = _VCloudWind.xyz * _Time.y;
    float2 uv = dir.xz / (max(dir.y, 0.03) + 0.1);
    float3 q = float3(uv.x * 0.11 + wind.x * 0.00018, 0.21, uv.y * 0.11 + wind.z * 0.00018);
    float4 n = SAMPLE_TEXTURE3D_LOD(_CloudShape, sampler_CloudShape, q, 0);
    float masses = saturate(n.r * 1.6 + n.g * 0.45 - 0.22);
    float4 fine = SAMPLE_TEXTURE3D_LOD(_CloudShape, sampler_CloudShape, q * float3(3.1, 1.0, 3.1) + 0.3, 0);
    float pattern = lerp(masses, saturate(fine.g * 1.4 - 0.15), 0.28);
    float contrast = lerp(0.3, 0.46, _VSkyNight.z) * saturate(dir.y * 7.0 + 0.2);
    float3 deck = _VSkyGrey.rgb * (0.78 + 0.22 * saturate(dir.y)) * (1.0 + (pattern - 0.45) * contrast * 2.0);
    return lerp(sky, deck, _VSkyNight.y);
   }

   float StarField(float3 dir)
   {
    float3 cell = floor(dir * 190.0);
    float3 p = frac(cell * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yzx + 33.33);
    float h = frac((p.x + p.y) * p.z);
    if (h < 0.9965)
     return 0.0;
    float3 centre = (cell + 0.5) / 190.0;
    float glow = saturate(1.0 - length(dir - centre) * 240.0);
    float twinkle = 0.7 + 0.3 * sin(_Time.y * (2.0 + h * 8.0) + h * 400.0);
    return glow * glow * twinkle * ((h - 0.9965) / 0.0035);
   }

   // ---------------------------------------------------------------- Clouds
   // Distance along dir from a camera at height h to the sphere of radius EarthRadius + hs.
   // c is factored to avoid cancellation between two huge squared radii.
   float ShellDistance(float h, float dirY, float hs)
   {
    float r0 = EarthRadius + h;
    float b = r0 * dirY;
    float c = (h - hs) * (2.0 * EarthRadius + h + hs);
    float disc = b * b - c;
    return disc < 0.0 ? -1.0 : -b + sqrt(disc);
   }

   float HeightAt(float h, float dirY, float t)
   {
    float r0 = EarthRadius + h;
    return sqrt(r0 * r0 + 2.0 * r0 * dirY * t + t * t) - EarthRadius;
   }

   float CloudDensity(float3 p, float heightFraction, bool detail)
   {
    float3 wind = _VCloudWind.xyz * _Time.y;
    // Weather: a large-scale slice of the shape noise decides where cloud fields form.
    float2 wuv = (p.xz + wind.xz * 0.6) * _VCloudNoise.z;
    float weather = SAMPLE_TEXTURE3D_LOD(_CloudShape, sampler_CloudShape, float3(wuv.x, 0.37, wuv.y), 0).r;
    float coverage = saturate(_VCloudLayer.z + (weather - 0.5) * _VCloudWind.w);

    // Cumulus profile: flat-ish base, rounded tops that grow with the local weather.
    float top = lerp(0.45, 1.0, saturate(weather * 1.4 - 0.2));
    float profile = saturate(CloudRemap(heightFraction, 0.0, 0.07, 0.0, 1.0)) * saturate(CloudRemap(heightFraction, top * 0.55, top, 1.0, 0.0));

    float3 q = (p + wind) * _VCloudNoise.x;
    q.y += heightFraction * 0.2; // slight skew so towers lean with the wind
    float4 n = SAMPLE_TEXTURE3D_LOD(_CloudShape, sampler_CloudShape, q, 0);
    float fbm = n.g * 0.625 + n.b * 0.25 + n.a * 0.125;
    float shape = saturate(CloudRemap(n.r, fbm - 1.0, 1.0, 0.0, 1.0));
    // Coverage sets the threshold above which the noise turns into cloud.
    float base = saturate(CloudRemap(shape * profile, 1.0 - coverage, 1.0, 0.0, 1.0));

    if (detail && base > 0.0 && _VCloudQuality.y > 0.5)
    {
     float3 dq = (p + wind * 1.6) * _VCloudNoise.y;
     float3 d = SAMPLE_TEXTURE3D_LOD(_CloudDetail, sampler_CloudDetail, dq, 0).rgb;
     float dfbm = d.r * 0.625 + d.g * 0.25 + d.b * 0.125;
     // Wispy erosion at the base, billowy at the top.
     float mod = lerp(dfbm, 1.0 - dfbm, saturate(heightFraction * 6.0));
     base = saturate(CloudRemap(base, mod * _VCloudNoise.w * 0.5, 1.0, 0.0, 1.0));
    }
    return base * _VCloudLayer.w;
   }

   // Optical depth towards the sun through the cheap (no detail) density field.
   float LightOpticalDepth(float3 p, float3 sunDir)
   {
    int steps = (int)_VCloudMarch.y;
    float thickness = _VCloudLayer.y - _VCloudLayer.x;
    float stepLength = thickness * 0.5 / max(steps, 1);
    float depth = 0.0;
    float travelled = 0.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
     float len = stepLength * (1.0 + i * 0.6);
     travelled += len * 0.5;
     float3 s = p + sunDir * travelled;
     float hf = saturate((s.y - _VCloudLayer.x) / thickness);
     depth += CloudDensity(s, hf, false) * len;
     travelled += len * 0.5;
    }
    return depth;
   }

   float3 MarchClouds(float3 dir, float3 sunDir, float3 skyZenith, float2 pixel, inout float transmittance)
   {
    float h = _WorldSpaceCameraPos.y;
    float bottom = _VCloudLayer.x;
    float top = _VCloudLayer.y;
    if (h >= bottom || dir.y < -0.05)
     return 0;
    float tStart = ShellDistance(h, dir.y, bottom);
    float tEnd = ShellDistance(h, dir.y, top);
    if (tStart < 0.0 || tStart > _VCloudMarch.z)
     return 0;
    tEnd = min(tEnd, tStart + (top - bottom) * 4.0);
    tEnd = min(tEnd, _VCloudMarch.z);

    int steps = (int)_VCloudMarch.x;
    float stepLength = (tEnd - tStart) / max(steps, 1);
    float jitter = InterleavedGradientNoise(pixel, 0);

    float cosTheta = dot(dir, sunDir);
    float3 sunLight = _VSkySunColor.rgb;
    float3 ambientTop = skyZenith * _VCloudLight.z;
    float3 ambientBottom = ambientTop * 0.35;

    float3 scatter = 0;
    float firstHit = -1.0;
    [loop]
    for (int i = 0; i < steps; i++)
    {
     float t = tStart + (i + jitter) * stepLength;
     float height = HeightAt(h, dir.y, t);
     float hf = saturate((height - bottom) / (top - bottom));
     float3 p = float3(_WorldSpaceCameraPos.x + dir.x * t, height, _WorldSpaceCameraPos.z + dir.z * t);
     float sigma = CloudDensity(p, hf, true);
     if (sigma <= 1e-6)
      continue;
     if (firstHit < 0.0)
      firstHit = t;

     float od = LightOpticalDepth(p, sunDir);
     // Multiple-scattering approximation (Hillaire 2016): each octave scatters with weaker
     // extinction and a flatter phase, which brightens thick cloud cores realistically.
     float3 light = 0;
     float a = 1.0, b = 1.0, c = 1.0;
     [unroll]
     for (int o = 0; o < 3; o++)
     {
      float phase = lerp(HenyeyGreenstein(cosTheta, _VCloudLight.x * c), HenyeyGreenstein(cosTheta, _VCloudLight.y * c), 0.3);
      light += b * phase * exp(-od * a);
      a *= 0.5; b *= 0.5; c *= 0.5;
     }
     // Powder: edges facing the viewer away from the sun read darker, giving cauliflower depth.
     float powder = lerp(1.0, saturate(1.0 - exp(-sigma * 120.0)) * 2.0, _VCloudLight.w);
     float3 ambient = lerp(ambientBottom, ambientTop, hf);
     float3 S = (sunLight * light * 4.0 * PI * powder + ambient) * sigma;

     float stepTr = exp(-sigma * stepLength);
     scatter += transmittance * (S - S * stepTr) / sigma;
     transmittance *= stepTr;
     if (transmittance < 0.01)
     {
      transmittance = 0.0;
      break;
     }
    }

    // Aerial perspective: far clouds dissolve into the sky behind them.
    float fade = firstHit > 0.0 ? exp(-max(firstHit - _VCloudMarch.w * 0.25, 0.0) / _VCloudMarch.w) : 1.0;
    transmittance = lerp(1.0, transmittance, fade);
    return scatter * fade;
   }

   half4 SkyFragment(Varyings input) : SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float3 dir = normalize(input.direction);
    float3 sunDir = normalize(_VSkySun.xyz);

    float3 color = FullSky(dir, sunDir);
    float clearNight = _VSkyNight.x * (1.0 - _VSkyNight.y);
    if (clearNight > 0.002 && dir.y > -0.02)
    {
     color += float3(0.85, 0.9, 1.0) * StarField(dir) * 0.9 * clearNight * saturate(dir.y * 8.0);
     float moonCos = dot(dir, _VSkyMoon.xyz);
     color += float3(0.78, 0.82, 0.92) * smoothstep(cos(0.024), cos(0.021), moonCos) * 1.7 * clearNight;
     color += float3(0.1, 0.13, 0.2) * pow(saturate(moonCos), 800.0) * 0.4 * clearNight;
    }

    // Sun disc with limb darkening, dimmed by the atmosphere along the view ray.
    float3 ext;
    Atmosphere(dir, sunDir, ext);
    float cosAngle = dot(dir, sunDir);
    float radius = _VSkySun.w;
    float disc = smoothstep(cos(radius), cos(radius * 0.92), cosAngle);
    float limb = 0.6 + 0.4 * sqrt(saturate(1.0 - pow(acos(min(cosAngle, 1.0)) / radius, 2.0)));
    color += _VSkySunColor.rgb * _VSkySunColor.a * disc * limb * ext * step(-0.02, dir.y) * (1.0 - _VSkyNight.y) * (1.0 - _VSkyNight.x);

    if (_VCloudQuality.x > 0.5)
    {
     float3 zenith = FullSky(float3(0, 1, 0), sunDir);
     float transmittance = 1.0;
     float3 clouds = MarchClouds(dir, normalize(_VSkyLightDir.xyz), zenith, input.positionCS.xy, transmittance);
     color = color * transmittance + clouds;
    }
    return half4(color, 1.0);
   }
   ENDHLSL
  }
 }
 Fallback Off
}
