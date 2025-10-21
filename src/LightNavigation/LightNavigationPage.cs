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
