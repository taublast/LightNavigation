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
        private bool _safeAreaAdjusted = false;

        //    /*
        //    public override void ViewSafeAreaInsetsDidChange()
        //    {
        //        base.ViewSafeAreaInsetsDidChange();

        //        // Go fullscreen
        //        // Only do this once to avoid recursion
        //        if (View!=null && !_safeAreaAdjusted)
        //        {
        //            _safeAreaAdjusted = true;

        //            var check = NavigationController;

        //            var safeArea = View.SafeAreaInsets;
        //            var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(MauiPage);

        //            // Calculate correct safe area insets
        //            var topInset = safeArea.Top; // Status bar area
        //            var bottomInset = safeArea.Bottom; // Home indicator area

        //            // If navigation bar is visible, add its height to top inset
        //            if (!hasNavBar && NavigationController?.NavigationBar != null)
        //            {
        //                topInset -= NavigationController.NavigationBar.Frame.Height;
        //            }

        //            MauiPage.On<iOS>().SetSafeAreaInsets(
        //                new Thickness(safeArea.Left, topInset, safeArea.Right, bottomInset));

        //            System.Diagnostics.Debug.WriteLine($"[LightPageVC] Set safe area - Top: {topInset}, Bottom: {bottomInset}, HasNavBar: {hasNavBar}");


        //            // Counteract safe area insets by setting negative additional insets
        //            var safeArea = View.SafeAreaInsets;
        //            AdditionalSafeAreaInsets = new UIEdgeInsets(
        //                -safeArea.Top,
        //                -safeArea.Left,
        //                -safeArea.Bottom,
        //                -safeArea.Right
        //            );

        //        }
        //    }
        //    */


        public override void ViewSafeAreaInsetsDidChange()
        {
            base.ViewSafeAreaInsetsDidChange();

            // Set safe areas when iOS has calculated them
            if (!_safeAreaAdjusted && View != null && MauiPage != null)
            {
              
                //_safeAreaAdjusted = true;

                var safeArea = View.SafeAreaInsets;
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(MauiPage);

                // Calculate correct safe area insets
                var topInset = safeArea.Top; // Status bar area
                var bottomInset = safeArea.Bottom; // Home indicator area

                /*
                // If navigation bar is visible, add its height to top inset
                if (!hasNavBar && NavigationController?.NavigationBar != null)
                {
                    topInset -= NavigationController.NavigationBar.Frame.Height;
                }

                MauiPage.On<iOS>().SetSafeAreaInsets(
                    new Thickness(safeArea.Left, topInset, safeArea.Right, bottomInset));
                */
                System.Diagnostics.Debug.WriteLine($"[LightPageVC] {MauiPage.GetType().Name} SafeAreaInsets - Top: {topInset}, Bottom: {bottomInset}, HasNavBar: {hasNavBar}");
            }
        }

    }
}
#endif
