// The display's fastest refresh rate, for iOS: DisplayRefreshRate.cs draws at it when the frame rate
// setting is Unlimited. 120 on a ProMotion display (the game's Info.plist sets
// CADisableMinimumFrameDurationOnPhone, see IosProjectBranding.cs), 60 elsewhere.

#import <UIKit/UIKit.h>

extern "C" int OurTaikoDisplay_MaximumFramesPerSecond()
{
    return (int)UIScreen.mainScreen.maximumFramesPerSecond;
}
