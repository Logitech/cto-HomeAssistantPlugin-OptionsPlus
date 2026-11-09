using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Loupedeck.HomeAssistantPlugin.Services;

using Xunit;

namespace Loupedeck.HomeAssistantPlugin.Tests.Native;

public sealed class NativeRenderingTests
{
    [NativeMacFact]
    [Trait("Category", "Native")]
    public void SdkGraphics_OnArm64_LoadsImagesAndRendersFallbacks()
    {
        Assert.True(OperatingSystem.IsMacOS());
        Assert.Equal(Architecture.Arm64, RuntimeInformation.ProcessArchitecture);

        var provider = new PluginResourceProvider(typeof(HomeAssistantPlugin).Assembly);
        Assert.NotNull(provider.LoadImage("light_bulb_icon.svg"));
        Assert.NotNull(provider.LoadImage("online_status_icon.png"));
        var icons = new IconService(new Dictionary<String, String>
        {
            ["svg"] = "light_bulb_icon.svg",
            ["png"] = "online_status_icon.png"
        });
        Assert.NotNull(icons.Get("svg"));
        Assert.NotNull(icons.Get("png"));
        Assert.NotNull(icons.Get("missing"));

        using var bitmap = new BitmapBuilder(80, 80);
        bitmap.DrawText("?", fontSize: 32, color: new BitmapColor(255, 255, 255));
        Assert.NotNull(TilePainter.IconOrGlyph(bitmap, null, "?"));
    }

    private sealed class NativeMacFactAttribute : FactAttribute
    {
        public NativeMacFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RUN_NATIVE_SMOKE") != "1")
            {
                this.Skip = "Opt in on an arm64 Mac with RUN_NATIVE_SMOKE=1 and -p:EnableNativeSmokeTests=true.";
            }
        }
    }
}
