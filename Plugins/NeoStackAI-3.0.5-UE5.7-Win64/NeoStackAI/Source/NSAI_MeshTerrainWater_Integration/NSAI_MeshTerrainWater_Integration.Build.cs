using UnrealBuildTool;
using System.IO;
public class NSAI_MeshTerrainWater_Integration : ModuleRules
{
    public NSAI_MeshTerrainWater_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI65_MTW";
        string[] RequiredPlugins = new string[] { "Water", "MeshPartition", "MeshPartitionWater" };
        bool bWithIntegration = (Target.Version.MajorVersion > 5 || (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 8)) &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_MESHTERRAINWATER_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Projects", "NeoStackAI" });
        if (!bWithIntegration) return;
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        // UE5.8 public MeshPartitionCompiledSection.h includes this Engine
        // internal header.
        PrivateIncludePaths.Add(Path.Combine(EngineDirectory, "Source", "Runtime", "Engine", "Internal"));
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PrivateDependencyModuleNames.AddRange(new string[] { "CoreUObject", "Engine", "UnrealEd", "Water", "WaterEditor", "MeshPartition", "MeshPartitionEditor", "MeshPartitionWater" });
    }
}
