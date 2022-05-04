﻿using Coocoo3D.Common;
using Coocoo3D.Components;
using Coocoo3D.Core;
using Coocoo3D.Numerics;
using Coocoo3D.ResourceWrap;
using Coocoo3D.Utility;
using Coocoo3DGraphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Vortice.DXGI;
using System.Runtime.InteropServices;

namespace Coocoo3D.RenderPipeline
{
    public class RecordSettings
    {
        public float FPS;
        public float StartTime;
        public float StopTime;
        public int Width;
        public int Height;
    }
    public class GameDriverContext
    {
        public int NeedRender;
        public bool Playing;
        public double PlayTime;
        public double DeltaTime;
        public float FrameInterval;
        public float PlaySpeed;
        public bool RequireResetPhysics;
        public TimeManager timeManager;

        public void RequireRender(bool updateEntities)
        {
            if (updateEntities)
                RequireResetPhysics = true;
            NeedRender = 10;
        }
    }

    public class RenderPipelineContext : IDisposable
    {
        public RecordSettings recordSettings = new RecordSettings()
        {
            FPS = 60,
            Width = 1920,
            Height = 1080,
            StartTime = 0,
            StopTime = 9999,
        };

        public MainCaches mainCaches = new();

        public Dictionary<string, VisualChannel> visualChannels = new();

        public VisualChannel currentChannel;

        public bool NewRenderPipeline = true;

        public bool SkyBoxChanged = false;

        public string skyBoxName = "_SkyBox";
        public string skyBoxTex = "Samples/adams_place_bridge_2k.jpg";

        public void SetSkyBox(string path)
        {
            if (skyBoxTex == path) return;
            skyBoxTex = path;
            SkyBoxChanged = true;
        }

        public Mesh quadMesh = new Mesh();
        public int frameRenderCount;

        public GraphicsDevice graphicsDevice;
        public GraphicsContext graphicsContext = new GraphicsContext();
        public SwapChain swapChain = new SwapChain();

        public RenderPipelineDynamicContext dynamicContextRead = new();
        public RenderPipelineDynamicContext dynamicContextWrite = new();

        public List<CBuffer> CBs_Bone = new();

        public Format outputFormat = Format.R8G8B8A8_UNorm;
        public Format swapChainFormat { get => swapChain.format; }

        public Recorder recorder;

        public string currentPassSetting = "Samples\\samplePasses.coocoox";

        internal Wrap.GPUWriter gpuWriter = new Wrap.GPUWriter();

        public GameDriverContext gameDriverContext = new GameDriverContext()
        {
            FrameInterval = 1 / 240.0f,
        };

        public Type[] RenderPipelineTypes;

        public string rpBasePth;

        public bool recording = false;

        public bool CPUSkinning = false;

        public void Load()
        {
            graphicsDevice = new GraphicsDevice();
            graphicsContext.Reload(graphicsDevice);

            SkyBoxChanged = true;

            quadMesh.ReloadIndex<int>(4, new int[] { 0, 1, 2, 2, 1, 3 });
            mainCaches.MeshReadyToUpload.Enqueue(quadMesh);
            DirectoryInfo directoryInfo = new DirectoryInfo("Samples");
            foreach (var file in directoryInfo.GetFiles("*.coocoox"))
                mainCaches.GetPassSetting(file.FullName);
            currentPassSetting = Path.GetFullPath(currentPassSetting);
            skyBoxTex = Path.GetFullPath(skyBoxTex);
            recorder = new Recorder()
            {
                graphicsDevice = graphicsDevice,
                graphicsContext = graphicsContext,
            };
            rpBasePth = Path.GetFullPath("Samples");
            RenderPipelineTypes = mainCaches.GetTypes(Path.GetFullPath("RenderPipelines.dll"), typeof(RenderPipeline));
            currentChannel = AddVisualChannel("main");
        }

        public void BeginDynamicContext(Scene scene)
        {
            dynamicContextWrite.FrameBegin();
            dynamicContextWrite.settings = scene.settings.GetClone();

            dynamicContextWrite.passSetting = mainCaches.GetPassSetting(currentPassSetting);

            dynamicContextWrite.frameRenderIndex = frameRenderCount;
            dynamicContextWrite.CPUSkinning = CPUSkinning;
            frameRenderCount++;
        }

        public CBuffer GetBoneBuffer(MMDRendererComponent rendererComponent)
        {
            return CBs_Bone[dynamicContextRead.findRenderer[rendererComponent]];
        }

        LinearPool<Mesh> meshPool = new();
        public Dictionary<MMDRendererComponent, Mesh> meshOverride = new();
        public byte[] bigBuffer = new byte[0];
        public void UpdateGPUResource()
        {
            meshPool.Reset();
            meshOverride.Clear();
            #region Update bone data
            var renderers = dynamicContextRead.renderers;
            while (CBs_Bone.Count < renderers.Count)
            {
                CBuffer constantBuffer = new CBuffer();
                constantBuffer.Mutable = true;
                CBs_Bone.Add(constantBuffer);
            }

            if (CPUSkinning)
            {
                int bufferSize = 0;
                foreach (var renderer in renderers)
                {
                    if (renderer.skinning)
                        bufferSize = Math.Max(GetModelPack(renderer.meshPath).vertexCount, bufferSize);
                }
                bufferSize *= 12;
                if (bufferSize > bigBuffer.Length)
                    bigBuffer = new byte[bufferSize];
            }
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                var model = GetModelPack(renderer.meshPath);
                var mesh = meshPool.Get(() => new Mesh());
                mesh.ReloadIndex<int>(model.vertexCount, null);
                meshOverride[renderer] = mesh;
                if (!renderer.skinning) continue;

                if (CPUSkinning)
                {
                    Skinning(model, renderer, mesh);
                }
                else
                {
                    if (renderer.meshNeedUpdate)
                    {
                        graphicsContext.BeginUpdateMesh(mesh);
                        graphicsContext.UpdateMesh<Vector3>(mesh, renderer.meshPosData1, 0);
                        graphicsContext.EndUpdateMesh(mesh);
                    }
                }
                var matrices = renderer.boneMatricesData;
                for (int k = 0; k < matrices.Length; k++)
                    matrices[k] = Matrix4x4.Transpose(matrices[k]);
                graphicsContext.UpdateResource<Matrix4x4>(CBs_Bone[i], matrices);
            }
            #endregion
        }
        public void Skinning(ModelPack model, MMDRendererComponent renderer, Mesh mesh)
        {
            const int parallelSize = 1024;
            Span<Vector3> d3 = MemoryMarshal.Cast<byte, Vector3>(new Span<byte>(bigBuffer, 0, bigBuffer.Length / 12 * 12));
            Parallel.For(0, (model.vertexCount + parallelSize - 1) / parallelSize, u =>
            {
                Span<Vector3> _d3 = MemoryMarshal.Cast<byte, Vector3>(new Span<byte>(bigBuffer, 0, bigBuffer.Length / 12 * 12));
                int from = u * parallelSize;
                int to = Math.Min(from + parallelSize, model.vertexCount);
                for (int j = from; j < to; j++)
                {
                    Vector3 pos0 = renderer.meshPosData1[j];
                    Vector3 pos1 = Vector3.Zero;
                    int a = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int boneId = model.boneId[j * 4 + k];
                        if (boneId >= renderer.bones.Count) break;
                        Matrix4x4 trans = renderer.boneMatricesData[boneId];
                        float weight = model.boneWeights[j * 4 + k];
                        pos1 += Vector3.Transform(pos0, trans) * weight;
                        a++;
                    }
                    if (a > 0)
                        _d3[j] = pos1;
                    else
                        _d3[j] = pos0;
                }
            });
            //graphicsContext.BeginUpdateMesh(mesh);
            //graphicsContext.UpdateMesh(mesh, d3.Slice(0, model.vertexCount), 0);
            mesh.AddBuffer(d3.Slice(0, model.vertexCount), 0);//for compatibility

            Parallel.For(0, (model.vertexCount + parallelSize - 1) / parallelSize, u =>
            {
                Span<Vector3> _d3 = MemoryMarshal.Cast<byte, Vector3>(new Span<byte>(bigBuffer, 0, bigBuffer.Length / 12 * 12));
                int from = u * parallelSize;
                int to = Math.Min(from + parallelSize, model.vertexCount);
                for (int j = from; j < to; j++)
                {
                    Vector3 norm0 = model.normal[j];
                    Vector3 norm1 = Vector3.Zero;
                    int a = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int boneId = model.boneId[j * 4 + k];
                        if (boneId >= renderer.bones.Count) break;
                        Matrix4x4 trans = renderer.boneMatricesData[boneId];
                        float weight = model.boneWeights[j * 4 + k];
                        norm1 += Vector3.TransformNormal(norm0, trans) * weight;
                        a++;
                    }
                    if (a > 0)
                        _d3[j] = Vector3.Normalize(norm1);
                    else
                        _d3[j] = Vector3.Normalize(norm0);
                }
            });

            //graphicsContext.UpdateMesh(mesh, d3.Slice(0, model.vertexCount), 1);
            mesh.AddBuffer(d3.Slice(0, model.vertexCount), 1);//for compatibility

            //graphicsContext.EndUpdateMesh(mesh);
            graphicsContext.UploadMesh(mesh);//for compatibility
        }

        Queue<string> delayAddVisualChannel = new();
        Queue<string> delayRemoveVisualChannel = new();
        public void DelayAddVisualChannel(string name)
        {
            delayAddVisualChannel.Enqueue(name);
        }
        public void DelayRemoveVisualChannel(string name)
        {
            delayRemoveVisualChannel.Enqueue(name);
        }

        public VisualChannel AddVisualChannel(string name)
        {
            var visualChannel = new VisualChannel();
            visualChannels[name] = visualChannel;
            visualChannel.Name = name;
            visualChannel.graphicsContext = graphicsContext;

            visualChannel.DelaySetRenderPipeline(RenderPipelineTypes[0], this, rpBasePth);

            return visualChannel;
        }

        public void RemoveVisualChannel(string name)
        {
            if (visualChannels.Remove(name, out var vc))
            {
                if (vc == currentChannel)
                    currentChannel = visualChannels["main"];
                vc.Dispose();
            }
        }

        private Dictionary<string, Texture2D> RTs = new();
        private Dictionary<string, TextureCube> RTCs = new();
        private Dictionary<string, GPUBuffer> dynamicBuffers = new();

        public void PreConfig()
        {
            while (delayAddVisualChannel.TryDequeue(out var vcName))
                AddVisualChannel(vcName);
            while (delayRemoveVisualChannel.TryDequeue(out var vcName))
                RemoveVisualChannel(vcName);
            var passSetting = dynamicContextRead.passSetting;
            passSetting.Initialize();
        }

        public void PrepareRenderTarget(PassSetting passSetting)
        {
            if (passSetting.RenderTargets != null)
                foreach (var rt1 in passSetting.RenderTargets)
                {
                    var rt = rt1.Value;
                    if (!rt.flag.HasFlag(RenderTargetFlag.Shared)) continue;
                    int x = (int)rt.width;
                    int y = (int)rt.height;
                    RPUtil.Texture2D(RTs, rt1.Key, rt, x, y, 1, graphicsContext);
                }
            if (passSetting.RenderTargetCubes != null)
                foreach (var rt1 in passSetting.RenderTargetCubes)
                {
                    var rt = rt1.Value;
                    if (!rt.flag.HasFlag(RenderTargetFlag.Shared)) continue;
                    int x = (int)rt.width;
                    int y = (int)rt.height;
                    RPUtil.TextureCube(RTCs, rt1.Key, rt, x, y, 1, graphicsContext);
                }
            if (passSetting.DynamicBuffers != null)
                foreach (var rt1 in passSetting.DynamicBuffers)
                {
                    var rt = rt1.Value;
                    if (!rt.flag.HasFlag(RenderTargetFlag.Shared)) continue;
                    RPUtil.DynamicBuffer(dynamicBuffers, rt1.Key, (int)rt.width, graphicsContext);
                }
        }

        public ModelPack GetModelPack(string path) => mainCaches.GetModel(path);

        public Texture2D _GetTex2DByName(VisualChannel visualChannel, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (RTs.TryGetValue(name, out var tex))
                return tex;
            else if (visualChannel.RTs.TryGetValue(name, out tex))
                return tex;
            else if (mainCaches.TryGetTexture(name, out tex))
                return tex;
            return null;
        }
        public TextureCube _GetTexCubeByName(VisualChannel visualChannel, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (RTCs.TryGetValue(name, out var tex))
                return tex;
            else if (visualChannel.RTCs.TryGetValue(name, out tex))
                return tex;
            return mainCaches.GetTextureCube(name);
        }
        public GPUBuffer _GetBufferByName(VisualChannel visualChannel, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (dynamicBuffers.TryGetValue(name, out var buffer))
                return buffer;
            else if (visualChannel.dynamicBuffers.TryGetValue(name, out buffer))
                return buffer;
            return null;
        }

        public Dictionary<string, object> customData = new();
        public T GetPersistentValue<T>(string name, T defaultValue)
        {
            if (customData.TryGetValue(name, out object val) && val is T val1)
                return val1;
            return defaultValue;
        }

        public void SetPersistentValue<T>(string name, T value)
        {
            customData[name] = value;
        }

        public void AfterRender()
        {
            recorder.OnFrame();
        }

        public void Dispose()
        {

        }
    }
}
