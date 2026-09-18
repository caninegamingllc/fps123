// Copyright 2026 Betide Studio. All Rights Reserved.
#include "AgentRuntime/RuntimeHostWriter.h"
#if PLATFORM_MAC
// Foundation.h pulls in CarbonCore's NumberFormatting.h, whose struct FVector collides with the
// engine's FVector alias; hide it the way the clipboard .mm files do.
#define FVector MacCarbonFVector
#import <Foundation/Foundation.h>
#undef FVector
int32 NeoStackRuntimeWriteDescriptor(void* Pipe)
{
	return [(NSFileHandle*)Pipe fileDescriptor];
}
#endif
