#if IOS || MACCATALYST
using System;
using UIKit;
using Foundation;
using Microsoft.Maui.Controls;

namespace LightNavigation.Platform
{
    public class LightNavigationControllerDelegate : UINavigationControllerDelegate
    {
        public override IUIViewControllerAnimatedTransitioning GetAnimationControllerForOperation(UINavigationController navigationController, UINavigationControllerOperation operation, UIViewController fromViewController, UIViewController toViewController)
        {
            Page page = null;
            bool isPush = operation == UINavigationControllerOperation.Push;

            if (isPush)
            {
                if (toViewController is LightPageViewController lightTo)
                {
                    page = lightTo.MauiPage;
                }
            }
            else if (operation == UINavigationControllerOperation.Pop)
            {
                // For Pop, we want the transition of the page being popped (fromViewController)
                if (fromViewController is LightPageViewController lightFrom)
                {
                    page = lightFrom.MauiPage;
                }
            }

            if (page != null)
            {
                var transition = LightNavigationPage.GetEffectiveTransition(page);
                
                // If Default, return null to let system handle it (standard iOS push/pop)
                if (transition == AnimationType.Default || transition == AnimationType.None)
                {
                    return null;
                }

                var duration = GetAnimationDuration(page, isPush, transition);
                var easing = LightNavigationPage.GetTransitionEasing(page);

                return new LightNavigationTransition(transition, operation, duration, easing);
            }

            return null;
        }

        private double GetAnimationDuration(Page page, bool isPush, AnimationType transition)
        {
            var customSpeed = LightNavigationPage.GetTransitionSpeed(page);
            if (customSpeed > 0)
            {
                return customSpeed / 1000.0;
            }

            if (transition == AnimationType.WhirlIn3)
            {
                return 0.4;
            }

            return isPush ? 0.3 : 0.25;
        }
    }
}
#endif
