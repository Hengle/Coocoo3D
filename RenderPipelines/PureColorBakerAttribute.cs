﻿using Caprice.Attributes;
using Coocoo3D.RenderPipeline;
using Coocoo3DGraphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RenderPipelines
{
    public class PureColorBakerAttribute : RuntimeBakeAttribute, ITexture2DBaker
    {
        public bool Bake(Texture2D texture, RenderWrap renderWrap, ref object tag)
        {
            renderWrap.SetRootSignature("C");
            renderWrap.SetRenderTarget(texture, true);
            var psoDesc = new PSODesc()
            {
                blendState = BlendState.None,
                cullMode = CullMode.None,
                rtvFormat = texture.GetFormat(),
                inputLayout = InputLayout.mmd,
                renderTargetCount = 1,
            };
            renderWrap.Writer.Write(Color);
            renderWrap.Writer.SetBufferImmediately(0);
            renderWrap.SetShader("PureColor.hlsl", psoDesc);
            renderWrap.DrawQuad();
            return true;
        }

        public Vector4 Color { get; }

        public PureColorBakerAttribute(float r, float g, float b, float a)
        {
            Color = new Vector4(r, g, b, a);
        }
    }
}