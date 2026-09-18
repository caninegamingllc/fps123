using UnrealBuildTool;
using System;
using System.IO;

public class NSAI_MetaHuman_Integration : ModuleRules
{
    public NSAI_MetaHuman_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI37_MH";
        string[] RequiredPlugins = new string[] { "MetaHumanCharacter", "MetaHumanSDK", "MetaHuman", "MetaHumanCoreTech" };
        bool bWithIntegration = NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_METAHUMAN_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Projects", "NeoStackAI" });
        if (!bWithIntegration)
        {
            return;
        }
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDefinitions.Add("SOL_ALL_SAFETIES_ON=1");
        PublicDefinitions.Add("SOL_USING_CXX_LUA=0");
        PublicDefinitions.Add("SOL_PRINT_ERRORS=0");
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new string[] {
            "NeoStackAI",
            "UnrealEd",
        });

        // Lua and sol2 are exposed by the NeoStack AI core module as public includes.

        // MetaHumanCharacter has EnabledByDefault=false. Detect modules by
        // descriptor + source Build.cs files, not generated Intermediate
        // headers that may not exist before the engine/plugin has been built.
        string MetaHumanPluginDir = Path.Combine(EngineDirectory, "Plugins", "MetaHuman", "MetaHumanCharacter");
        string MetaHumanSDKDir = Path.Combine(EngineDirectory, "Plugins", "MetaHuman", "MetaHumanSDK");
        string CoreTechDir = Path.Combine(EngineDirectory, "Plugins", "MetaHuman", "MetaHumanCoreTechLib");
        string AnimatorDir = Path.Combine(EngineDirectory, "Plugins", "MetaHuman", "MetaHumanAnimator");
        string CaptureDataDir = Path.Combine(EngineDirectory, "Plugins", "VirtualProduction", "CaptureData");
        string ImgMediaDir = Path.Combine(EngineDirectory, "Plugins", "Media", "ImgMedia");
        bool bHasMetaHumanCharacter =
            File.Exists(Path.Combine(MetaHumanPluginDir, "MetaHumanCharacter.uplugin")) &&
            HasPluginModuleSource(MetaHumanPluginDir, "MetaHumanCharacter") &&
            HasPluginModuleSource(MetaHumanPluginDir, "MetaHumanCharacterEditor") &&
            HasPluginModuleSource(MetaHumanPluginDir, "MetaHumanCharacterPalette") &&
            HasPluginModuleSource(MetaHumanPluginDir, "MetaHumanCharacterPaletteEditor") &&
            HasPluginModuleSource(MetaHumanSDKDir, "MetaHumanSDKRuntime") &&
            HasPluginModuleSource(MetaHumanSDKDir, "MetaHumanSDKEditor") &&
            HasPluginModuleSource(CoreTechDir, "MetaHumanCoreTechLib");

        // MetaHuman Animator Performance processing/export and
        // the 5.8 Creator export library. Both are detected separately so a
        // Creator-only install (no Animator modules) still binds the Creator verbs.
        bool bHasPerformance = false;
        bool bHasExportLibrary = false;

        if (bHasMetaHumanCharacter)
        {
            PrivateDependencyModuleNames.AddRange(new string[] {
                "MetaHumanCharacter",
                "MetaHumanSDKRuntime",
                "MetaHumanSDKEditor",
                "MetaHumanCharacterEditor",
                "MetaHumanCharacterPalette",
                "MetaHumanCharacterPaletteEditor",
                "MetaHumanCoreTechLib",
                "AssetRegistry",
            });

            // MetaHumanIdentity (under MetaHumanAnimator plugin) for ImportFromIdentity
            if (HasPluginModuleSource(AnimatorDir, "MetaHumanIdentity"))
            {
                PrivateDependencyModuleNames.Add("MetaHumanIdentity");
            }

            // UMetaHumanPerformance (MetaHumanAnimator/MetaHumanPerformance, Editor,
            // PostEngineInit). Its public header pulls CaptureDataCore (CaptureData.h,
            // FrameRange.h), NNE (NNEModelData.h / NNETypes.h) and ControlRig
            // (Rigs/RigHierarchyElements.h, ControlRigAssetReference.h) which are only
            // private dependencies of that module, so they are listed here explicitly.
            // MetaHumanCaptureData, MetaHumanPipelineCore, MetaHumanPipeline,
            // MetaHumanCoreTech, MetaHumanBodyTrackerInterface (5.8), MetaHumanCoreEditor
            // and CaptureDataEditor are public dependencies of MetaHumanPerformance and
            // propagate automatically (MetaHumanPerformance.Build.cs:10-26).
            // ImgMedia (UImgMediaSource, ImgMediaSource.h) is only a PRIVATE dependency
            // of CaptureDataCore (CaptureDataCore.Build.cs:14-25; its public list at
            // :9-12 is just "Media"), so UFootageCaptureData::ImageSequences elements
            // are opaque unless ImgMedia is listed here. The ImgMedia plugin is
            // EnabledByDefault (ImgMedia.uplugin:13) and CaptureData.uplugin:35-38
            // enables it explicitly, so no .uproject change is needed.
            bHasPerformance =
                HasPluginModuleSource(AnimatorDir, "MetaHumanPerformance") &&
                HasPluginModuleSource(AnimatorDir, "MetaHumanIdentity") &&
                HasPluginModuleSource(CoreTechDir, "MetaHumanCoreTech") &&
                HasPluginModuleSource(CoreTechDir, "MetaHumanPipelineCore") &&
                HasPluginModuleSource(AnimatorDir, "MetaHumanPipeline") &&
                HasPluginModuleSource(CaptureDataDir, "CaptureDataCore") &&
                HasPluginModuleSource(CaptureDataDir, "CaptureDataEditor") &&
                HasPluginModuleSource(ImgMediaDir, "ImgMedia");
            if (bHasPerformance)
            {
                PrivateDependencyModuleNames.AddRange(new string[] {
                    "MetaHumanPerformance",
                    "MetaHumanCoreTech",
                    "MetaHumanPipelineCore",
                    "MetaHumanPipeline",
                    "CaptureDataCore",
                    "CaptureDataEditor",
                    "ImgMedia",
                    "NNE",
                    "ControlRig",
                    "RigVM",
                    "AssetTools",
                    "LevelSequence",
                    "AnimationCore",
                });
                if (Target.Version.MajorVersion > 5 || (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 8))
                {
                    // UMetaHumanPerformanceExportAnimationSettings::BodyRetargeter (UIKRetargeter) is 5.8-only.
                    PrivateDependencyModuleNames.Add("IKRig");
                }
            }

            // UMetaHumanCharacterExportBlueprintLibrary ships in UE 5.8 only
            // (MetaHumanCharacterEditor/Public/MetaHumanCharacterExportBlueprintLibrary.h).
            // Errors from that library go to FMessageLog("MetaHuman"), captured
            // through the MessageLog module listing.
            bHasExportLibrary =
                (Target.Version.MajorVersion > 5 || (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 8)) &&
                File.Exists(Path.Combine(MetaHumanPluginDir, "Source", "MetaHumanCharacterEditor", "Public", "MetaHumanCharacterExportBlueprintLibrary.h"));
            if (bHasExportLibrary)
            {
                PrivateDependencyModuleNames.Add("MessageLog");
            }
        }
        else
        {
            PrivateDefinitions.Add("NSAI_METAHUMAN_DISABLED=1");
        }

        PrivateDefinitions.Add("NSAI_HAS_METAHUMAN_PERFORMANCE=" + (bHasPerformance ? "1" : "0"));
        PrivateDefinitions.Add("NSAI_HAS_METAHUMAN_EXPORT_LIBRARY=" + (bHasExportLibrary ? "1" : "0"));
    }

    private static bool HasPluginModuleSource(string PluginDir, string ModuleName)
    {
        return File.Exists(Path.Combine(PluginDir, "Source", ModuleName, ModuleName + ".Build.cs"));
    }
}
