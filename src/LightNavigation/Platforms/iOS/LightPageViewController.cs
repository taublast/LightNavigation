#if IOS || MACCATALYST
using UIKit;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace LightNavigation.Platform
{
    public class LightPageViewController : UIViewController
    {
        public virtual void SetPage(MauiPage targetPage)
        {
            MauiPage = targetPage;
        }

        public MauiPage? MauiPage { get; set; }

        public override void ViewSafeAreaInsetsDidChange()
        {
            base.ViewSafeAreaInsetsDidChange();

            // Update safe areas whenever iOS reports changes (happens during animations)
            if (View != null && MauiPage != null)
            {
                var safeArea = View.SafeAreaInsets;
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(MauiPage);

                // Calculate correct safe area insets
                var topInset = safeArea.Top; // Status bar area
                var bottomInset = safeArea.Bottom; // Home indicator area

                // Navigation bar height is already included in safeArea.Top by iOS when navbar is visible
                // We don't need to add it manually

                MauiPage.On<iOS>().SetSafeAreaInsets(
                    new Thickness(safeArea.Left, topInset, safeArea.Right, bottomInset));

                // Force MAUI to re-layout the page with new safe areas
                if (MauiPage.Handler?.PlatformView is UIView platformView)
                {
                    platformView.SetNeedsLayout();
                    platformView.LayoutIfNeeded();
                }

                System.Diagnostics.Debug.WriteLine($"[LightPageVC] {MauiPage.GetType().Name} SafeAreaInsets - Top: {topInset}, Bottom: {bottomInset}, HasNavBar: {hasNavBar}");
            }
        }
    }
}
#endif
