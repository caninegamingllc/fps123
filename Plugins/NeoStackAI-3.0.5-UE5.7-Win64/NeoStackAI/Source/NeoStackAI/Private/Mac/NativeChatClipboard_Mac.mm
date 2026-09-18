// Copyright 2026 Betide Studio. All Rights Reserved.
#include "NativeChat/NativeChatClipboard.h"
#if PLATFORM_MAC
#include "HAL/MemoryBase.h"
#include "Mac/MacMallocZone.h"
#include "Misc/EngineVersionComparison.h"
// Exported by Core in UE 5.5-5.8; matches ApplicationCore's pasteboard guard.
extern CORE_API FMacMallocCrashHandler* GCrashMalloc;
#define FVector MacCarbonFVector
#import <AppKit/AppKit.h>
#undef FVector

namespace NeoStackNativeChat
{
namespace
{
struct FMacClipboardOps
{
	NSPasteboard* Pasteboard = nil;
	NSArray* Objects = nil;
	bool Prepare(const FString& Text)
	{
		NSString* String = Text.GetNSString();
		if (!String) return false;
		NSPasteboardItem* Item = [[[NSPasteboardItem alloc] init] autorelease];
		if (!Item || ![Item setString:String forType:NSPasteboardTypeString]) return false;
		Objects = [NSArray arrayWithObject:Item];
		if (!Objects) return false;
		Pasteboard = [NSPasteboard generalPasteboard];
		return Pasteboard != nil;
	}
	bool Claim()
	{
		[Pasteboard clearContents]; // Its integer is a change count, not a BOOL.
		return true;
	}
	bool Publish() { return [Pasteboard writeObjects:Objects] == YES; }
};
}
bool TryCopyNativeText(const FString& Text)
{
	if (!IsInGameThread() || GIsCriticalError) return false;
	// Cocoa must not allocate after UE enters its crash allocator, even before
	// GIsCriticalError is set. Match the installed engine's versioned guard.
#if UE_VERSION_OLDER_THAN(5, 6, 0)
	if (GMalloc == GCrashMalloc) return false;
#else
	if (UE::Private::GMalloc == GCrashMalloc) return false;
#endif
	// Match UE's GT pasteboard route; no synchronous MainThreadCall, dispatch
	// queue, retained callback, or foreign-thread AppKit window/view access.
	@try
	{
		@autoreleasepool
		{
			FMacClipboardOps Ops;
			return ClipboardPrivate::TryCopyStagedText(Text, Ops);
		}
	}
	@catch (NSException* Exception)
	{
		(void)Exception; // Never log exception/userInfo/secret clipboard payloads.
		return false;
	}
}
}
#endif
