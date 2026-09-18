using UnrealBuildTool;
public class NSAI_NaniteAssembly_Integration : ModuleRules
{
    public NSAI_NaniteAssembly_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI72_NA";
        string[] RequiredPlugins = new string[] { "NaniteAssemblyEditorUtils" };
        bool bWithIntegration = !(Target.Version.MajorVersion == 5 && Target.Version.MinorVersion < 7)
            && NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_NANITEASSEMBLY_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDefinitions.Add("WITH_NSAI_NANITE_ASSEMBLY=" + (bWithIntegration ? "1" : "0"));
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Projects", "NeoStackAI" });
        if (!bWithIntegration) return;
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PublicDependencyModuleNames.AddRange(new string[] { "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new string[] { "UnrealEd", "NaniteAssemblyEditorUtils", "RenderCore", "AssetRegistry" });
    }
}
