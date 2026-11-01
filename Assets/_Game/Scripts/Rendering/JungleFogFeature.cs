using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ChronoWild.Rendering
{
    /// <summary>
    /// Full-screen height fog with volumetric sun shafts. Skipped entirely unless a
    /// JungleAtmosphere component is active in the loaded scenes.
    /// </summary>
    public class JungleFogFeature : ScriptableRendererFeature
    {
        public Shader shader;
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;

        Material material;
        FogPass pass;

        public override void Create()
        {
            pass = new FogPass { renderPassEvent = passEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (JungleAtmosphere.Active == null || shader == null)
                return;
            var cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;
            if (material == null)
                material = CoreUtils.CreateEngineMaterial(shader);
            pass.material = material;
            pass.renderPassEvent = passEvent;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
        }

        class FogPass : ScriptableRenderPass
        {
            public Material material;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer)
                    return;
                TextureHandle source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_JungleFogColor";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, material, 0), "Jungle Fog");
                resources.cameraColor = destination;
            }
        }
    }
}
