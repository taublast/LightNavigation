#if IOS || MACCATALYST
using System;
using UIKit;
using Foundation;
using CoreGraphics;
using CoreAnimation;
using Microsoft.Maui.Platform;

namespace LightNavigation.Platform
{
    public class LightNavigationTransition : UIViewControllerAnimatedTransitioning
    {
        private readonly AnimationType _transitionType;
        private readonly UINavigationControllerOperation _operation;
        private readonly double _duration;
        private readonly TransitionEasing _easing;

        public LightNavigationTransition(AnimationType transitionType, UINavigationControllerOperation operation, double duration, TransitionEasing easing)
        {
            _transitionType = transitionType;
            _operation = operation;
            _duration = duration;
            _easing = easing;
        }

        public override double TransitionDuration(IUIViewControllerContextTransitioning transitionContext)
        {
            return _duration;
        }

        public override void AnimateTransition(IUIViewControllerContextTransitioning transitionContext)
        {
            var containerView = transitionContext.ContainerView;
            var fromViewController = transitionContext.GetViewControllerForKey(UITransitionContext.FromViewControllerKey);
            var toViewController = transitionContext.GetViewControllerForKey(UITransitionContext.ToViewControllerKey);
            var fromView = transitionContext.GetViewFor(UITransitionContext.FromViewKey) ?? fromViewController.View;
            var toView = transitionContext.GetViewFor(UITransitionContext.ToViewKey) ?? toViewController.View;

            if (toView == null || fromView == null)
            {
                transitionContext.CompleteTransition(false);
                return;
            }

            // Ensure the toView has the correct frame and is added to the container
            var finalFrame = transitionContext.GetFinalFrameForViewController(toViewController);
            toView.Frame = finalFrame;
            
            // For Push, we add toView on top. For Pop, we insert toView below fromView (usually).
            // However, standard practice is to add toView to container.
            // For Push: container has fromView. Add toView.
            // For Pop: container has fromView. Insert toView below fromView? 
            // Actually, for custom transitions, we manage the subviews.
            
            if (_operation == UINavigationControllerOperation.Push)
            {
                containerView.AddSubview(toView);
                PreparePushAnimation(toView, fromView, containerView.Bounds);
            }
            else if (_operation == UINavigationControllerOperation.Pop)
            {
                containerView.InsertSubviewBelow(toView, fromView);
                PreparePopAnimation(fromView, toView, containerView.Bounds);
            }

            // Force layout to ensure safe areas are respected before animation starts
            toView.SetNeedsLayout();
            toView.LayoutIfNeeded();
            fromView.SetNeedsLayout();
            fromView.LayoutIfNeeded();

            var curve = GetAnimationCurve(_easing, _operation == UINavigationControllerOperation.Push);
            
            // Special handling for WhirlIn3 rotation
            if (_transitionType == AnimationType.WhirlIn3)
            {
                var targetView = _operation == UINavigationControllerOperation.Push ? toView : fromView;
                var rotationAnimation = CABasicAnimation.FromKeyPath("transform.rotation.z");
                
                if (_operation == UINavigationControllerOperation.Push)
                {
                    rotationAnimation.From = NSNumber.FromDouble(-Math.PI * 6); // -1080 degrees
                    rotationAnimation.To = NSNumber.FromDouble(0);
                }
                else
                {
                    rotationAnimation.From = NSNumber.FromDouble(0);
                    rotationAnimation.To = NSNumber.FromDouble(Math.PI * 6); // 1080 degrees
                }
                
                rotationAnimation.Duration = _duration;
                rotationAnimation.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);
                targetView.Layer.AddAnimation(rotationAnimation, "whirl3Rotation");
            }

            var animator = new UIViewPropertyAnimator(_duration, curve, () =>
            {
                if (_operation == UINavigationControllerOperation.Push)
                {
                    PerformPushAnimation(toView, fromView, containerView.Bounds);
                }
                else
                {
                    PerformPopAnimation(fromView, toView, containerView.Bounds);
                }
            });

            animator.AddCompletion((position) =>
            {
                var success = !transitionContext.TransitionWasCancelled;
                
                // Cleanup
                if (success)
                {
                    if (_operation == UINavigationControllerOperation.Push)
                    {
                        fromView.Transform = CGAffineTransform.MakeIdentity();
                        fromView.Alpha = 1;
                    }
                    else
                    {
                        fromView.RemoveFromSuperview();
                        toView.Transform = CGAffineTransform.MakeIdentity();
                        toView.Alpha = 1;
                    }
                }
                else
                {
                    toView.RemoveFromSuperview();
                }

                transitionContext.CompleteTransition(success);
            });

            animator.StartAnimation();
        }

        private UIViewAnimationCurve GetAnimationCurve(TransitionEasing easing, bool isPush)
        {
            switch (easing)
            {
                case TransitionEasing.Linear: return UIViewAnimationCurve.Linear;
                case TransitionEasing.Decelerate: return UIViewAnimationCurve.EaseOut;
                case TransitionEasing.Accelerate: return UIViewAnimationCurve.EaseIn;
                case TransitionEasing.AccelerateDecelerate: return UIViewAnimationCurve.EaseInOut;
                default: return isPush ? UIViewAnimationCurve.EaseOut : UIViewAnimationCurve.EaseIn;
            }
        }

        private void PreparePushAnimation(UIView newView, UIView oldView, CGRect bounds)
        {
            switch (_transitionType)
            {
                case AnimationType.SlideFromRight:
                case AnimationType.Default:
                    newView.Transform = CGAffineTransform.MakeTranslation(bounds.Width, 0);
                    break;
                case AnimationType.SlideFromLeft:
                    newView.Transform = CGAffineTransform.MakeTranslation(-bounds.Width, 0);
                    break;
                case AnimationType.SlideFromBottom:
                    newView.Transform = CGAffineTransform.MakeTranslation(0, bounds.Height);
                    break;
                case AnimationType.SlideFromTop:
                    newView.Transform = CGAffineTransform.MakeTranslation(0, -bounds.Height);
                    break;
                case AnimationType.ParallaxSlideFromRight:
                    newView.Transform = CGAffineTransform.MakeTranslation(bounds.Width, 0);
                    break;
                case AnimationType.ParallaxSlideFromLeft:
                    newView.Transform = CGAffineTransform.MakeTranslation(-bounds.Width, 0);
                    break;
                case AnimationType.Fade:
                    newView.Alpha = 0;
                    break;
                case AnimationType.ZoomIn:
                    newView.Alpha = 0;
                    newView.Transform = CGAffineTransform.MakeScale(0.3f, 0.3f);
                    break;
                case AnimationType.ZoomOut:
                    newView.Alpha = 0;
                    newView.Transform = CGAffineTransform.MakeScale(1.5f, 1.5f);
                    break;
                case AnimationType.WhirlIn:
                    newView.Alpha = 0;
                    newView.Transform = CGAffineTransform.Rotate(CGAffineTransform.MakeScale(0.3f, 0.3f), (nfloat)(-Math.PI));
                    break;
                case AnimationType.WhirlIn3:
                    newView.Alpha = 0;
                    newView.Transform = CGAffineTransform.MakeScale(0.3f, 0.3f);
                    break;
            }
        }

        private void PerformPushAnimation(UIView newView, UIView oldView, CGRect bounds)
        {
            newView.Transform = CGAffineTransform.MakeIdentity();
            newView.Alpha = 1;

            switch (_transitionType)
            {
                case AnimationType.ParallaxSlideFromRight:
                    oldView.Transform = CGAffineTransform.MakeTranslation(-bounds.Width * 0.3f, 0);
                    break;
                case AnimationType.ParallaxSlideFromLeft:
                    oldView.Transform = CGAffineTransform.MakeTranslation(bounds.Width * 0.3f, 0);
                    break;
                case AnimationType.Fade:
                    oldView.Alpha = 0;
                    break;
            }
        }

        private void PreparePopAnimation(UIView oldView, UIView newView, CGRect bounds)
        {
            // Ensure newView is in its final state (identity) but maybe shifted for parallax
            newView.Transform = CGAffineTransform.MakeIdentity();
            newView.Alpha = 1;

            switch (_transitionType)
            {
                case AnimationType.ParallaxSlideFromRight:
                    newView.Transform = CGAffineTransform.MakeTranslation(-bounds.Width * 0.3f, 0);
                    break;
                case AnimationType.ParallaxSlideFromLeft:
                    newView.Transform = CGAffineTransform.MakeTranslation(bounds.Width * 0.3f, 0);
                    break;
                case AnimationType.Fade:
                    newView.Alpha = 0;
                    break;
            }
        }

        private void PerformPopAnimation(UIView oldView, UIView newView, CGRect bounds)
        {
            newView.Transform = CGAffineTransform.MakeIdentity();
            newView.Alpha = 1;

            switch (_transitionType)
            {
                case AnimationType.SlideFromRight:
                case AnimationType.Default:
                    oldView.Transform = CGAffineTransform.MakeTranslation(bounds.Width, 0);
                    break;
                case AnimationType.SlideFromLeft:
                    oldView.Transform = CGAffineTransform.MakeTranslation(-bounds.Width, 0);
                    break;
                case AnimationType.SlideFromBottom:
                    oldView.Transform = CGAffineTransform.MakeTranslation(0, bounds.Height);
                    break;
                case AnimationType.SlideFromTop:
                    oldView.Transform = CGAffineTransform.MakeTranslation(0, -bounds.Height);
                    break;
                case AnimationType.ParallaxSlideFromRight:
                    oldView.Transform = CGAffineTransform.MakeTranslation(bounds.Width, 0);
                    break;
                case AnimationType.ParallaxSlideFromLeft:
                    oldView.Transform = CGAffineTransform.MakeTranslation(-bounds.Width, 0);
                    break;
                case AnimationType.Fade:
                    oldView.Alpha = 0;
                    break;
                case AnimationType.ZoomIn:
                    oldView.Alpha = 0;
                    oldView.Transform = CGAffineTransform.MakeScale(0.3f, 0.3f);
                    break;
                case AnimationType.ZoomOut:
                    oldView.Alpha = 0;
                    oldView.Transform = CGAffineTransform.MakeScale(1.5f, 1.5f);
                    break;
                case AnimationType.WhirlIn:
                    oldView.Alpha = 0;
                    oldView.Transform = CGAffineTransform.Rotate(CGAffineTransform.MakeScale(0.3f, 0.3f), (nfloat)(-Math.PI));
                    break;
                case AnimationType.WhirlIn3:
                    oldView.Alpha = 0;
                    oldView.Transform = CGAffineTransform.MakeScale(0.3f, 0.3f);
                    break;
            }
        }
    }
}
#endif
