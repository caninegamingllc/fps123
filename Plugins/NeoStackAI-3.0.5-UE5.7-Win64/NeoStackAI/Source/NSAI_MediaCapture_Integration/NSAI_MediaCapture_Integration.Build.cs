using UnrealBuildTool;
using System.IO;

public class NSAI_MediaCapture_Integration : ModuleRules
{
    public NSAI_MediaCapture_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI73_MCAP";
        // MediaIOFramework ships UMediaOutput / UMediaCapture / UFileMediaOutput (Engine/Plugins/Media/MediaIOFramework,
        // EnabledByDefault=false; pulls OpenColorIO per MediaIOFramework.uplugin:38-43).
        string[] RequiredPlugins = new string[] { "MediaIOFramework" };
        bool bWithIntegration = NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_MEDIACAPTURE_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Projects", "NeoStackAI" });
        if (!bWithIntegration)
        {
            return;
        }
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        // Lua and sol2 are exposed by the NeoStack AI core module as public includes.
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new string[] {
            "NeoStackAI",
            "UnrealEd",
            "MediaIOCore",
            "MediaAssets",
            "ImageWriteQueue",
            "RHI",
            "RenderCore",
        });
    }
}
