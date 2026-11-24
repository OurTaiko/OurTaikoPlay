// The game's build number on iOS, for GameVersion.cs: the bundle's CFBundleVersion, which
// PlayerBuilds sets to the commit count (CFBundleShortVersionString, Unity's Application.version,
// must be plain dotted numbers there, so the number cannot ride in it as on the other platforms).

#import <Foundation/Foundation.h>
#include <stdlib.h>
#include <string.h>

// A copy made with malloc: the runtime frees the string it is given.
extern "C" char* OurTaikoBundle_BuildNumber()
{
    NSString* number = [NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleVersion"];
    const char* text = [number isKindOfClass:NSString.class] ? number.UTF8String : "";
    return strdup(text != NULL ? text : "");
}
