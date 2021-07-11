using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Text;
using WaveEngine.Common.Graphics;
using WaveEngine.Platform;
using VisualTests.Runners.Common;
using Buffer = WaveEngine.Common.Graphics.Buffer;
using System.Runtime.CompilerServices;
using WaveEngine.Mathematics;

namespace VisualTests.LowLevel.Tests
{
    public unsafe class ImGuiRenderer : IDisposable
    {
        public GraphicsContext context;

        private Buffer vertexBuffer;
        private Buffer indexBuffer;
        private Buffer constantBuffer;
        private Texture fontTexture;
        //private Shader vertexShader;
        //private Shader pixelShader;
        private GraphicsPipelineState pipelineState;
        private ResourceLayout layout;
        private ResourceLayout textureLayout;
        private ResourceSet resourceSet;
        private ResourceSet fontResourceSet;
        private Rectangle[] scissors;
        private ImGuiIOPtr io;
        private Buffer[] vertexBuffers;

        private int windowWidth;
        private int windowHeight;
        private System.Numerics.Vector2 scaleFactor = System.Numerics.Vector2.One;

        private IntPtr fontAtlasID = (IntPtr)1;
        private int lastAssignedID = 10;

        private struct ResourceSetInfo
        {
            public readonly IntPtr ImGuiBinding;
            public readonly ResourceSet ResourceSet;

            public ResourceSetInfo(IntPtr imGuiBinding, ResourceSet resourceSet)
            {
                ImGuiBinding = imGuiBinding;
                ResourceSet = resourceSet;
            }
        }

        private readonly Dictionary<Texture, ResourceSetInfo> resourceByTexture = new Dictionary<Texture, ResourceSetInfo>();
        private readonly Dictionary<IntPtr, ResourceSetInfo> resourceById = new Dictionary<IntPtr, ResourceSetInfo>();

        public ImGuiRenderer(GraphicsContext context, FrameBuffer frameBuffer, ShaderDescription vertexShaderDescription, ShaderDescription pixelShaderDescription)
        {
            this.context = context;

            IntPtr imGuiContext = ImGui.CreateContext();
            ImGui.SetCurrentContext(imGuiContext);

            ImGui.GetIO().Fonts.AddFontDefault();

            // Create native resources
            var vertexShader = context.Factory.CreateShader(ref vertexShaderDescription);
            var pixelShader = context.Factory.CreateShader(ref pixelShaderDescription);

            var vertexBufferDescription = new BufferDescription(
                10000, 
                BufferFlags.VertexBuffer, 
                ResourceUsage.Dynamic,
                ResourceCpuAccess.Write);

            this.vertexBuffer = context.Factory.CreateBuffer(ref vertexBufferDescription);
            this.vertexBuffers = new Buffer[1];
            this.vertexBuffers[0] = this.vertexBuffer;

            var indexBufferDescription = new BufferDescription(
                2000, 
                BufferFlags.IndexBuffer, 
                ResourceUsage.Dynamic,
                ResourceCpuAccess.Write);

            this.indexBuffer = context.Factory.CreateBuffer(ref indexBufferDescription);

            var vertexLayouts = new InputLayouts()
                  .Add(new LayoutDescription()
                              .Add(new ElementDescription(ElementFormat.Float2, ElementSemanticType.Position))
                              .Add(new ElementDescription(ElementFormat.Float2, ElementSemanticType.TexCoord))
                              .Add(new ElementDescription(ElementFormat.UByte4Normalized, ElementSemanticType.Color)));

            var layoutDescription = new ResourceLayoutDescription(
                    new LayoutElementDescription(0, ResourceType.ConstantBuffer, ShaderStages.Vertex),
                    new LayoutElementDescription(0, ResourceType.Sampler, ShaderStages.Pixel));

            this.layout = context.Factory.CreateResourceLayout(ref layoutDescription);

            var textureLayoutDescription = new ResourceLayoutDescription(
                    new LayoutElementDescription(0, ResourceType.Texture, ShaderStages.Pixel));

            this.textureLayout = context.Factory.CreateResourceLayout(ref textureLayoutDescription);

            var blendState = BlendStates.AlphaBlend;
            blendState.RenderTarget0.SourceBlendColor = Blend.SourceAlpha;
            blendState.RenderTarget0.DestinationBlendColor = Blend.InverseSourceAlpha;
            blendState.RenderTarget0.BlendOperationColor = BlendOperation.Add;
            blendState.RenderTarget0.SourceBlendAlpha = Blend.SourceAlpha;
            blendState.RenderTarget0.DestinationBlendAlpha = Blend.InverseSourceAlpha;
            blendState.RenderTarget0.BlendOperationAlpha = BlendOperation.Add;
            blendState.IndependentBlendEnable = true;

            var pipelineDescription = new GraphicsPipelineDescription()
            {
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                InputLayouts = vertexLayouts,
                ResourceLayouts = new[] { this.layout, this.textureLayout },
                Shaders = new GraphicsShaderStateDescription()
                {
                    VertexShader = vertexShader,
                    PixelShader = pixelShader,
                },
                RenderStates = new RenderStateDescription()
                {
                    RasterizerState = RasterizerStates.None,
                    BlendState = blendState,
                    DepthStencilState = DepthStencilStates.None,
                },
                Outputs = frameBuffer.OutputDescription,
            };

            this.windowWidth = (int)frameBuffer.Width;
            this.windowHeight = (int)frameBuffer.Height;

            this.pipelineState = context.Factory.CreateGraphicsPipeline(ref pipelineDescription);

            var constantBufferDescription = new BufferDescription((uint)Unsafe.SizeOf<Matrix4x4>(), BufferFlags.ConstantBuffer, ResourceUsage.Default);
            this.constantBuffer = context.Factory.CreateBuffer(ref constantBufferDescription);

            SamplerStateDescription samplerDescription = SamplerStates.PointClamp;
            var sampler = context.Factory.CreateSamplerState(ref samplerDescription);

            var resourceSetDescription = new ResourceSetDescription(this.layout, this.constantBuffer, sampler);
            this.resourceSet = context.Factory.CreateResourceSet(ref resourceSetDescription);

            this.io = ImGui.GetIO();
            this.io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out int bytesPerPixel);

            this.io.Fonts.SetTexID(fontAtlasID);

            var fontTextureDescription = new TextureDescription()
            {
                Type = TextureType.Texture2D,
                Width = (uint)width,
                Height = (uint)height,
                Format = PixelFormat.R8G8B8A8_UNorm,
                Usage = ResourceUsage.Default,
                Depth = 1,
                Faces = 1,
                ArraySize = 1,
                MipLevels = 1,
                SampleCount = TextureSampleCount.None,
                CpuAccess = ResourceCpuAccess.Write,
                Flags = TextureFlags.ShaderResource,
            };

            this.fontTexture = context.Factory.CreateTexture(ref fontTextureDescription);
            context.UpdateTextureData(this.fontTexture, (IntPtr)pixels, (uint)(bytesPerPixel * width * height), 0);

            var fontResourceSetDescription = new ResourceSetDescription(this.textureLayout, this.fontTexture);
            this.fontResourceSet = context.Factory.CreateResourceSet(ref fontResourceSetDescription);

            this.io.Fonts.ClearTexData();

            this.scissors = new Rectangle[1];
        }

        public void Update(TimeSpan gameTime)
        {
            this.io.DisplaySize = new System.Numerics.Vector2(
                windowWidth / scaleFactor.X,
                windowHeight / scaleFactor.Y);

            this.io.DisplayFramebufferScale = scaleFactor;
            this.io.DeltaTime = 1f / 60f;

            ImGui.NewFrame();
            ImGui.ShowDemoWindow();
        }

        public void Render(CommandBuffer cb, FrameBuffer frameBuffer)
        {
            ImGui.Render();

            uint vertexOffset = 0;
            uint indexOffset = 0;

            ImDrawDataPtr drawData = ImGui.GetDrawData();

            if (drawData.CmdListsCount == 0)
            {
                return;
            }

            // Resize index and vertex buffers.
            int vertexBufferSize = drawData.TotalVtxCount * sizeof(ImDrawVert);
            if (vertexBufferSize > this.vertexBuffer.Description.SizeInBytes)
            {
                this.vertexBuffer.Dispose();
                uint nextSize = (uint)MathHelper.NextPowerOfTwo(vertexBufferSize);
                var vertexBufferDescription = new BufferDescription(
                    nextSize, 
                    BufferFlags.VertexBuffer, 
                    ResourceUsage.Dynamic, 
                    ResourceCpuAccess.Write);

                this.vertexBuffer = context.Factory.CreateBuffer(ref vertexBufferDescription);
                this.vertexBuffers[0] = this.vertexBuffer;
            }

            int indexBufferSize = drawData.TotalIdxCount * sizeof(ushort);
            if (indexBufferSize > this.indexBuffer.Description.SizeInBytes)
            {
                this.indexBuffer.Dispose();
                uint nextSize = (uint)MathHelper.NextPowerOfTwo(indexBufferSize);
                var indexBufferDescription = new BufferDescription(
                    nextSize, 
                    BufferFlags.IndexBuffer, 
                    ResourceUsage.Dynamic, 
                    ResourceCpuAccess.Write);

                this.indexBuffer = context.Factory.CreateBuffer(ref indexBufferDescription);
            }

            // Update index and vertex buffers.
            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ImDrawListPtr cmdList = drawData.CmdListsRange[i];

                cb.UpdateBufferData(
                    this.vertexBuffer,
                    cmdList.VtxBuffer.Data,
                    (uint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert)),
                    vertexOffset * (uint)sizeof(ImDrawVert));

                cb.UpdateBufferData(
                    this.indexBuffer,
                    cmdList.IdxBuffer.Data,
                    (uint)(cmdList.IdxBuffer.Size * sizeof(ushort)),
                    indexOffset * sizeof(ushort));

                vertexOffset += (uint)cmdList.VtxBuffer.Size;
                indexOffset += (uint)cmdList.IdxBuffer.Size;
            }

            // Set orthographics projection matrix
            Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(
                0f,
                this.io.DisplaySize.X,
                this.io.DisplaySize.Y,
                0.0f,
                -1.0f,
                1.0f);

            //mvp = Matrix4x4.Transpose(mvp);
            cb.UpdateBufferData(constantBuffer, ref mvp);

            RenderPassDescription renderPassDescription = new RenderPassDescription(frameBuffer, ClearValue.None);
            cb.BeginRenderPass(ref renderPassDescription);

            // Bind resources
            cb.SetGraphicsPipelineState(this.pipelineState);
            cb.SetVertexBuffers(this.vertexBuffers);
            cb.SetIndexBuffer(this.indexBuffer, IndexFormat.UInt16);
            cb.SetResourceSet(this.resourceSet);

            drawData.ScaleClipRects(this.io.DisplayFramebufferScale);

            // Render command lists
            vertexOffset = 0;
            indexOffset = 0;

            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ImDrawListPtr cmdList = drawData.CmdListsRange[i];
                for (int j = 0; j < cmdList.CmdBuffer.Size; j++)
                {
                    ImDrawCmdPtr cmd = cmdList.CmdBuffer[j];
                    
                    this.scissors[0] = new Rectangle(
                        (int)cmd.ClipRect.X,
                        (int)cmd.ClipRect.Y,
                        (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                        (int)(cmd.ClipRect.W - cmd.ClipRect.Y));
                    cb.SetScissorRectangles(this.scissors);

                    if (cmd.TextureId != IntPtr.Zero)
                    {
                        if (cmd.TextureId == fontAtlasID)
                        {
                            cb.SetResourceSet(this.fontResourceSet, 1);
                        }
                        else
                        {
                            cb.SetResourceSet(GetImageResourceSet(cmd.TextureId), 1);
                        }
                    }

                    cb.DrawIndexed(cmd.ElemCount, indexOffset, vertexOffset);

                    indexOffset += cmd.ElemCount;
                }

                vertexOffset += (uint)cmdList.VtxBuffer.Size;
            }

            cb.EndRenderPass();
        }

        public IntPtr GetOrCreateImGuiBinding(Texture texture)
        {
            if (!resourceByTexture.TryGetValue(texture, out ResourceSetInfo info))
            {
                var resourceSetDescriptionnew = new ResourceSetDescription(this.textureLayout, texture);
                var newResourceSet = context.Factory.CreateResourceSet(ref resourceSetDescriptionnew);
                info = new ResourceSetInfo(GetNextImGuiBindingID(), newResourceSet);

                resourceByTexture.Add(texture, info);
                resourceById.Add(info.ImGuiBinding, info);
            }

            return info.ImGuiBinding;

        }

        public void RemoveImGuiBinding(Texture texture)
        {
            if (resourceByTexture.TryGetValue(texture, out ResourceSetInfo info))
            {
                resourceByTexture.Remove(texture);
                resourceById.Remove(info.ImGuiBinding);
                info.ResourceSet.Dispose();
            }
        }

        private IntPtr GetNextImGuiBindingID()
        {
            int newID = lastAssignedID++;
            return (IntPtr)newID;
        }

        private ResourceSet GetImageResourceSet(IntPtr textureId)
        {
            if (resourceById.TryGetValue(textureId, out ResourceSetInfo rsi))
            {
                return rsi.ResourceSet;
            }

            return null;
        }

        public void Dispose()
        {
            ImGui.DestroyContext();
            vertexBuffer.Dispose();
            indexBuffer.Dispose();
            constantBuffer.Dispose();
            //vertexShader.Dispose();
            //pixelShader.Dispose();
            layout.Dispose();
            textureLayout.Dispose();
            resourceSet.Dispose();
            fontResourceSet.Dispose();
        }
    }
}
