// Copyright © Wave Engine S.L. All rights reserved. Use is subject to license terms.

using System;
using System.IO;
using System.Threading.Tasks;
using WaveEngine.Common.Graphics;
using WaveEngine.Common.IO;

namespace VisualTests.Runners.Common
{
    public static class TestHelpers
    {
        public static bool IsDirectXBackend(this GraphicsContext graphicsContext)
        {
            var backendType = graphicsContext.BackendType;
            return backendType == GraphicsBackend.DirectX11 || backendType == GraphicsBackend.DirectX12;
        }

        public static Task<ShaderDescription> ReadAndCompileShader(this AssetsDirectoryBase assetsDirectory, GraphicsContext graphicsContext, string filename, ShaderStages stage, string entryPoint)
        {
            var compilerParams = CompilerParameters.Default;
#if DEBUG
            compilerParams.CompilationMode = CompilationMode.Debug;
#endif

            return ReadAndCompileShader(assetsDirectory, graphicsContext, filename, stage, entryPoint, compilerParams);
        }

        public static async Task<ShaderDescription> ReadAndCompileShader(this AssetsDirectoryBase assetsDirectory, GraphicsContext graphicsContext, string filename, ShaderStages stage, string entryPoint, CompilerParameters compileParameters)
        {
            GraphicsBackend backend = graphicsContext.BackendType;

            string source;
            byte[] bytecode = null;

            switch (backend)
            {
                case GraphicsBackend.DirectX11:
                case GraphicsBackend.DirectX12:

                    source = await assetsDirectory.ReadAsStringAsync($"Shaders/HLSL/{filename}.fx");
                    bytecode = graphicsContext.ShaderCompile(source, entryPoint, stage, compileParameters).ByteCode;

                    break;
                case GraphicsBackend.OpenGL:

                    source = await assetsDirectory.ReadAsStringAsync($"Shaders/GLSL/{filename}.glsl");
                    bytecode = graphicsContext.ShaderCompile(source, entryPoint, stage, compileParameters).ByteCode;

                    break;
                case GraphicsBackend.OpenGLES:
                case GraphicsBackend.WebGL1:
                case GraphicsBackend.WebGL2:

                    source = await assetsDirectory.ReadAsStringAsync($"Shaders/ESSL/{filename}.essl");
                    bytecode = graphicsContext.ShaderCompile(source, entryPoint, stage, compileParameters).ByteCode;

                    break;
                case GraphicsBackend.Metal:

                    source = await assetsDirectory.ReadAsStringAsync($"Shaders/MSL/{filename}.msl");
                    bytecode = graphicsContext.ShaderCompile(source, entryPoint, stage, compileParameters).ByteCode;

                    break;
                case GraphicsBackend.Vulkan:

                    using (var stream = assetsDirectory.Open($"Shaders/VK/{filename}.spirv"))
                    using (var memstream = new MemoryStream())
                    {
                        stream.CopyTo(memstream);
                        bytecode = memstream.ToArray();
                    }

                    break;
                default:
                    throw new Exception($"Backend not found {backend}");
            }

            ShaderDescription description = new ShaderDescription(stage, entryPoint, bytecode);

            return description;
        }

        public static Task<string> ReadShaderSource(this AssetsDirectoryBase assetsDirectory, GraphicsContext graphicsContext, string hlslFileName, string translateFileName, string root = null)
        {
            var backendType = graphicsContext.BackendType;

            if (backendType == GraphicsBackend.DirectX11 ||
                backendType == GraphicsBackend.DirectX12)
            {
                return assetsDirectory.ReadAsStringAsync($"{root}Shaders/HLSL/{hlslFileName}.fx");
            }
            else if (backendType == GraphicsBackend.OpenGL)
            {
                return assetsDirectory.ReadAsStringAsync($"{root}Shaders/GLSL/{translateFileName}.glsl");
            }
            else if (backendType == GraphicsBackend.OpenGLES)
            {
                return assetsDirectory.ReadAsStringAsync($"{root}Shaders/GLSL_ES/{translateFileName}.essl");
            }
            else if (backendType == GraphicsBackend.Metal)
            {
                return assetsDirectory.ReadAsStringAsync($"Shaders/MSL/{translateFileName}.msl");
            }
            else
            {
                throw new InvalidOperationException($"Unsuported backend type: {backendType}");
            }
        }
    }
}
