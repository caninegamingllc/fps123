using UnrealBuildTool;
using System.IO;

public class NSAI_GameplayCameras_Integration : ModuleRules
{
    public NSAI_GameplayCameras_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI75_GC";
        // GameplayCameras.uplugin (Engine/Plugins/Cameras/GameplayCameras) is EnabledByDefault and
        // Experimental. This module needs the 5.6 surface: CameraShakeAsset, the two actor factories,
        // FCameraAssetReference/FCameraRigAssetReference on the components and AGameplayCameraRigActor.
        // 5.5 exposes none of those, so the module compiles to a stub there.
        string[] RequiredPlugins = new string[] { "GameplayCameras" };
        bool bSupportedVersion = Target.Version.MajorVersion > 5 ||
            (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 6);
        bool bWithIntegration = bSupportedVersion &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_GAMEPLAYCAMERAS_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
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
            // Runtime camera module: assets, nodes, directors, components, evaluator readback.
            "GameplayCameras",
            // UStateTreeCameraDirector::StateTreeReference (FStateTreeReference::SetStateTree).
            "StateTreeModule",
            // UCameraRigAsset::GameplayTags.
            "GameplayTags",
            "AssetRegistry",
            "AssetTools",
        });
        // GameplayCamerasEditor (factories, actor factories) keeps every class in Private headers and
        // exports nothing this module needs; it is loaded by name at call time and its classes are found
        // by /Script path, so it is intentionally not a link dependency.
    }
}
