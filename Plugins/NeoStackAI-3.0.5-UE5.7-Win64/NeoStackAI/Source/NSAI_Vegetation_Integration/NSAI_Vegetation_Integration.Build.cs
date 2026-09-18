using UnrealBuildTool;
using System.IO;

// Procedural Vegetation Editor (PVE) integration: UProceduralVegetation assets, their
// embedded UProceduralVegetationGraph (a UPCGGraph subclass), export-node authoring,
// generation through the shared PCG job registry and the editor-command export path.
// 5.8+ only: the 5.7 plugin layout differs (UProceduralVegetation : UObject, no
// Nodes/PVExportSettings.h, PVExportParams.h at the Public root), so older engines get
// the empty stub module and no vegetation verbs.
public class NSAI_Vegetation_Integration : ModuleRules
{
    public NSAI_Vegetation_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI68_VEG";
        string[] RequiredPlugins = new string[] { "ProceduralVegetationEditor", "PCG" };
        bool bSupportedVersion = Target.Version.MajorVersion > 5 ||
            (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 8);
        bool bWithIntegration = bSupportedVersion &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_VEGETATION_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
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
            // Shared standalone/vegetation job registry and node spawning (Public/ headers of the PCG integration).
            "NSAI_PCG_Integration",
            "UnrealEd",
            // FToolkitManager::Get()/FindEditorForAsset (Toolkits/ToolkitManager.h:21,33 are
            // EDITORFRAMEWORK_API); UnrealEd's public dependency does not link the import lib for us.
            "EditorFramework",
            // PCGEditor.h:11 -> Schema/PCGEditorGraphSchema.h:7 -> PCGEditorCommon.h:6 ->
            // Misc/AssetCategoryPath.h. The FPCGEditorCommon::PCG*AssetCategoryPath namespace consts
            // (PCGEditorCommon.h:57-59) emit dynamic initialisers in every TU that includes the header,
            // and FAssetCategoryPath's ctor/AppendSubMenu are ASSETDEFINITION_API (AssetCategoryPath.h:48,88).
            "AssetDefinition",
            "Slate",
            "SlateCore",
            "InputCore",
            "AssetRegistry",
            "PCG",
            "PCGEditor",
            // ProceduralVegetation (runtime module of the PVE plugin): UProceduralVegetation, FPVExportParams, UPVData.
            "ProceduralVegetation",
            // Params/PVExportParams.h -> PVWindSettings.h -> DynamicWindSkeletalData.h
            "DynamicWind",
            // DataTypes/PVData.h -> GeometryCollection/ManagedArrayCollection.h (export-node group counts)
            "Chaos",
            "ChaosCore",
        });
    }
}
