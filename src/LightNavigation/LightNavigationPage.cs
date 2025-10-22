using Microsoft.Maui.Controls;

namespace LightNavigation
{
    /// <summary>
    /// A high-performance NavigationPage implementation that eliminates black flashing
    /// during page transitions on all platforms.
    ///
    /// Features:
    /// - Smooth animations on Android, iOS, and Windows
    /// - No black flash during navigation transitions
    /// - Support for INavigationAware lifecycle callbacks
    /// - Queue-based navigation to prevent concurrent operations
    ///
    /// Usage:
    /// Replace your NavigationPage with LightNavigationPage:
    /// <code>
    /// MainPage = new LightNavigationPage(new YourRootPage());
    /// </code>
    /// </summary>
    public class LightNavigationPage : NavigationPage
    {
        /// <summary>
        /// Attached property for specifying the transition animation type for a page.
        /// Usage in XAML: ln:LightNavigationPage.Transition="Fade"
        /// </summary>
        public static readonly BindableProperty TransitionProperty = BindableProperty.CreateAttached(
            propertyName: "Transition",
            returnType: typeof(AnimationType),
            declaringType: typeof(LightNavigationPage),
            defaultValue: AnimationType.Default,
            defaultBindingMode: BindingMode.OneWay);

        /// <summary>
        /// Gets the transition animation type for the specified page.
        /// This is the getter for the attached property.
        /// </summary>
        public static AnimationType GetTransition(BindableObject target)
        {
            return (AnimationType)target.GetValue(TransitionProperty);
        }

        /// <summary>
        /// Sets the transition animation type for the specified page.
        /// This is the setter for the attached property.
        /// </summary>
        public static void SetTransition(BindableObject target, AnimationType value)
        {
            target.SetValue(TransitionProperty, value);
        }

        /// <summary>
        /// Creates a new instance of LightNavigationPage with the specified root page.
        /// </summary>
        /// <param name="root">The root page to display in the navigation stack.</param>
        public LightNavigationPage(Page root) : base(root)
        {
            this.Popped += OnPopRequested;
            this.PoppedToRoot += OnPopToRootRequested;
        }

        private void OnPopToRootRequested(object? sender, NavigationEventArgs navigationEventArgs)
        {
            try
            {
                this.PoppedToRoot -= OnPopToRootRequested;
                this.Popped -= OnPopRequested;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"[LightNavigationPage] Error in OnPopToRootRequested: {exception}");
            }
        }

        private void OnPopRequested(object? sender, NavigationEventArgs navigationEventArgs)
        {
            try
            {
                this.Popped -= OnPopRequested;
                this.PoppedToRoot -= OnPopToRootRequested;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"[LightNavigationPage] Error in OnPopRequested: {exception}");
            }
        }
    }
}
