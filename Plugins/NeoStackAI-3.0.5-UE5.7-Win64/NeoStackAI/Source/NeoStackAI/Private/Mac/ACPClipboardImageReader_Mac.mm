// Copyright 2026 Betide Studio. All Rights Reserved.
#include "ACPClipboardImageReader.h"
#if PLATFORM_MAC
#define FVector MacCarbonFVector
#import <AppKit/AppKit.h>
#undef FVector

bool FACPClipboardImageReader::HasImageOnClipboard()
{
	@autoreleasepool
	{
		return [[NSPasteboard generalPasteboard] availableTypeFromArray:
			@[NSPasteboardTypePNG, @"public.jpeg", NSPasteboardTypeTIFF]] != nil;
	}
}
FACPClipboardImageCapture FACPClipboardImageReader::CaptureImageFromClipboard()
{
	FACPClipboardImageCapture Result;
	@autoreleasepool
	{
		NSPasteboard* Pasteboard = [NSPasteboard generalPasteboard];
		NSString* Type = [Pasteboard availableTypeFromArray:@[NSPasteboardTypePNG, @"public.jpeg", NSPasteboardTypeTIFF]];
		if (!Type) return Result;
		Result.bImagePresent = true;
		if ([Type isEqualToString:NSPasteboardTypeTIFF])
		{
			// Cocoa's imageRepWithData / TIFF-to-PNG path decodes before we can
			// impose a pixel allocation limit. Do not run it on the paste route.
			Result.Error = TEXT("TIFF clipboard images are unsupported. Copy or attach a PNG or JPEG image instead.");
			return Result;
		}
		NSData* Data = [Pasteboard dataForType:Type];
		if (!Data || Data.length == 0 || Data.length > NSUInteger(MaxEncodedBytes))
		{
			Result.Error = TEXT("Clipboard image is unreadable or exceeds the 2.5 MiB encoded limit."); return Result;
		}
		Result.Bytes.Append(static_cast<const uint8*>(Data.bytes), int32(Data.length));
		Result.Format = [Type isEqualToString:NSPasteboardTypePNG] ? EACPClipboardImageFormat::Png : EACPClipboardImageFormat::Jpeg;
		// Dimensions and signatures are checked by shared bounded ingestion on
		// an owned worker. No NSBitmapImageRep or TIFF decode occurs here.
	}
	return Result;
}
#endif
