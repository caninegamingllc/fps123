using UnrealBuildTool;
using System.IO;

public class NSAI_Composure_Integration : ModuleRules
{
    public NSAI_Composure_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI72_CMP";
        // Legacy Composure plugin (Engine/Plugins/Compositing/Composure, "Legacy Composure" in 5.7+).
        // Its descriptor pulls ComposureShared, CameraCalibrationCore, BlueprintMaterialTextureNodes,
        // MediaIOFramework, OpenColorIO, ActorLayerUtilities and LensComponent (Composure.uplugin:38-66).
        string[] RequiredPlugins = new string[] { "Composure" };
        bool bWithIntegration = NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_COMPOSURE_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
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
            "Slate",
            "SlateCore",
            "Composure",
            "CinematicCamera",
            "RenderCore",
            "RHI",
        });
        // ICompElementEditorModule / ICompElementManager are pure-virtual interfaces reached through
        // FModuleManager::LoadModulePtr; the module is loaded PostEngineInit (Composure.uplugin:29-33)
        // so only the include path is needed, never a link dependency.
        PrivateIncludePathModuleNames.Add("ComposureLayersEditor");
    }
}
