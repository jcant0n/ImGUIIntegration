// Copyright © Wave Engine S.L. All rights reserved. Use is subject to license terms.

using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using VisualTests.Runners.Common;
using WaveEngine.Common.Graphics;
using WaveEngine.Common.Input.Mouse;
using WaveEngine.Mathematics;
using Buffer = WaveEngine.Common.Graphics.Buffer;

namespace VisualTests.LowLevel.Tests
{
    public class WaveMain : VisualTestDefinition
    {
        private Vector4[] vertexData = new Vector4[]
        {
            // TriangleList
            new Vector4(0f, 0.5f, 0.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
            new Vector4(0.5f, -0.5f, 0.0f, 1.0f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f),
            new Vector4(-0.5f, -0.5f, 0.0f, 1.0f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
        };

        private Matrix4x4 view;
        private Matrix4x4 proj;

        private Buffer constantBuffer;

        private Viewport[] viewports;
        private Rectangle[] scissors;
        private CommandQueue commandQueue;
        private GraphicsPipelineState pipelineState;
        private ResourceSet resourceSet;
        private Buffer[] vertexBuffers;
        private float time = 0.0f;

        private ImGuiRenderer uiRenderer;

        public WaveMain()
            : base("DrawTriangle")
        {
        }

        protected override void OnResized(uint width, uint height)
        {
            this.viewports[0] = new Viewport(0, 0, width, height);
        }

        protected override async void InternalLoad()
        {
            // Compile Vertex and Pixel shaders
            var vertexShaderDescription = await this.assetsDirectory.ReadAndCompileShader(this.graphicsContext, "VertexShader", ShaderStages.Vertex, "VS");
            var pixelShaderDescription = await this.assetsDirectory.ReadAndCompileShader(this.graphicsContext, "FragmentShader", ShaderStages.Pixel, "PS");

            var vertexShader = this.graphicsContext.Factory.CreateShader(ref vertexShaderDescription);
            var pixelShader = this.graphicsContext.Factory.CreateShader(ref pixelShaderDescription);

            var vertexBufferDescription = new BufferDescription((uint)Unsafe.SizeOf<Vector4>() * (uint)this.vertexData.Length, BufferFlags.VertexBuffer, ResourceUsage.Default);
            var vertexBuffer = this.graphicsContext.Factory.CreateBuffer(this.vertexData, ref vertexBufferDescription);

            this.view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), new Vector3(0, 0, 0), Vector3.UnitY);
            this.proj = Matrix4x4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, (float)this.frameBuffer.Width / (float)this.frameBuffer.Height, 0.1f, 100f);

            // Constant Buffer
            var constantBufferDescription = new BufferDescription((uint)Unsafe.SizeOf<Matrix4x4>(), BufferFlags.ConstantBuffer, ResourceUsage.Default);
            this.constantBuffer = this.graphicsContext.Factory.CreateBuffer(ref constantBufferDescription);

            // Prepare Pipeline
            var vertexLayouts = new InputLayouts()
                  .Add(new LayoutDescription()
                              .Add(new ElementDescription(ElementFormat.Float4, ElementSemanticType.Position))
                              .Add(new ElementDescription(ElementFormat.Float4, ElementSemanticType.Color)));

            ResourceLayoutDescription layoutDescription = new ResourceLayoutDescription(
                    new LayoutElementDescription(0, ResourceType.ConstantBuffer, ShaderStages.Vertex));

            ResourceLayout resourcesLayout = this.graphicsContext.Factory.CreateResourceLayout(ref layoutDescription);

            ResourceSetDescription resourceSetDescription = new ResourceSetDescription(resourcesLayout, this.constantBuffer);
            this.resourceSet = this.graphicsContext.Factory.CreateResourceSet(ref resourceSetDescription);

            var pipelineDescription = new GraphicsPipelineDescription
            {
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                InputLayouts = vertexLayouts,
                ResourceLayouts = new[] { resourcesLayout },
                Shaders = new GraphicsShaderStateDescription()
                {
                    VertexShader = vertexShader,
                    PixelShader = pixelShader,
                },
                RenderStates = new RenderStateDescription()
                {
                    RasterizerState = RasterizerStates.None,
                    BlendState = BlendStates.Opaque,
                    DepthStencilState = DepthStencilStates.ReadWrite,
                },
                Outputs = this.frameBuffer.OutputDescription,
            };

            this.pipelineState = this.graphicsContext.Factory.CreateGraphicsPipeline(ref pipelineDescription);
            this.commandQueue = this.graphicsContext.Factory.CreateCommandQueue();

            var swapChainDescription = this.swapChain?.SwapChainDescription;
            var width = swapChainDescription.HasValue ? swapChainDescription.Value.Width : this.surface.Width;
            var height = swapChainDescription.HasValue ? swapChainDescription.Value.Height : this.surface.Height;

            this.viewports = new Viewport[1];
            this.viewports[0] = new Viewport(0, 0, width, height);
            this.scissors = new Rectangle[1];
            this.scissors[0] = new Rectangle(0, 0, (int)width, (int)height);

            this.vertexBuffers = new Buffer[1];
            this.vertexBuffers[0] = vertexBuffer;

            var imGuiVSDescription = await this.assetsDirectory.ReadAndCompileShader(this.graphicsContext, "ImGui/imgui-vertex", ShaderStages.Vertex, "VS");
            var imGuiPSDescription = await this.assetsDirectory.ReadAndCompileShader(this.graphicsContext, "ImGui/imgui-frag", ShaderStages.Pixel, "PS");


            uiRenderer = new ImGuiRenderer(this.graphicsContext, this.frameBuffer, imGuiVSDescription, imGuiPSDescription);

            this.MarkAsLoaded();
        }

        protected override void InternalDrawCallback(TimeSpan gameTime)
        {
            // Update
            this.time += (float)gameTime.TotalSeconds;
            //this.time += 1f / 60f;
            this.view = Matrix4x4.CreateRotationY(this.time * 0.5f) * Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), new Vector3(0, 0, 0), Vector3.Up);
            var viewProj = Matrix4x4.Multiply(this.view, this.proj);

            this.surface.MouseDispatcher.DispatchEvents();
            this.surface.KeyboardDispatcher.DispatchEvents();

            // Draw
            var commandBuffer = this.commandQueue.CommandBuffer();

            commandBuffer.Begin();

            commandBuffer.UpdateBufferData(this.constantBuffer, ref viewProj);

            RenderPassDescription renderPassDescription = new RenderPassDescription(this.frameBuffer, ClearValue.Default);
            commandBuffer.BeginRenderPass(ref renderPassDescription);

            commandBuffer.SetViewports(this.viewports);
            commandBuffer.SetScissorRectangles(this.scissors);
            commandBuffer.SetGraphicsPipelineState(this.pipelineState);
            commandBuffer.SetResourceSet(this.resourceSet);
            commandBuffer.SetVertexBuffers(this.vertexBuffers);

            commandBuffer.Draw((uint)this.vertexData.Length / 2);
            commandBuffer.EndRenderPass();

            this.uiRenderer.Update(gameTime);

            uiRenderer.Render(commandBuffer, this.frameBuffer);

            commandBuffer.End();

            commandBuffer.Commit();

            //var commandBufferGUI = this.commandQueue.CommandBuffer();
            //commandBuffer.Begin();


            //commandBufferGUI.End();
            //commandBufferGUI.Commit();

            this.commandQueue.Submit();
            this.commandQueue.WaitIdle();
        }
    }
}
