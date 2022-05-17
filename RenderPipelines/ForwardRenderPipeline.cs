﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Caprice.Attributes;
using Coocoo3D.RenderPipeline;
using Coocoo3DGraphics;
using Coocoo3D.UI.Attributes;
using System.Numerics;

namespace RenderPipelines
{
    [UIShow(name: "前向渲染")]
    public class ForwardRenderPipeline : RenderPipeline
    {
        [AOV(AOVType.Color)]
        [Size("Output")]
        [Format(ResourceFormat.R8G8B8A8_UNorm)]
        public Texture2D output;

        [AOV(AOVType.Depth)]
        [Size("Output")]
        [Format(ResourceFormat.D32_Float)]
        [AutoClear]
        public Texture2D depth;

        [Size("Output")]
        [Format(ResourceFormat.D32_Float)]
        public Texture2D depth2;

        [Size("HalfOutput")]
        [Format(ResourceFormat.R16G16B16A16_Float)]
        public Texture2D intermedia1;
        [Size("Output")]
        [Format(ResourceFormat.R16G16B16A16_Float)]
        public Texture2D intermedia2;

        [Size("Output")]
        [Format(ResourceFormat.R16G16B16A16_Float)]
        public Texture2D noPostProcess;

        [Size("Output")]
        [Format(ResourceFormat.R16G16B16A16_Float)]
        public Texture2D noPostProcess2;

        [Size(4096, 4096)]
        [Format(ResourceFormat.D32_Float)]
        [AutoClear]
        public Texture2D _ShadowMap;

        [Size(128, 128)]
        [Format(ResourceFormat.R16G16B16A16_Float)]
        [BRDFBaker]
        public Texture2D _BRDFLUT;

        [UIShow(name: "天空盒")]
        [Srgb]
        [Resource("adams_place_bridge_2k.jpg")]
        public Texture2D skyboxTexture;

        [Size(1024, 1024, 6)]
        [Format(ResourceFormat.R16G16B16A16_Float)]
        [CubeFrom2D(nameof(skyboxTexture))]
        [BakeDependency(nameof(skyboxTexture))]
        public TextureCube _SkyBox;

        [Size(512, 512, 6)]
        [Format(ResourceFormat.R16G16B16A16_Float)]
        [EnvironmentReflection(nameof(_SkyBox))]
        [BakeDependency(nameof(_SkyBox))]
        public TextureCube _Environment;

        [Indexable]
        [UIDragFloat(0.01f, 0, name: "天空盒亮度")]
        public float SkyLightMultiple = 3;

        [Indexable]
        [UIDragFloat(0.01f, 0, name: "亮度")]
        public float Brightness = 1;

        [Indexable]
        [UIShow(name: "启用雾")]
        public bool EnableFog;

        [Indexable]
        [UIColor(name: "雾颜色")]
        public Vector3 FogColor = new Vector3(0.4f, 0.4f, 0.6f);

        [Indexable]
        [UIDragFloat(0.001f, 0, name: "雾密度")]
        public float FogDensity = 0.005f;

        [Indexable]
        [UIDragFloat(0.1f, 0, name: "雾开始距离")]
        public float FogStartDistance = 5;

        [Indexable]
        //[UIDragFloat(0.1f, 0, name: "雾结束距离")]
        public float FogEndDistance = 100000;

        [UIShow(name: "启用泛光")]
        public bool EnableBloom;
        [Indexable]
        [UIDragFloat(0.01f, name: "泛光阈值")]
        public float BloomThreshold = 1.05f;
        [Indexable]
        [UIDragFloat(0.01f, name: "泛光强度")]
        public float BloomIntensity = 0.1f;

        [UIShow(name: "启用TAA")]
        public bool EnableTAA;

        [UIDragFloat(0.01f, name: "TAA系数")]
        [Indexable]
        public float TAAFactor = 0.3f;

        #region Material Parameters
        [Indexable]
        [UIShow(UIShowType.Material, "透明材质")]
        public bool IsTransparent;

        [Indexable]
        [UISlider(0.0f, 1.0f, UIShowType.Material, "金属")]
        public float Metallic;

        [Indexable]
        [UISlider(0.0f, 1.0f, UIShowType.Material, "粗糙")]
        public float Roughness = 0.8f;

        [Indexable]
        [UIDragFloat(0.01f, 0, float.MaxValue, UIShowType.Material, "发光")]
        public float Emissive;

        [Indexable]
        [UISlider(0.0f, 1.0f, UIShowType.Material, "高光")]
        public float Specular = 0.5f;

        [UIShow(UIShowType.Material)]
        [PureColorBaker(1, 1, 1, 1)]
        [Format(ResourceFormat.R8G8B8A8_UNorm)]
        [Size(32, 32)]
        [Srgb]
        public Texture2D _Albedo;

        [UIShow(UIShowType.Material)]
        [PureColorBaker(1, 1, 1, 1)]
        [Format(ResourceFormat.R8G8B8A8_UNorm)]
        [Size(32, 32)]
        public Texture2D _Metallic;

        [UIShow(UIShowType.Material)]
        [PureColorBaker(1, 1, 1, 1)]
        [Format(ResourceFormat.R8G8B8A8_UNorm)]
        [Size(32, 32)]
        public Texture2D _Roughness;

        [UIShow(UIShowType.Material)]
        [Srgb]
        [PureColorBaker(1, 1, 1, 1)]
        [Format(ResourceFormat.R8G8B8A8_UNorm)]
        [Size(32, 32)]
        public Texture2D _Emissive;

        [UIShow(UIShowType.Material)]
        [PureColorBaker(0.5f, 0.5f, 1, 1)]
        [Format(ResourceFormat.R8G8B8A8_UNorm)]
        [Size(32, 32)]
        public Texture2D _Normal;

        #endregion

        [Indexable]
        public float cameraFar;
        [Indexable]
        public float cameraNear;
        [Indexable]
        public Matrix4x4 ViewProjection = Matrix4x4.Identity;
        [Indexable]
        public Matrix4x4 InvertViewProjection = Matrix4x4.Identity;

        [Indexable]
        public Matrix4x4 _ViewProjection = Matrix4x4.Identity;
        [Indexable]
        public Matrix4x4 _InvertViewProjection = Matrix4x4.Identity;

        Random random = new Random(0);

        [Indexable]
        public int outputWidth;
        [Indexable]
        public int outputHeight;

        ForwardRenderPass forwordRenderPass = new ForwardRenderPass()
        {
            renderTarget = nameof(noPostProcess),
            depthStencil = nameof(depth),
        };

        public PostProcessPass postProcess = new PostProcessPass()
        {
            inputColor = nameof(noPostProcess),
            inputDepth = nameof(depth),
            output = nameof(output),

        };

        public TAAPass taaPass = new TAAPass()
        {
            target = nameof(noPostProcess),
            depth = nameof(depth),
            history = nameof(noPostProcess2),
            historyDepth = nameof(depth2),
            cbv = new object[]
            {
                nameof(ViewProjection),
                nameof(InvertViewProjection),
                nameof(_ViewProjection),
                nameof(_InvertViewProjection),
                nameof(outputWidth),
                nameof(outputHeight),
                nameof(cameraFar),
                nameof(cameraNear),
                nameof(TAAFactor),
            }
        };

        public override void BeforeRender()
        {
            renderWrap.GetOutputSize(out outputWidth, out outputHeight);
            renderWrap.SetSize("Output", outputWidth, outputHeight);
            renderWrap.SetSize("HalfOutput", (outputWidth + 1) / 2, (outputHeight + 1) / 2);
            renderWrap.texLoading = renderWrap.GetTex2DLoaded("loading.png");
            renderWrap.texError = renderWrap.GetTex2DLoaded("error.png");
        }

        public override void Render()
        {
            var camera = renderWrap.Camera;
            ViewProjection = camera.vpMatrix;
            InvertViewProjection = camera.pvMatrix;
            cameraFar = camera.far;
            cameraNear = camera.near;
            if (EnableTAA)
            {
                Vector2 jitterVector = new Vector2((float)(random.NextDouble() * 2 - 1) / outputWidth, (float)(random.NextDouble() * 2 - 1) / outputHeight);
                camera = camera.GetJitter(jitterVector);
            }

            forwordRenderPass.Brightness = Brightness;
            postProcess.EnableBloom = EnableBloom;

            forwordRenderPass.SetCamera(camera);
            forwordRenderPass.Execute(renderWrap);

            if (EnableTAA)
            {
                taaPass.Execute(renderWrap);
            }
            postProcess.Execute(renderWrap);

            renderWrap.Swap(nameof(noPostProcess), nameof(noPostProcess2));
            renderWrap.Swap(nameof(depth), nameof(depth2));
            camera = renderWrap.Camera;
            _ViewProjection = camera.vpMatrix;
            _InvertViewProjection = camera.pvMatrix;
        }

        public override void AfterRender()
        {
        }
    }
}