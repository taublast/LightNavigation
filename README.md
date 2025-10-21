# LightNavigation for .NET MAUI

A subclassed `NavigationPage` bringing custom animated transitions and page lifecycle events.

## 🚀 Features

- ✅ **Smooth Animations** - Platform-native animations with customizations
- ✅ **Lifecycle Awareness** - `INavigationAware` interface for navigation lifecycle callbacks, dispose resources properly
- ✅ **Queue-Based Navigation** - Prevents concurrent navigation operations issues
- ✅ **Zero Dependencies** - No third-party libraries required

## 📦 Installation

### NuGet Package Manager

```bash
dotnet add package LightNavigation
```

### Package Manager Console

```powershell
Install-Package LightNavigation
```

## 🔧 Setup

### 1. Register the Handlers

In your `MauiProgram.cs`, add the `ConfigureLightNavigation()` call:

```csharp
using LightNavigation;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureLightNavigation()  // ← Add this line
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

That's it! Your navigation is now smooth and flash-free.

## 📚 Usage

### Basic Navigation

`LightNavigationPage` works exactly like the standard `NavigationPage`:

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

    // Called just before this page is pushed onto the navigation stack
    public void OnPushing()
    {
        System.Diagnostics.Debug.WriteLine("Page is being pushed");
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

## 🎨 How It Works

### Platform-Specific Implementations

#### Android
- Uses `FrameLayout` directly (no fragments)
- Custom view animations with `ViewPropertyAnimator`
- Sequential navigation queue to prevent concurrent operations

#### iOS / Mac Catalyst
- Uses `UINavigationController` with custom view management
- `UIViewPropertyAnimator` for smooth, interruptible animations
- Parallax effect for native iOS feel

#### Windows
- Uses `Grid` container for simplicity
- `Storyboard` animations with `DoubleAnimation`
- Dual-view rendering during transitions

## 🔍 Technical Details

### Animation Durations

- **Push Animation**: 150ms (Android/Windows), 300ms (iOS)
- **Pop Animation**: 100ms (Android), 150ms (Windows), 300ms (iOS)

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

- [ ] Additional animation styles
- [ ] Gesture-based navigation
- [ ] Transition customization API
- [ ] Performance profiling tools

---

**Made with ❤️ for the .NET MAUI community**
