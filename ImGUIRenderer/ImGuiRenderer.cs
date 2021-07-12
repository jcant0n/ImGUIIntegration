using ImGuiNET;
using System;
using System.Collections.Generic;
using WaveEngine.Common.Graphics;
using Buffer = WaveEngine.Common.Graphics.Buffer;
using System.Runtime.CompilerServices;
using WaveEngine.Mathematics;
using WaveEngine.Common.Input.Mouse;
using WaveEngine.Common.Input;
using WaveEngine.Common.Input.Keyboard;

namespace VisualTests.LowLevel.Tests
{
    public unsafe class ImGuiRenderer : IDisposable
    {
        private GraphicsContext context;
        private Surface surface;
        private Buffer[] vertexBuffers;
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
        private ImGuiIOPtr io;

        private int windowWidth;
        private int windowHeight;
        private System.Numerics.Vector2 scaleFactor = System.Numerics.Vector2.One;

        private IntPtr fontAtlasID = (IntPtr)1;
        private int lastAssignedID = 100;

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

        public ImGuiRenderer(GraphicsContext context, FrameBuffer frameBuffer, Surface surface, ShaderDescription vertexShaderDescription, ShaderDescription pixelShaderDescription)
        {
            this.context = context;
            this.surface = surface;

            IntPtr imGuiContext = ImGui.CreateContext();
            ImGui.SetCurrentContext(imGuiContext);

            ImGui.GetIO().Fonts.AddFontDefault();

            // Create native resources
            var vertexShader = context.Factory.CreateShader(ref vertexShaderDescription);
            var pixelShader = context.Factory.CreateShader(ref pixelShaderDescription);

            var vertexBufferDescription = new BufferDescription(
                10000,
                BufferFlags.VertexBuffer,
                ResourceUsage.Default,
                ResourceCpuAccess.None);

            this.vertexBuffers = new Buffer[1];
            this.vertexBuffers[0] = context.Factory.CreateBuffer(ref vertexBufferDescription);

            var indexBufferDescription = new BufferDescription(
                2000,
                BufferFlags.IndexBuffer,
                ResourceUsage.Default,
                ResourceCpuAccess.None);

            this.indexBuffer = context.Factory.CreateBuffer(ref indexBufferDescription);

            var vertexLayouts = new InputLayouts()
                  .Add(new LayoutDescription()
                              .Add(new ElementDescription(ElementFormat.Float2, ElementSemanticType.Position))
                              .Add(new ElementDescription(ElementFormat.Float2, ElementSemanticType.TexCoord))
                              .Add(new ElementDescription(ElementFormat.UByte4Normalized, ElementSemanticType.Color)));

            var layoutDescription = new ResourceLayoutDescription(
                    new LayoutElementDescription(0, ResourceType.ConstantBuffer, ShaderStages.Vertex));

            this.layout = context.Factory.CreateResourceLayout(ref layoutDescription);

            var textureLayoutDescription = new ResourceLayoutDescription(
                    new LayoutElementDescription(0, ResourceType.Texture, ShaderStages.Pixel),
                    new LayoutElementDescription(0, ResourceType.Sampler, ShaderStages.Pixel));

            this.textureLayout = context.Factory.CreateResourceLayout(ref textureLayoutDescription);

            var blendState = BlendStates.AlphaBlend;
            blendState.AlphaToCoverageEnable = false;
            blendState.RenderTarget0.BlendEnable = true;
            blendState.RenderTarget0.SourceBlendColor = Blend.SourceAlpha;
            blendState.RenderTarget0.DestinationBlendColor = Blend.InverseSourceAlpha;
            blendState.RenderTarget0.BlendOperationColor = BlendOperation.Add;
            blendState.RenderTarget0.SourceBlendAlpha = Blend.SourceAlpha;
            blendState.RenderTarget0.DestinationBlendAlpha = Blend.Zero;
            blendState.RenderTarget0.BlendOperationAlpha = BlendOperation.Add;

            var rasterizerState = RasterizerStates.None;
            rasterizerState.FillMode = FillMode.Solid;
            rasterizerState.CullMode = CullMode.None;
            rasterizerState.ScissorEnable = true;
            rasterizerState.DepthClipEnable = true;

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
                    RasterizerState = rasterizerState,
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

            var resourceSetDescription = new ResourceSetDescription(this.layout, this.constantBuffer);
            this.resourceSet = context.Factory.CreateResourceSet(ref resourceSetDescription);

            this.io = ImGui.GetIO();
            RecreateFontTexture(context);

            // Keyboard mapping. ImGui will use those indices to peek into the io.KeyDown[] array that we will update during the application lifetime.
            io.KeyMap[(int)ImGuiKey.Tab] = (int)Keys.Tab; 
            io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)Keys.Left;
            io.KeyMap[(int)ImGuiKey.RightArrow] = (int)Keys.Right;
            io.KeyMap[(int)ImGuiKey.UpArrow] = (int)Keys.Up;
            io.KeyMap[(int)ImGuiKey.DownArrow] = (int)Keys.Down;
            io.KeyMap[(int)ImGuiKey.PageUp] = (int)Keys.PageUp;
            io.KeyMap[(int)ImGuiKey.PageDown] = (int)Keys.PageDown;
            io.KeyMap[(int)ImGuiKey.Home] = (int)Keys.Home;
            io.KeyMap[(int)ImGuiKey.End] = (int)Keys.End;
            io.KeyMap[(int)ImGuiKey.Delete] = (int)Keys.Delete;
            io.KeyMap[(int)ImGuiKey.Backspace] = (int)Keys.Back;
            io.KeyMap[(int)ImGuiKey.Enter] = (int)Keys.Enter;
            io.KeyMap[(int)ImGuiKey.Escape] = (int)Keys.Escape;
            io.KeyMap[(int)ImGuiKey.A] = (int)Keys.A;
            io.KeyMap[(int)ImGuiKey.C] = (int)Keys.C;
            io.KeyMap[(int)ImGuiKey.V] = (int)Keys.V;
            io.KeyMap[(int)ImGuiKey.X] = (int)Keys.X;
            io.KeyMap[(int)ImGuiKey.Y] = (int)Keys.Y;
            io.KeyMap[(int)ImGuiKey.Z] = (int)Keys.Z;

            // Register input events
            var mouseDispatcher = this.surface.MouseDispatcher;
            mouseDispatcher.MouseButtonDown += (s, e) =>
            {
                switch (e.Button)
                {
                    case MouseButtons.Left:
                        io.MouseDown[0] = true;
                        break;
                    case MouseButtons.Right:
                        io.MouseDown[1] = true;
                        break;
                    case MouseButtons.Middle:
                        io.MouseDown[2] = true;
                        break;
                }
            };

            mouseDispatcher.MouseButtonUp += (s, e) =>
            {
                switch (e.Button)
                {
                    case MouseButtons.Left:
                        io.MouseDown[0] = false;
                        break;
                    case MouseButtons.Right:
                        io.MouseDown[1] = false;
                        break;
                    case MouseButtons.Middle:
                        io.MouseDown[2] = false;
                        break;
                    default:
                        break;
                }
            };

            mouseDispatcher.MouseMove += (s, e) =>
            {
                io.MousePos.X = e.Position.X;
                io.MousePos.Y = e.Position.Y;
            };

            mouseDispatcher.MouseScroll += (s, e) =>
            {
                io.MouseWheel = e.Delta.Y;
            };

            var keyboardDispatcher = this.surface.KeyboardDispatcher;
            keyboardDispatcher.KeyDown += (s, e) =>
            {
                io.KeysDown[((int)e.Key)] = true;
            };

            keyboardDispatcher.KeyUp += (s, e) =>
            {
                io.KeysDown[(int)e.Key] = false;
            };

            keyboardDispatcher.KeyChar += (s, e) =>
            {
                io.AddInputCharacter(e.Character);
            };
        }

        public void WindowResized(int width, int height)
        {
            this.windowWidth = width;
            this.windowHeight = height;
        }

        private void RecreateFontTexture(GraphicsContext context)
        {
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

            SamplerStateDescription samplerDescription = new SamplerStateDescription()
            {
                Filter = TextureFilter.MinLinear_MagLinear_MipLinear,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                MipLODBias = 0f,
                ComparisonFunc = ComparisonFunction.Always,
                MinLOD = 0f,
                MaxLOD = 0f,
            };

            var sampler = context.Factory.CreateSamplerState(ref samplerDescription);

            this.fontResourceSet?.Dispose();
            var fontResourceSetDescription = new ResourceSetDescription(this.textureLayout, this.fontTexture, sampler);
            this.fontResourceSet = context.Factory.CreateResourceSet(ref fontResourceSetDescription);

            this.io.Fonts.ClearTexData();
        }

        public void Update(TimeSpan gameTime)
        {
            this.io.DisplaySize = new System.Numerics.Vector2(
                            windowWidth / scaleFactor.X,
                            windowHeight / scaleFactor.Y);

            this.io.DisplayFramebufferScale = scaleFactor;
            this.io.DeltaTime = (float)gameTime.TotalSeconds;

            // Read keyboard modifiers input
            var keyboardDispatcher = this.surface.KeyboardDispatcher;
            io.KeyCtrl = keyboardDispatcher.IsKeyDown(Keys.LeftControl);
            io.KeyShift = keyboardDispatcher.IsKeyDown(Keys.LeftShift);
            io.KeyAlt = keyboardDispatcher.IsKeyDown(Keys.LeftAlt);

            ImGui.NewFrame();
        }

        public void Render(CommandBuffer cb, FrameBuffer frameBuffer)
        {
            ImGui.Render();

            uint vertexOffsetInVertices = 0;
            uint indexOffsetInElements = 0;

            ImDrawDataPtr drawData = ImGui.GetDrawData();

            if (drawData.CmdListsCount == 0)
            {
                return;
            }

            // Resize index and vertex buffers.
            int vertexBufferSize = drawData.TotalVtxCount * sizeof(ImDrawVert);
            if (vertexBufferSize > this.vertexBuffers[0].Description.SizeInBytes)
            {
                this.vertexBuffers[0].Dispose();
                uint nextSize = (uint)MathHelper.NextPowerOfTwo(vertexBufferSize);
                var vertexBufferDescription = new BufferDescription(
                    (uint)(nextSize),
                    BufferFlags.VertexBuffer,
                    ResourceUsage.Default,
                    ResourceCpuAccess.None);

                this.vertexBuffers[0] = context.Factory.CreateBuffer(ref vertexBufferDescription);
            }

            int indexBufferSize = drawData.TotalIdxCount * sizeof(ushort);
            if (indexBufferSize > this.indexBuffer.Description.SizeInBytes)
            {
                this.indexBuffer.Dispose();
                uint nextSize = (uint)MathHelper.NextPowerOfTwo(indexBufferSize);
                var indexBufferDescription = new BufferDescription(
                    (uint)(nextSize),
                    BufferFlags.IndexBuffer,
                    ResourceUsage.Default,
                    ResourceCpuAccess.None);

                this.indexBuffer = context.Factory.CreateBuffer(ref indexBufferDescription);
            }

            // Update index and vertex buffers.
            for (int i = 0; i < drawData.CmdListsCount; i++)
            {
                ImDrawListPtr cmdList = drawData.CmdListsRange[i];

                cb.UpdateBufferData(
                    this.vertexBuffers[0],
                    cmdList.VtxBuffer.Data,
                    (uint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert)),
                    vertexOffsetInVertices * (uint)sizeof(ImDrawVert));

                cb.UpdateBufferData(
                    this.indexBuffer,
                    cmdList.IdxBuffer.Data,
                    (uint)(cmdList.IdxBuffer.Size * sizeof(ushort)),
                    indexOffsetInElements * sizeof(ushort));

                vertexOffsetInVertices += (uint)cmdList.VtxBuffer.Size;
                indexOffsetInElements += (uint)cmdList.IdxBuffer.Size;
            }

            // Set orthographics projection matrix
            Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(
                0f,
                this.io.DisplaySize.X,
                this.io.DisplaySize.Y,
                0.0f,
                -1.0f,
                1.0f);

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
            uint vtx_offset = 0;
            uint idx_offset = 0;

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawListPtr cmdList = drawData.CmdListsRange[n];
                for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
                {
                    ImDrawCmdPtr cmd = cmdList.CmdBuffer[i];
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

                    var scissors = new Rectangle[1]
                    {
                        new Rectangle(
                        (int)cmd.ClipRect.X,
                        (int)cmd.ClipRect.Y,
                        (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                        (int)(cmd.ClipRect.W - cmd.ClipRect.Y))
                    };

                    cb.SetScissorRectangles(scissors);

                    cb.DrawIndexedInstanced(cmd.ElemCount, 1, idx_offset, vtx_offset, 0);

                    idx_offset += cmd.ElemCount;
                }

                vtx_offset += (uint)cmdList.VtxBuffer.Size;
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
            this.vertexBuffers[0].Dispose();
            this.vertexBuffers = null;
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
