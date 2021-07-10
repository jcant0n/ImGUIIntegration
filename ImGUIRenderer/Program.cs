using System;
using VisualTests.LowLevel.Tests;
using VisualTests.Runners.Common;

namespace ImGUIRenderer
{
    class Program
    {
        static void Main(string[] args)
        {
            object nativeSurface = null;

            DesktopUtils.Execute<WaveMain>(args, surfaceObject: nativeSurface);                       // || Vulkan || Metal  || DX11  | DX12  | Vulkan  | OpenGL || WebGL
        }
    }
}
