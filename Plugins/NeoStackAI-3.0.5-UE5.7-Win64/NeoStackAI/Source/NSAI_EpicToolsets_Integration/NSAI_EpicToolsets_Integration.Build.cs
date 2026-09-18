using UnrealBuildTool;
using System.IO;

// Epic toolsets integration: UAgentSkill catalogue import + allow-listed skill
// write-through (reflection only; never links Epic's NoRedist ToolsetRegistry),
// FileSandboxCore-backed sandbox verbs, and the Experimental/Toolsets coverage
// matrix. UE 5.8+ only: ToolsetRegistry, Experimental/Toolsets and
// Developer/Sandbox/FileSandbox do not exist in 5.5-5.7.
public class NSAI_EpicToolsets_Integration : ModuleRules
{
    public NSAI_EpicToolsets_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI67_ET";
        string[] RequiredPlugins = new string[] { "ToolsetRegistry" };
        bool bSupportedVersion = Target.Version.MajorVersion > 5 ||
            (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 8);
        bool bWithIntegration = bSupportedVersion &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        // FileSandbox (Developer/Sandbox/FileSandbox) is Beta and redistributable, so it
        // may be linked. It is enabled transitively by ToolsetRegistry.uplugin but custom
        // engine builds can strip it, so it gets its own flag.
        bool bWithFileSandbox = bWithIntegration &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, new string[] { "FileSandbox" }, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_EPICTOOLSETS_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDefinitions.Add("WITH_NSAI_EPICTOOLSETS_FILESANDBOX=" + (bWithFileSandbox ? "1" : "0"));
        // The UDeveloperSettings class in Public/ compiles in both branches so UHT never
        // sees a conditionally declared UCLASS; the stub branch just registers no verbs.
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "CoreUObject", "Engine", "DeveloperSettings", "Projects", "NeoStackAI" });
        if (!bWithIntegration)
        {
            return;
        }
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        // Lua and sol2 are exposed by the NeoStack AI core module as public includes.
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PrivateDependencyModuleNames.AddRange(new string[] {
            "NeoStackAI",
            "UnrealEd",
            "AssetRegistry",
            "AssetTools",
            "Kismet",
            "EditorScriptingUtilities",
            "Json",
            "JsonUtilities",
            "Projects",
            "DeveloperSettings",
        });
        // Deliberately never "ToolsetRegistry": it is NoRedist and is reached by reflection only.
        if (bWithFileSandbox)
        {
            PrivateDependencyModuleNames.Add("FileSandboxCore");
        }
    }
}
