# Video Completion Mode Feature

## Overview
A new boolean switch has been added to allow videos to finish playing before being replaced with the next image in the slideshow. This feature prevents interrupting videos mid-playback.

## Default Behavior
- **Enabled by default** (`allowVideoToFinish = true`)
- Videos will continue playing even if a new image update is triggered
- Once a video finishes, the next image will be displayed normally

## How to Toggle From Code

### Method 1: Toggle on/off
```csharp
// In MainWindow or any code with access to the engine
_engine.ToggleVideoCompletion();  // Switches between true and false
```

### Method 2: Check current state
```csharp
bool isEnabled = _engine.IsVideoCompletionEnabled();
```

### Method 3: Set directly
```csharp
// Enable video completion
_engine.SetVideoCompletion(true);

// Disable video completion (videos get replaced immediately)
_engine.SetVideoCompletion(false);
```

## Technical Implementation

### Files Modified

1. **SMEngine/CSettings.cs**
   - Added property: `public bool allowVideoToFinish = true;`
   - Initialized in constructor

2. **SMEngine/SMEngine.cs**
   - Added `ToggleVideoCompletion()` - toggles the mode
   - Added `IsVideoCompletionEnabled()` - gets current state
   - Added `SetVideoCompletion(bool enabled)` - sets mode directly

3. **andyScreenSaver/windows/Helpers/TileRenderer.cs**
   - Updated `RenderSync()` signature to accept `bool allowVideoToFinish = true`
   - Updated `RenderAsync()` signature to accept `bool allowVideoToFinish = true`
   - Both methods now check if a video is playing before replacement when enabled
   - Added debug logging for when videos are skipped due to this mode

4. **andyScreenSaver/windows/MainWindow.xaml.cs**
   - Updated all calls to `RenderSync()` and `RenderAsync()` to pass the setting
   - Passes `_engine?.settings.allowVideoToFinish ?? true` to both render methods
   - Ensures consistent behavior in both caption and non-caption rendering paths

## Behavior Details

### When Enabled (true)
- `RenderSync()` checks if a VideoView is currently playing before replacing it
- `RenderAsync()` checks if a VideoView is currently playing before replacing it
- If a video is playing, the replacement is skipped
- The update service will attempt to place the next image in a different tile
- Log messages: "allowVideoToFinish: skipping video replacement, one is still playing"

### When Disabled (false)
- Videos are immediately stopped and disposed
- New content replaces the video immediately
- Original behavior (no wait for video completion)

## Code Flow

1. Image update service triggers `UpdateImage()` in MainWindow
2. `UpdateImage()` calls `SetImage()` with the new ImageSet
3. `SetImage()` determines if it's a video or image
4. For videos, calls `RenderSync()` (synchronous) or `RenderAsync()` (asynchronous)
5. Renderer checks:
   - If `allowVideoToFinish` is true AND a video is playing → skip replacement
   - If `allowVideoToFinish` is false OR no video is playing → proceed with replacement

## Debug Logging

When videos are preserved, the following messages appear in logs:
```
allowVideoToFinish: skipping video replacement, one is still playing
allowVideoToFinish: skipping video replacement in grid, one is still playing
allowVideoToFinish: skipping video in grid, still playing
allowVideoToFinish: skipping video, still playing
```

## Example Usage

```csharp
// In MainWindow.xaml.cs or anywhere with engine access

public void Init()
{
    InitializeEngine();

    // Start with videos finishing enabled (default)
    // _engine.SetVideoCompletion(true);  // Already the default

    // Later, based on user preference...
    if (userPrefersNoWaiting)
    {
        _engine.SetVideoCompletion(false);
    }
}

// In user input handler
private void Window_KeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.V)  // Example: 'V' key to toggle
    {
        _engine.ToggleVideoCompletion();
        var state = _engine.IsVideoCompletionEnabled() ? "enabled" : "disabled";
        Debug.WriteLine($"Video completion mode: {state}");
    }
}
```

## Notes

- Setting is per-engine instance (not persisted to registry/disk)
- Default is `true` (allow videos to finish)
- Setting is checked on every render attempt, allowing runtime changes
- Both sync and async render paths respect the setting consistently
- Thread-safe: setting is accessed through the engine's public interface
