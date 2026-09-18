using UnrealBuildTool;
using System.IO;

public class NSAI_DynamicWind_Integration : ModuleRules
{
    public NSAI_DynamicWind_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI69_DW";
        string[] RequiredPlugins = new string[] { "DynamicWind" };
        // Engine/Plugins/Experimental/DynamicWind ships from UE 5.7 (present in the
        // UE_5.7 and UE_5.8 installs, absent from 5.6 and 5.5).
        bool bSupportedVersion = Target.Version.MajorVersion > 5 ||
            (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 7);
        bool bWithIntegration = bSupportedVersion &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_DYNAMICWIND_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Projects", "NeoStackAI" });
        if (!bWithIntegration)
        {
            return;
        }
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        // Lua and sol2 are exposed by the NeoStack AI core module as public includes.
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PublicDependencyModuleNames.AddRange(new string[] { "CoreUObject", "Engine" });
        // Only DynamicWind public headers are included (DynamicWindParameters.h,
        // DynamicWindSkeletalData.h, DynamicWindData.h) plus DynamicWindEditor's
        // public DynamicWindImportData.h. DynamicWindSubsystem.h pulls the Internal
        // DynamicWindLog.h and the private Blueprint library header is never
        // included: both are reached by reflection (Lua/LuaReflectionInvoke.h).
        PrivateDependencyModuleNames.AddRange(new string[] {
            "NeoStackAI",
            "UnrealEd",
            "AssetRegistry",
            "Json",
            "JsonUtilities",
            "MeshDescription",
            "StaticMeshDescription",
            "ImageCore",
            "DynamicWind",
            "DynamicWindEditor",
        });
    }
}
