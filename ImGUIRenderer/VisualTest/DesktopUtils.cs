// Copyright © Wave Engine S.L. All rights reserved. Use is subject to license terms.

using System;
using System.Runtime.InteropServices;
using WaveEngine.Common;
using WaveEngine.Common.Graphics;
using WaveEngine.Platform;
using static WaveEngine.Common.Graphics.SurfaceInfo;

namespace VisualTests.Runners.Common
{
    public static class DesktopUtils
    {
        public static void Execute<T>(string[] args = null, uint width = 1280, uint height = 720, object surfaceObject = null)
            where T : VisualTestDefinition, new()
        {
            using (var test = new T())
            {
                // Read arguments
                SurfaceTypes prefferedSurface = SurfaceTypes.Forms;
                if (args?.Length > 0)
                {
                    bool parsed = Enum.TryParse(args[0], out prefferedSurface);
                }

                GraphicsBackend prefferedBackend = GraphicsBackend.DirectX11;
                if (args?.Length > 1)
                {
                    bool parsed = Enum.TryParse(args[1], out prefferedBackend);
                }

                string prefferedAudio = null;
                if (args?.Length > 2)
                {
                    if (args[2] == "XAudio2" || args[2] == "OpenAL")
                    {
                        prefferedAudio = args[2];
                    }
                }

                test.Initialize(prefferedSurface, prefferedAudio);

                // Create Window
                string windowsTitle = $"{typeof(T).Name}";
                var windowSystem = test.WindowSystem;

                Surface surface;

                if (DeviceInfo.PlatformType == PlatformType.Web)
                {
                    surface = windowSystem.CreateSurface(surfaceObject);
                }
                else
                {
                    var window = windowSystem.CreateWindow(windowsTitle, width, height);
                    test.FPSUpdateCallback = (fpsString) =>
                    {
                        window.Title = $"{windowsTitle}  {fpsString}";
                    };
                    surface = window;
                }

                test.Surface = surface;

                // Managers
                var swapChainDescriptor = test.CreateSwapChainDescription(surface.Width, surface.Height);
                swapChainDescriptor.SurfaceInfo = surface.SurfaceInfo;

                var graphicsContext = test.CreateGraphicsContext(swapChainDescriptor, prefferedBackend);
                windowsTitle = $"{windowsTitle} [{prefferedSurface.ToString()}] [{prefferedAudio}] [{graphicsContext.BackendType}]";

                test.Run();
            }
        }
    }
}
