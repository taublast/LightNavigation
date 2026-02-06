#if IOS || MACCATALYST
using UIKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace LightNavigation.Platform
{
    public class LightPageViewController : UIViewController
    {
        public MauiPage? MauiPage { get; set; }
        private bool _safeAreaSet = false;

        public override void ViewSafeAreaInsetsDidChange()
        {
            base.ViewSafeAreaInsetsDidChange();

            // Set safe areas when iOS has calculated them
            if (!_safeAreaSet && View != null && MauiPage != null)
            {
                _safeAreaSet = true;

                var safeArea = View.SafeAreaInsets;
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(MauiPage);

                // Calculate correct safe area insets
                var topInset = safeArea.Top; // Status bar area
                var bottomInset = safeArea.Bottom; // Home indicator area

                // If navigation bar is visible, add its height to top inset
                if (hasNavBar && NavigationController?.NavigationBar != null)
                {
                    topInset += NavigationController.NavigationBar.Frame.Height;
                }

                MauiPage.On<iOS>().SetSafeAreaInsets(
                    new Thickness(safeArea.Left, topInset, safeArea.Right, bottomInset));

                System.Diagnostics.Debug.WriteLine($"[LightPageVC] Set safe area - Top: {topInset}, Bottom: {bottomInset}, HasNavBar: {hasNavBar}");
            }
        }
    }
}
#endif