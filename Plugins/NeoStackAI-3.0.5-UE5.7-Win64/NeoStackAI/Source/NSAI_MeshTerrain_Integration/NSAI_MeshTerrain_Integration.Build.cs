using UnrealBuildTool;
using System.IO;

public class NSAI_MeshTerrain_Integration : ModuleRules
{
    public NSAI_MeshTerrain_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI64_MT";
        string[] RequiredPlugins = new string[] { "MeshPartition", "MeshModelingToolset", "GeometryProcessing" };
        bool bSupportedVersion = Target.Version.MajorVersion > 5 ||
            (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 8);
        bool bWithIntegration = bSupportedVersion &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_MESHTERRAIN_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Projects", "NeoStackAI" });
        if (!bWithIntegration)
        {
            return;
        }
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        // UE 5.8 MeshPartitionCompiledSection.h publicly includes Engine's internal
        // MaterialCacheVirtualTexture.h; add that include path here.
        PrivateIncludePaths.Add(Path.Combine(EngineDirectory, "Source", "Runtime", "Engine", "Internal"));
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PrivateDependencyModuleNames.AddRange(new string[] {
            "CoreUObject", "Engine", "UnrealEd", "PhysicsCore", "MeshPartition", "MeshPartitionEditor",
            "GeometryCore", "GeometryFramework", "InteractiveToolsFramework", "AssetRegistry", "AssetTools",
            "MeshPartitionModelingToolset", "MeshModelingTools", "ModelingComponents", "DynamicMesh", "ImageWrapper", "MeshDescription", "StaticMeshDescription"
        });
    }
}
