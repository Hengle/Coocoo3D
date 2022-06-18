﻿using Coocoo3D.RenderPipeline;
using Coocoo3DGraphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RenderPipelines
{
    public class BloomPass
    {
        string shader = "Bloom.hlsl";

        PSODesc psoDesc = new PSODesc()
        {
            cullMode = CullMode.None,
            renderTargetCount = 1,
        };

        string rs = "Csu";

        public object[][] cbvs;

        public string intermediaTexture;

        public string input;

        public string output;

        public int mipLevel;

        public (int, int) inputSize;

        (string, string)[] keywords1 = { ("BLOOM_1", "1") };
        (string, string)[] keywords2 = { ("BLOOM_2", "1") };

        public void Execute(RenderWrap renderWrap)
        {
            if (cbvs == null || cbvs.Length < 1 || string.IsNullOrEmpty(input)) return;
            renderWrap.SetRootSignature(rs);

            var inputTexture = renderWrap.GetRenderTexture2D(input);
            var intermedia1 = renderWrap.GetRenderTexture2D(intermediaTexture);
            Vector2 intermediaSize = new Vector2(intermedia1.width, intermedia1.height);
            cbvs[0][0] = new Vector2((float)inputSize.Item1 / inputTexture.width / intermediaSize.X, 0);
            //cbvs[0][1] = 0.0f;

            cbvs[0][4] = new Vector2((float)inputSize.Item1 / inputTexture.width / intermediaSize.X, (float)inputSize.Item2 / inputTexture.height / intermediaSize.Y);

            var writer = renderWrap.Writer;
            for (int i = 0; i < cbvs.Length; i++)
            {
                object[] cbv1 = cbvs[i];
                if (cbv1 == null) continue;
                renderWrap.Write(cbv1, writer);
                writer.SetBufferImmediately(i);
            }

            renderWrap.SetSRVLim(inputTexture, mipLevel, 0);
            renderWrap.SetUAV(intermedia1, 0);
            renderWrap.Dispatch(shader, keywords1, (intermedia1.width + 63) / 64, intermedia1.height);

            var renderTarget = renderWrap.GetRenderTexture2D(output);
            cbvs[0][0] = new Vector2(0, 1.0f / intermediaSize.Y);
            //cbvs[0][1] = ;
            cbvs[0][4] = new Vector2(1.0f / (float)renderTarget.width, 1.0f / (float)renderTarget.height);
            for (int i = 0; i < cbvs.Length; i++)
            {
                object[] cbv1 = cbvs[i];
                if (cbv1 == null) continue;
                renderWrap.Write(cbv1, writer);
                writer.SetBufferImmediately(i);
            }


            renderWrap.SetSRV(intermedia1, 0);
            renderWrap.SetUAV(renderTarget, 0);
            renderWrap.Dispatch(shader, keywords2, renderTarget.width, (renderTarget.height + 63) / 64);
            writer.Clear();
        }
    }
}
