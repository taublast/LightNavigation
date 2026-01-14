# LightNavigation for .NET MAUI

A subclassed `NavigationPage` bringing custom animated transitions and page lifecycle events. To use for implementing custom navigation scenarios.
Android, iOS, MacCatalist, Windows, .NET9.

## 🚀 Features

- ✅ **Smooth Animations** - Platform-native animations with customizations
- ✅ **Custom Transitions** - 13 built-in transition types (Fade, Zoom, Whirl, Slide, Parallax, etc.)
- ✅ **Transition Customization** - Control animation speed and easing per page
- ✅ **Lifecycle Awareness** - `INavigationAware` interface for navigation lifecycle callbacks, dispose resources properly
- ✅ **Queue-Based Navigation** - Prevents concurrent navigation operations issues
- ✅ **Zero Dependencies** - No third-party libraries required

Solves:

* Transition issues in standard `NavigationPage` such as:
	* https://github.com/dotnet/maui/issues/11809
	* https://github.com/dotnet/maui/issues/16621
* Lack of different transition animations options. 
* Lifecycle awareness for pages, so they now "know" if they are covered/removed/went on top etc to properly manage data and resources.

## 🎈 What's New in v1.3.0
* Nuget targets both net9 and net10
* Sample app targets net10

## 📦 Installation

### NuGet Package Manager

```bash
dotnet add package Plugin.Maui.LightNavigation
```

### Package Manager Console

```powershell
Install-Package Plugin.Maui.LightNavigation
```

## 🔧 Setup

### 1. Register the Handlers

In your `MauiProgram.cs`, add the `UseLightNavigation()` call:

```csharp
using LightNavigation;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseLightNavigation()  // ← Add this line
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        return builder.Build();
    }
}
```

### 2. Use LightNavigationPage

Replace your `NavigationPage` with `LightNavigationPage`:

```csharp
using LightNavigation;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Replace: MainPage = new NavigationPage(new MainPage());
        // With:
        MainPage = new LightNavigationPage(new MainPage());
    }
}
```

or when using FlyoutPage:

```csharp

Detail = new LightNavigationPage(new MainPage());

_navigationRoot = Detail as NavigableElement; //<-- use this for navigation

```


### 3. Example App

You can find an example app in the `LightNavigation.TestApp` project included in this repository. Useful for testing and exploring features.


## 📚 Usage

### Basic Navigation

`LightNavigationPage` works exactly like the standard `NavigationPage` in fact it's a subclassed one:

```csharp
// Push a page
await Navigation.PushAsync(new DetailPage());

// Pop a page
await Navigation.PopAsync();

// Pop to root
await Navigation.PopToRootAsync();

// With animation control
await Navigation.PushAsync(new DetailPage(), animated: true);
```

all methods have optional `bool animated` parameter.

### Navigation Lifecycle Awareness

Implement `INavigationAware` on your pages to receive navigation lifecycle callbacks:

```csharp
using LightNavigation;
using Microsoft.Maui.Controls;

public partial class MyPage : ContentPage, INavigationAware
{
    public MyPage()
    {
        InitializeComponent();
    }

	// Called when this page is removed from the navigation stack
    public void OnRemoved()
    {
        System.Diagnostics.Debug.WriteLine("Page has been removed");
        // Clean up resources, unsubscribe from events, etc.

		this.DisconnectHandlers(); // <-- important to avoid memory leaks

		// Dispose other resources if needed
    }

    // Called just before this page is pushed onto the navigation stack
    public void OnPushing()
    {
        System.Diagnostics.Debug.WriteLine("Page is being pushed");
		//can start preparing data etc..
    }

    // Called when this page becomes the topmost page (visible to user)
    public void OnTopmost()
    {
        System.Diagnostics.Debug.WriteLine("Page is now topmost");
        // Refresh data, resume animations, etc.
    }

    // Called just before this page is popped from the navigation stack
    public void OnPopping()
    {
        System.Diagnostics.Debug.WriteLine("Page is being popped");
    }


    // Called when this page is removed from the navigation stack
    public void OnRemoved()
    {
        System.Diagnostics.Debug.WriteLine("Page has been removed");
        // Clean up resources, unsubscribe from events, etc.
    }
}
```

### Custom Transitions

LightNavigation supports 13 different transition animations:

```csharp
// Set a global default transition for all pages
LightNavigationPage.SetDefaultTransition(AnimationType.Fade);

// Set transition for a specific page
var page = new DetailPage();
LightNavigationPage.SetTransition(page, AnimationType.SlideFromBottom);
await Navigation.PushAsync(page);
```

**Available Transition Types:**
- `Default` - Platform native transition
- `None` - No animation (instant)
- `SlideFromRight` / `SlideFromLeft` / `SlideFromBottom` / `SlideFromTop`
- `ParallaxSlideFromRight` / `ParallaxSlideFromLeft` - iOS-style parallax effect
- `Fade` - Crossfade between pages
- `ZoomIn` / `ZoomOut` - Zoom from/to center
- `WhirlIn` - Rotate + zoom with 180° rotation
- `WhirlIn3` - Rotate + zoom with 3 full rotations (1080°)

> **Note:** All 13 custom transitions are fully implemented on Android, Windows, iOS, and Mac Catalyst.

### Transition Customization

Control animation speed and easing for individual pages:

```csharp
var page = new DetailPage();

// Custom animation speed (duration in milliseconds, 0 = use default)
LightNavigationPage.SetTransitionSpeed(page, 500); // 500ms animation

// Custom easing/interpolation (Default = use built-in)
LightNavigationPage.SetTransitionEasing(page, TransitionEasing.Linear);

await Navigation.PushAsync(page);
```

**Available Easing Types:**
- `Default` - Platform default (Decelerate for push, Accelerate for pop)
- `Linear` - Constant speed
- `Decelerate` - Fast start, slow end
- `Accelerate` - Slow start, fast end
- `AccelerateDecelerate` - Slow-fast-slow

> **Note:** Speed and easing customization is fully supported on all platforms (Android, Windows, iOS, and Mac Catalyst).

**XAML Usage:**
```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:ln="clr-namespace:LightNavigation;assembly=LightNavigation"
             x:Class="MyApp.DetailPage"
             ln:LightNavigationPage.Transition="Fade"
             ln:LightNavigationPage.TransitionSpeed="300"
             ln:LightNavigationPage.TransitionEasing="Linear">
    <!-- Page content -->
</ContentPage>
```



## 🎨 How It Works

### Platform-Specific Implementations

#### Android
- Uses `FrameLayout` directly (no fragments)
- Custom view animations with `ViewPropertyAnimator`
- Sequential navigation queue to prevent concurrent operations
- **Navigation Handling**: Uses MAUI's `RequestNavigation` command mapper

#### iOS / Mac Catalyst
- Uses `UINavigationController` with custom view management
- `UIViewPropertyAnimator` for smooth, interruptible animations with `CGAffineTransform` for complex transitions
- Parallax effect and custom transitions for native iOS feel
- **Navigation Handling**: Uses event-based navigation (`INavigationPageController` events) for reliable operation
  - Subscribes to `PushRequested`, `PopRequested`, and `PopToRootRequested` events
  - This approach ensures compatibility with iOS navigation lifecycle

#### Windows
- Uses `Grid` container for simplicity
- `Storyboard` animations with `DoubleAnimation` and `CompositeTransform`
- Dual-view rendering during transitions
- **Navigation Handling**: Uses MAUI's `RequestNavigation` command mapper

## 🔍 Technical Details

### Animation Durations

- **Push Animation**: 150ms (Android/Windows), 300ms (iOS)
- **Pop Animation**: 100ms (Android), 150ms (Windows), 300ms (iOS)
- **WhirlIn3 Animation**: 400ms (all platforms) - Extended duration for dramatic 3-rotation effect

All durations can be overridden per-page using `LightNavigationPage.SetTransitionSpeed()`.

### Navigation Queue

All platforms implement a queue-based navigation system using:
- `Queue<Func<Task>>` for navigation operations
- `SemaphoreSlim` for sequential processing
- Prevents concurrent navigation that causes crashes

## 🆚 Comparison

| Feature | Standard NavigationPage | LightNavigationPage |
|---------|------------------------|---------------------|
| Black flash on transitions | ❌ Yes (Android/Windows) | ✅ No |
| Smooth animations | ⚠️ Basic | ✅ Platform-optimized |
| Custom transition types | ❌ No | ✅ 13 built-in types |
| Animation speed control | ❌ No | ✅ Per-page customization |
| Easing customization | ❌ No | ✅ 5 easing types |
| Navigation queue | ❌ No | ✅ Yes |
| Lifecycle callbacks | ❌ Limited | ✅ Full `INavigationAware` |
| Concurrent navigation safety | ❌ Can crash | ✅ Protected |

## 📄 License

MIT License - See LICENSE file for details

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📞 Support

If you encounter any issues or have questions:
- Open an issue on GitHub
- Check the documentation
- Review the sample code

## 🎯 Roadmap

- [x] Custom transition animations (13 types implemented)
- [x] Transition speed and easing customization
- [x] iOS/Catalyst transition implementations (all 13 transitions + speed/easing)
- [ ] Gesture-based navigation
- [ ] Performance profiling tools

---

**Made with ❤️ for the .NET MAUI community**
