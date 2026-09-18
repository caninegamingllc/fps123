using UnrealBuildTool;
using System.IO;

public class NSAI_UAFLayering_Integration : ModuleRules
{
    public NSAI_UAFLayering_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI70_UAF";
        // UAFLayering ships only with UE 5.8 (Engine/Plugins/Experimental/UAF/UAFLayering).
        // 5.7 has UAF/UAFAnimGraph but no layering plugin; 5.5/5.6 use the AnimNext* names.
        string[] RequiredPlugins = new string[] { "UAFLayering" };
        bool bSupportedVersion = Target.Version.MajorVersion > 5 ||
            (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 8);
        bool bWithIntegration = bSupportedVersion &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_UAFLAYERING_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
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
            "AssetTools",
            "AssetRegistry",
            "RigVM",
            // UUAFComponent, UUAFRigVMAsset, FAssetDataFactory, FUAFWeakSystemReference (Public headers only).
            "UAF",
            // UE::UAF::UncookedOnly::Compilation::RequestAssetCompilation (Public/UAFCompilationScope.h).
            "UAFUncookedOnly",
            // UUAFLayeringUtils (Public/UAFLayeringUtils.h). Every other layering type is
            // reached by reflection so no engine Internal include path is needed.
            "UAFLayering",
        });
    }
}
