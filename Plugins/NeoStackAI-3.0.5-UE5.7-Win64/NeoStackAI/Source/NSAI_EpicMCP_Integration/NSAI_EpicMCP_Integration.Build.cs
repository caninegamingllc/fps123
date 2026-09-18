using UnrealBuildTool;
using System.IO;

public class NSAI_EpicMCP_Integration : ModuleRules
{
    public NSAI_EpicMCP_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI66_EM";
        // Epic's ModelContextProtocol plugin (Experimental, NoRedist) first ships in UE 5.8 and
        // hard-depends on ToolsetRegistry (ModelContextProtocol.uplugin:48-57). The publish
        // direction needs a real link: IModelContextProtocolTool and
        // IModelContextProtocolResourceProvider are non-UObject interfaces, so the reflection
        // route used by the core ue_toolsets bridge cannot reach them. The link lives only in
        // this optional folded module, which compiles to an empty stub when the descriptor is
        // absent or the engine is older than 5.8.
        string[] RequiredPlugins = new string[] { "ModelContextProtocol", "ToolsetRegistry" };
        bool bSupportedVersion = Target.Version.MajorVersion > 5 ||
            (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 8);
        bool bWithIntegration = bSupportedVersion &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_EPICMCP_INTEGRATION=" + (bWithIntegration ? "1" : "0"));

        // UNSAIEpicMCPSettings is reflected in both branches so the settings surface exists
        // (and reports "unsupported") on 5.5-5.7 too. UHT therefore always runs on this module.
        // The stub branch must only include editor-free NeoStackAI headers (no UnrealEd/BlueprintGraph).
        PublicDependencyModuleNames.AddRange(new string[] {
            "Core", "CoreUObject", "Engine", "DeveloperSettings", "Projects", "Json", "NeoStackAI"
        });
        if (!bWithIntegration)
        {
            return;
        }
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        // Lua and sol2 are exposed by the NeoStack AI core module as public includes.
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PrivateDependencyModuleNames.AddRange(new string[] {
            "UnrealEd",
            "BlueprintGraph",
            "AssetRegistry",
            "JsonUtilities",
            "HTTP",
            "HTTPServer",
            "ModelContextProtocol",
            "ModelContextProtocolEngine",
        });
    }
}
