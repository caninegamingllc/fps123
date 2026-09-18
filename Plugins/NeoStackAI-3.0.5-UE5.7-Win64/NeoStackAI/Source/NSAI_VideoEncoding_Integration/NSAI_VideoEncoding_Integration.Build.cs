using UnrealBuildTool;
using System.IO;

public class NSAI_VideoEncoding_Integration : ModuleRules
{
    public NSAI_VideoEncoding_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI76_VENC";
        string[] RequiredPlugins = new string[] { "AVCodecsCore" };
        bool bWithIntegration = NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_VIDEOENCODING_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Projects", "NeoStackAI" });
        if (!bWithIntegration)
        {
            return;
        }
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new string[] {
            "NeoStackAI",
            "UnrealEd",
            "RHI",
            "RenderCore",
            "AVCodecsCore",
            "AVCodecsCoreRHI",
        });
    }
}
