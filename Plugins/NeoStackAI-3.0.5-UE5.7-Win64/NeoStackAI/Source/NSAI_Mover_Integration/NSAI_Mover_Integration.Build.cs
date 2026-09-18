using UnrealBuildTool;
using System.IO;

public class NSAI_Mover_Integration : ModuleRules
{
    public NSAI_Mover_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI74_MV";
        // The Mover plugin (Engine/Plugins/Experimental/Mover) is Experimental and
        // not EnabledByDefault; the build gate only checks that its descriptor is
        // present so the integration compiles to an empty stub otherwise. There is
        // no engine-version floor: 5.5/5.6 build with the 5.7+/5.8-only paths gated
        // in source with UE_VERSION_OLDER_THAN.
        string[] RequiredPlugins = new string[] { "Mover" };
        bool bWithIntegration = NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_MOVER_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
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
            // Mover's public dependencies (NetworkPrediction, MotionWarping, Water,
            // GameplayTags, NavigationSystem) propagate through the Mover module
            // itself (Mover.Build.cs:12-24); GameplayTags is listed explicitly because
            // the tag verbs call FGameplayTag directly.
            "Mover",
            "GameplayTags",
        });
    }
}
