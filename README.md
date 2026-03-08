# LightNavigation for .NET MAUI

A subclassed `NavigationPage` bringing custom animated transitions and page lifecycle events. 
To use for implementing custom navigation scenarios.
Android, iOS, MacCatalist, Windows, .net9 .net10.

## ✨ Features

- **Easy to use** - A subclassed NavigationPage acting as a drop-in replacement to navigate among pages and modals
- **Smooth Transitions** - Platform-native animations (Fade, Zoom, Whirl, Slide, Parallax, etc.)
- **Transition Customization** - Control animation speed and easing per page
- **Lifecycle Callbacks** - Pages now "know" when they are covered/removed/went on top for managing data and resources
- **Queue-Based Navigation** - Prevents concurrent navigation operations issues
- **Zero Dependencies** - No third-party libraries required

## What's New 1.10.1.1
* **Animations fixes** for animations consistency across platforms.
* **.NET 10 support** thanks to [yurkinh](https://github.com/yurkinh) for the kick.

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

// Push modal
await Navigation.PushModalAsync(new NavigationPage(new LoginPage()));

// Pop modal
await Navigation.PopModalAsync();

// With animation control
await Navigation.PushAsync(new DetailPage(), animated: true);
```

all methods have optional `bool animated` parameter!


### Static Methods

Configure navigation behavior globally or per-page using static methods.

#### Global Configuration

| Method | Description |
|--------|-------------|
| `SetDefaultTransition(AnimationType transition)` | Sets the default transition animation type to use when a page doesn't have a specific transition set. |
| `SetDefaultTransitionSpeedMs(int msIn, int msOut)` | Sets global default transition speeds in milliseconds for push (in) and pop (out). |

#### Per-Page Configuration (Attached Properties)

| Method | Description |
|--------|-------------|
| `SetTransition(BindableObject page, AnimationType type)` | Sets the transition animation type for the specified page. |
| `SetTransitionSpeedMs(BindableObject page, int duration)` | Sets the transition speed (duration in ms) for the specified page. 0 = default. |
| `SetTransitionEasing(BindableObject page, TransitionEasing easing)` | Sets the transition easing type for the specified page. |

#### Per-Page Helpers

| Method | Description |
|--------|-------------|
| `GetTransition(BindableObject page)` | Gets the transition animation type for the specified page. |
| `GetTransitionSpeedMs(BindableObject page)` | Gets the transition speed for the specified page. |
| `GetTransitionEasing(BindableObject page)` | Gets the transition easing type for the specified page. |

#### Global Helpers

| Method | Description |
|--------|-------------|
| `GetDefaultTransition()` | Gets the current global default transition animation type. |
| `GetDefaultTransitionSpeedInMs()` | Gets the global default push/in animation duration in ms. |
| `GetDefaultTransitionSpeedOutMs()` | Gets the global default pop/out animation duration in ms. |

Example:

```csharp
        //inside MauiProgram.cs
        LightNavigationPage.SetDefaultTransition(AnimationType.SlideFromBottom);
        LightNavigationPage.SetDefaultTransitionSpeedMs(300, 200);
```

### Navigation Lifecycle Awareness

Implement `INavigationAware` on your pages to receive navigation lifecycle callbacks.

| Method | Description |
|--------|-------------|
| `OnPushing()` | Called just before this page is about to be pushed onto the navigation stack. |
| `OnTopmost()` | Called when this page becomes the topmost page (visible to user). |
| `OnCovered()` | Called when this page is covered by another page (pushed on top of it). |
| `OnPopping()` | Called just before this page is about to be popped from the navigation stack. |
| `OnRemoved()` | Called when this page is removed from the navigation stack. |

Example implementation:

```csharp
using LightNavigation;
using Microsoft.Maui.Controls;

public partial class MyPage : ContentPage, INavigationAware
{
	public MyPage()
	{
		InitializeComponent();
	}

	// Called just before this page is pushed onto the navigation stack
	public void OnPushing()
	{
		System.Diagnostics.Debug.WriteLine("Page is being pushed");
		// can start preparing data etc..
	}

	// Called when this page becomes the topmost page (visible to user)
	public void OnTopmost()
	{
		System.Diagnostics.Debug.WriteLine("Page is now topmost");
		// Refresh data, resume animations, etc.
	}

	// Called when this page is covered by another page (pushed on top of it)
	public void OnCovered()
	{
		System.Diagnostics.Debug.WriteLine("Page is being covered");
		// Pause animations, etc.
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
		this.DisconnectHandlers(); // <-- important to avoid memory leaks

		// Dispose other resources if needed
	}
}
```

### Custom Transitions

LightNavigation supports 13 different transition animations that can be applied globally or per-page.

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

Control animation speed and easing for individual pages using the attached properties or static methods (see below).

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

All durations can be overridden per-page using `LightNavigationPage.SetTransitionSpeedMs()`.

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

---

## 📄 License

MIT License - See LICENSE file for details

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📞 Support

If you encounter any issues or have questions:
- Check the documentation  
- Review the included LightNavigation.TestApp for usage examples  
- If an issue is reproducible inside LightNavigation.TestApp please open an issue on GitHub  

---

**Made with ❤️ for the .NET MAUI community**
