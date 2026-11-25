// Hardware keyboard keys through the GameController framework (GCKeyboard), for iOS.
// UIKit's key presses, which Unity's Keyboard reads, drop a key struck again soon after it was let
// go (iOS debounces keyboards), so a drum on keyboard keys loses hits. GCKeyboard reports every
// change. Each change waits here with its time (CACurrentMediaTime) until the game drains the queue
// once a frame (GameControllerKeyboard.cs). Key codes are HID usages (GCKeyCode).

#import <Foundation/Foundation.h>
#import <GameController/GameController.h>
#import <QuartzCore/QuartzCore.h>

#include <stdint.h>
#include <mutex>
#include <vector>

namespace
{
    struct KeyChange
    {
        int32_t code;
        uint8_t down;
        double time;
    };

    // More than a few seconds of key changes nobody drained (the game paused in the background).
    constexpr size_t MaxQueued = 4096;

    std::mutex gate;
    std::vector<KeyChange> queued;
    GCKeyboardInput *attached = nil;
    dispatch_queue_t handlers = nil;
    bool started = false;
    int32_t attachments = 0;

    // Puts the handler on the keyboard input the system has now, if it is not the one that has it.
    // Main thread: the connect notifications, Start and Connected arrive there.
    void Attach()
    {
        GCKeyboard *keyboard = GCKeyboard.coalescedKeyboard;
        GCKeyboardInput *input = keyboard.keyboardInput;
        if (input == attached) return;
        if (attached != nil) attached.keyChangedHandler = nil;
        attached = input;
        if (input == nil) return;
        attachments++;
        // Off the main thread, so a change is stamped when it arrives, not when a frame lets the run loop run.
        keyboard.handlerQueue = handlers;
        input.keyChangedHandler = ^(GCKeyboardInput *source, GCControllerButtonInput *key, GCKeyCode code, BOOL pressed)
        {
            (void)source; (void)key;
            double now = CACurrentMediaTime();
            std::lock_guard<std::mutex> lock(gate);
            if (queued.size() < MaxQueued) queued.push_back({ (int32_t)code, (uint8_t)(pressed ? 1 : 0), now });
        };
    }
}

extern "C"
{
    // Starts listening; returns whether a hardware keyboard is connected now.
    int32_t OurTaikoKeyboard_Start(void)
    {
        if (!started)
        {
            started = true;
            handlers = dispatch_queue_create("org.ourtaiko.keyboard", DISPATCH_QUEUE_SERIAL);
            NSNotificationCenter *center = [NSNotificationCenter defaultCenter];
            NSOperationQueue *main = [NSOperationQueue mainQueue];
            [center addObserverForName:GCKeyboardDidConnectNotification object:nil queue:main
                            usingBlock:^(NSNotification *note) { (void)note; Attach(); }];
            [center addObserverForName:GCKeyboardDidDisconnectNotification object:nil queue:main
                            usingBlock:^(NSNotification *note) { (void)note; Attach(); }];
        }
        Attach();
        return attached != nil ? 1 : 0;
    }

    // Whether a hardware keyboard is connected and has the handler. Asked every frame, and looks
    // at the system's keyboard input each time: the handler must be on that one, and the connect
    // notifications alone are not relied on to say when it has become another.
    int32_t OurTaikoKeyboard_Connected(void)
    {
        if (!started) return 0;
        Attach();
        return attached != nil ? 1 : 0;
    }

    // How many times the handler has been put on a keyboard input, for the log.
    int32_t OurTaikoKeyboard_Attachments(void)
    {
        return attachments;
    }

    double OurTaikoKeyboard_Now(void)
    {
        return CACurrentMediaTime();
    }

    // Moves up to `capacity` queued changes, oldest first, into the arrays; returns how many.
    int32_t OurTaikoKeyboard_Drain(int32_t *codes, uint8_t *downs, double *times, int32_t capacity)
    {
        if (codes == nullptr || downs == nullptr || times == nullptr || capacity <= 0) return 0;
        std::lock_guard<std::mutex> lock(gate);
        int32_t count = (int32_t)std::min(queued.size(), (size_t)capacity);
        for (int32_t i = 0; i < count; i++)
        {
            codes[i] = queued[i].code;
            downs[i] = queued[i].down;
            times[i] = queued[i].time;
        }
        queued.erase(queued.begin(), queued.begin() + count);
        return count;
    }
}
