using UnrealBuildTool;
using System.IO;

public class NSAI_Mutable_Integration : ModuleRules
{
    public NSAI_Mutable_Integration(ReadOnlyTargetRules Target) : base(Target)
    {
		bUsePrecompiled = true;
        ShortName = "NSI71_MU";
        string[] RequiredPlugins = new string[] { "Mutable" };
        // UCustomizableObject::Compile(FCompileParams) exists from UE 5.6 (5.5 only has
        // ConditionalAutoCompile), but 5.6 still differs in FCompileParams field names
        // (bSkipIfOutOfDate / bGatherReferneces), the by-name texture parameter API,
        // the missing SkeletalMesh/Material parameter accessors and the synchronous bake
        // signature. The bound surface targets UE 5.7+; older engines compile the stub.
        bool bSupportedVersion = Target.Version.MajorVersion > 5 ||
            (Target.Version.MajorVersion == 5 && Target.Version.MinorVersion >= 7);
        bool bWithIntegration = bSupportedVersion &&
            NeoStackIntegrationRules.ArePluginsAvailable(Target, RequiredPlugins, EngineDirectory);
        PublicDefinitions.Add("WITH_NSAI_MUTABLE_INTEGRATION=" + (bWithIntegration ? "1" : "0"));
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Projects", "NeoStackAI" });
        if (!bWithIntegration)
        {
            return;
        }
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        // Lua and sol2 are exposed by the NeoStack AI core module as public includes.
        PrivateDefinitions.Add("LUA_BUILD_AS_DLL=1");
        PrivateDependencyModuleNames.AddRange(new string[] {
            "CoreUObject",
            "Engine",
            "UnrealEd",
            "AssetRegistry",
            "AssetTools",
            "CustomizableObject",
            "CustomizableObjectEditor",
            "MutableRuntime",
            "MutableTools",
            "SkeletalMerging",
            "GameplayTags",
        });
        // ICustomizableObjectEditorModule (IsCompiling / CancelCompileRequests / silent compile)
        // lives in CustomizableObject/Internal and the bake shim entry points
        // (ScheduleCOCompilationForBaking / ScheduleInstanceUpdateForBaking /
        // BakeCustomizableObjectInstance) in CustomizableObjectEditor/Internal. UBT only
        // propagates Internal include paths to engine-scope modules
        // (UEBuildModule.cs:736-740), so reach in explicitly.
        string MutableSource = Path.Combine(EngineDirectory, "Plugins", "Mutable", "Source");
        if (!Directory.Exists(MutableSource))
        {
            MutableSource = Path.Combine(EngineDirectory, "Plugins", "Experimental", "Mutable", "Source");
        }
        PrivateIncludePaths.Add(Path.Combine(MutableSource, "CustomizableObject/Internal"));
        PrivateIncludePaths.Add(Path.Combine(MutableSource, "CustomizableObjectEditor/Internal"));
    }
}
