#if ANDROID
using Android.Views;
using Android.Widget;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Debug = System.Diagnostics.Debug;
using AView = Android.Views.View;

namespace LightNavigation.Platform
{
    /// <summary>
    /// COMPLETELY CUSTOM NavigationPage handler for Android that DOES NOT use fragments.
    /// This eliminates black flashing and navigation issues on Android.
    /// </summary>
    public class LightNavigationViewHandler : ViewHandler<NavigationPage, FrameLayout>
    {
        private const string TAG = "[LightNavigation_Android]";
        private readonly List<Page> _viewStack = new();
        private Page? _currentView;

        // Animation settings
        private const int ANIMATION_IN_DURATION_MS = 150;
        private const int ANIMATION_OUT_DURATION_MS = 100;
        private const int WHIRL3_DURATION_MS = 400; // Longer duration for 3-rotation whirl effect

        // Navigation operation queue to prevent concurrent operations and ensure smooth transitions
        private readonly Queue<Func<Task>> _navigationQueue = new();
        private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);
        private bool _isProcessingQueue = false;

        public LightNavigationViewHandler() : base(PropertyMapper, CommandMapper)
        {
            Debug.WriteLine($"{TAG} ✅ Handler created");
        }

        /// <summary>
        /// Enqueues a navigation operation and processes the queue sequentially.
        /// This prevents concurrent navigation operations that cause crashes and visual glitches.
        /// </summary>
        private async Task EnqueueNavigationAsync(Func<Task> operation)
        {
            _navigationQueue.Enqueue(operation);
            Debug.WriteLine($"{TAG} 📥 Enqueued operation. Queue size: {_navigationQueue.Count}");

            // If already processing, just return - the queue will be processed
            if (_isProcessingQueue)
            {
                Debug.WriteLine($"{TAG} ⏳ Already processing queue, operation will be handled");
                return;
            }

            _isProcessingQueue = true;

            try
            {
                while (_navigationQueue.Count > 0)
                {
                    var nextOperation = _navigationQueue.Dequeue();
                    Debug.WriteLine($"{TAG} 🔄 Processing operation. Remaining: {_navigationQueue.Count}");

                    await _navigationSemaphore.WaitAsync();
                    try
                    {
                        await nextOperation();
                    }
                    finally
                    {
                        _navigationSemaphore.Release();
                    }
                }
            }
            finally
            {
                _isProcessingQueue = false;
                Debug.WriteLine($"{TAG} ✅ Queue processing complete");
            }
        }

        public static IPropertyMapper<NavigationPage, LightNavigationViewHandler> PropertyMapper =
            new PropertyMapper<NavigationPage, LightNavigationViewHandler>(ViewHandler.ViewMapper)
            {
            };

        public static CommandMapper<NavigationPage, LightNavigationViewHandler> CommandMapper =
            new CommandMapper<NavigationPage, LightNavigationViewHandler>(ViewHandler.ViewCommandMapper)
            {
                [nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation
            };

        protected override FrameLayout CreatePlatformView()
        {
            // Create a simple FrameLayout container - NO fragments!
            var container = new FrameLayout(Context)
            {
                LayoutParameters = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent)
            };

            // Set background to transparent
            container.SetBackgroundColor(Android.Graphics.Color.Transparent);

            return container;
        }

        protected override void DisconnectHandler(FrameLayout platformView)
        {
            // Clean up views
            platformView.RemoveAllViews();
            _navigationQueue.Clear();
            _viewStack.Clear();
            _currentView = null;
            _navigationSemaphore?.Dispose();

            base.DisconnectHandler(platformView);
        }

        private static void MapRequestNavigation(LightNavigationViewHandler handler, NavigationPage view, object? args)
        {
            if (args is not NavigationRequest request)
            {
                Debug.WriteLine($"{TAG} ⚠️ MapRequestNavigation called with invalid args");
                return;
            }

            handler.HandleNavigationRequest(request);
        }

        private void HandleNavigationRequest(NavigationRequest request)
        {
            _ = EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var newStack = request.NavigationStack;
                    var currentStackCount = _viewStack.Count;

                    Debug.WriteLine($"{TAG} 📊 Current stack: {currentStackCount}, New stack: {newStack.Count}");

                    if (newStack.Count > currentStackCount)
                    {
                        // Push - show the new page
                        var newPage = newStack[newStack.Count - 1] as Page;
                        if (newPage != null)
                        {
                            // Get the effective transition (page-specific or global default)
                            var transition = LightNavigationPage.GetEffectiveTransition(newPage);

                            if (currentStackCount == 0)
                            {
                                await ShowPageAsync(newPage, false, transition, isInitial: true);
                            }
                            else
                            {
                                await ShowPageAsync(newPage, request.Animated, transition, isInitial: false);
                            }
                        }
                    }
                    else if (newStack.Count < currentStackCount)
                    {
                        // Pop - get transition from the page we're returning to
                        var targetPage = newStack.Count > 0 ? newStack[newStack.Count - 1] as Page : null;
                        var transition = targetPage != null ? LightNavigationPage.GetEffectiveTransition(targetPage) : AnimationType.Default;

                        var diff = currentStackCount - newStack.Count;

                        // Pop the required number of pages
                        for (int i = 0; i < diff; i++)
                        {
                            await PopPageAsync(request.Animated, transition);
                        }
                    }

                    // Notify MAUI that navigation is complete
                    if (VirtualView is IStackNavigation nav)
                    {
                        nav.NavigationFinished(newStack);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG} ❌ Error handling navigation request: {ex.Message}");
                }
            });
        }

        private Task ShowPageAsync(Page page, bool animate, AnimationType transition, bool isInitial)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                // Check if page is already in the stack to prevent duplicates
                if (_viewStack.Contains(page))
                {
                    Debug.WriteLine($"{TAG} ⚠️ Page already in stack, skipping");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                // Convert MAUI page to Android View
                var newView = page.ToPlatform(MauiContext!);

                if (newView == null)
                {
                    Debug.WriteLine($"{TAG} ❌ ToPlatform returned null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                Debug.WriteLine($"{TAG} ✅ Created Android view for page");

                var container = PlatformView;
                var oldPage = _currentView;
                var oldView = _currentView?.Handler?.PlatformView as AView;

                // CRITICAL: Ensure view is removed from previous parent before adding
                var parent = newView.Parent as ViewGroup;
                if (parent != null)
                {
                    Debug.WriteLine($"{TAG} ⚠️ View already has parent, removing first");
                    parent.RemoveView(newView);
                }

                var newAware = page as INavigationAware;
                var oldAware = oldPage as INavigationAware;

                newAware?.OnPushing();
                oldAware?.OnPopping();

                if (isInitial || oldView == null)
                {
                    // Set as root - no animation
                    container.AddView(newView);
                    _currentView = page;
                    _viewStack.Add(page);
                    oldAware?.OnCovered();
                    newAware?.OnTopmost();
                    tcs.SetResult(true);
                }
                else
                {
                    if (animate && transition != AnimationType.None)
                    {
                        // Set initial animation state based on transition type
                        ApplyPushAnimationStart(newView, oldView, container, transition);
                        oldView.Visibility = ViewStates.Visible;
                    }

                    container.AddView(newView);

                    // Fix for safe insets issue: Ensure the new view receives window insets immediately
                    // This prevents the content from jumping to safe insets after animation completes
                    if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M && container.RootWindowInsets != null)
                    {
                        newView.DispatchApplyWindowInsets(container.RootWindowInsets);
                    }
                    else
                    {
                        newView.RequestApplyInsets();
                    }

                    _viewStack.Add(page);

                    // Wait for new view to be laid out and rendered
                    newView.Post(() =>
                    {
                        if (animate && transition != AnimationType.None)
                        {
                            // Animate based on transition type
                            ApplyPushAnimation(newView, oldView, container, transition, page, () =>
                            {
                                // AFTER animation completes, hide old view
                                ResetViewState(newView);
                                oldView.Visibility = ViewStates.Invisible;

                                newAware?.OnTopmost();
                                oldAware?.OnCovered();
                                tcs.SetResult(true);
                            });
                        }
                        else
                        {
                            // No animation - hide old view immediately
                            oldView.Visibility = ViewStates.Invisible;

                            newAware?.OnTopmost();
                            oldAware?.OnCovered();
                            tcs.SetResult(true);
                        }
                    });

                    _currentView = page;
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in ShowPageAsync: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task PopPageAsync(bool animate, AnimationType transition)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                if (_viewStack.Count <= 1)
                {
                    Debug.WriteLine($"{TAG} ⚠️ Cannot pop - only one page on stack");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                var container = PlatformView;

                if (_viewStack.Count > 1)
                {
                    var page = _viewStack[_viewStack.Count - 2];
                    var newView = page.Handler!.PlatformView as AView;
                    var oldPage = _viewStack.Last();
                    var oldView = oldPage.Handler!.PlatformView as AView;

                    var newAware = page as INavigationAware;
                    var oldAware = oldPage as INavigationAware;

                    oldAware?.OnPopping();

                    if (animate && transition != AnimationType.None && oldView != null && newView != null)
                    {
                        // Set initial state for pop animation
                        ApplyPopAnimationStart(oldView, newView, container, transition);
                    }

                    Debug.WriteLine($"{TAG} OLDVIEW visibility: {oldView?.Visibility}");

                    newView?.Post(() =>
                    {
                        if (animate && transition != AnimationType.None && oldView != null && newView != null)
                        {
                            Debug.WriteLine($"{TAG} 🎬 Starting pop animation - {transition}");

                            // Apply pop animation based on transition type
                            // Use oldPage properties since that's the page being removed
                            ApplyPopAnimation(oldView, newView, container, transition, oldPage, () =>
                            {
                                container.RemoveView(oldView);
                                ResetViewState(newView);

                                // Update stacks AFTER animation completes
                                _currentView = page;
                                _viewStack.Remove(oldPage);

                                oldAware?.OnRemoved();
                                newAware?.OnTopmost();
                                Debug.WriteLine($"{TAG} 📊 Stack count after pop: {_viewStack.Count}");
                                Debug.WriteLine($"{TAG} ✅ Pop animation complete!");
                                tcs.SetResult(true);
                            });
                        }
                        else
                        {
                            // No animation - just remove immediately
                            if (oldView != null)
                            {
                                container.RemoveView(oldView);
                            }

                            // Make sure the new view is visible
                            if (newView != null)
                            {
                                newView.Visibility = ViewStates.Visible;
                                newView.Alpha = 1;
                                newView.TranslationX = 0;
                                newView.TranslationY = 0;
                                newView.ScaleX = 1;
                                newView.ScaleY = 1;
                                newView.BringToFront();
                            }

                            // Update stacks immediately (no animation)
                            _currentView = page;
                            _viewStack.Remove(oldPage);

                            oldAware?.OnRemoved();
                            newAware?.OnTopmost();
                            Debug.WriteLine($"{TAG} 📊 Stack count after pop: {_viewStack.Count}");
                            Debug.WriteLine($"{TAG} ✅ Pop complete");
                            tcs.SetResult(true);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in PopPageAsync: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task PopToRootAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                if (_viewStack.Count <= 1)
                {
                    Debug.WriteLine($"{TAG} ℹ️ Already at root");
                    tcs.SetResult(true);
                    return tcs.Task;
                }

                var container = PlatformView;

                // Get the root view (first item in stack)
                var page = _viewStack.First();
                var rootView = page.Handler!.PlatformView as AView;

                Debug.WriteLine($"{TAG} 🏠 PopToRoot - showing root view");

                // Make root view visible
                if (rootView != null)
                {
                    rootView.Visibility = ViewStates.Visible;
                }

                // Remove all other views from container
                var viewsToRemove = _viewStack.Skip(1).ToList();

                rootView?.Post(() =>
                {
                    foreach (var oldPage in viewsToRemove)
                    {
                        var oldView = oldPage.Handler?.PlatformView as AView;
                        if (oldView != null)
                        {
                            container.RemoveView(oldView);
                        }
                        if (oldPage is INavigationAware oldAware)
                        {
                            oldAware.OnRemoved();
                        }
                    }

                    tcs.SetResult(true);
                });

                // Clear stack except root
                _viewStack.Clear();
                _viewStack.Add(page);
                _currentView = page;

                if (page is INavigationAware newAware)
                {
                    newAware.OnTopmost();
                }

                Debug.WriteLine($"{TAG} 📊 Stack count after PopToRoot: {_viewStack.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in PopToRootAsync: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        #region Animation Helpers

        private void ResetViewState(AView view)
        {
            view.TranslationX = 0;
            view.TranslationY = 0;
            view.Alpha = 1;
            view.ScaleX = 1;
            view.ScaleY = 1;
        }

        /// <summary>
        /// Gets the appropriate Android interpolator for the specified easing type.
        /// </summary>
        /// <param name="easing">The easing type (Default = use built-in behavior).</param>
        /// <param name="isForward">True for push animations (use decelerate), false for pop animations (use accelerate).</param>
        /// <returns>The Android IInterpolator to use.</returns>
        private Android.Views.Animations.IInterpolator GetInterpolatorForEasing(TransitionEasing easing, bool isForward)
        {
            // If Default (0), use the standard behavior based on direction
            if (easing == TransitionEasing.Default)
            {
                return isForward
                    ? new Android.Views.Animations.DecelerateInterpolator()
                    : new Android.Views.Animations.AccelerateInterpolator();
            }

            // Otherwise, use the specified easing type
            return easing switch
            {
                TransitionEasing.Linear => new Android.Views.Animations.LinearInterpolator(),
                TransitionEasing.Decelerate => new Android.Views.Animations.DecelerateInterpolator(),
                TransitionEasing.Accelerate => new Android.Views.Animations.AccelerateInterpolator(),
                TransitionEasing.AccelerateDecelerate => new Android.Views.Animations.AccelerateDecelerateInterpolator(),
                _ => isForward
                    ? new Android.Views.Animations.DecelerateInterpolator()
                    : new Android.Views.Animations.AccelerateInterpolator()
            };
        }

        private void ApplyPushAnimationStart(AView newView, AView oldView, FrameLayout container, AnimationType transition)
        {
            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                case AnimationType.ParallaxSlideFromRight:
                    newView.Alpha = 0.3f;
                    newView.TranslationX = container.Width * 0.15f;
                    break;

                case AnimationType.SlideFromLeft:
                case AnimationType.ParallaxSlideFromLeft:
                    newView.Alpha = 0.3f;
                    newView.TranslationX = -container.Width * 0.15f;
                    break;

                case AnimationType.SlideFromBottom:
                    newView.Alpha = 0.3f;
                    newView.TranslationY = container.Height * 0.15f;
                    break;

                case AnimationType.SlideFromTop:
                    newView.Alpha = 0.3f;
                    newView.TranslationY = -container.Height * 0.15f;
                    break;

                case AnimationType.Fade:
                    newView.Alpha = 0f;
                    break;

                case AnimationType.ZoomIn:
                    newView.Alpha = 0f;
                    newView.ScaleX = 0.85f;
                    newView.ScaleY = 0.85f;
                    // Set pivot to center for zoom
                    newView.PivotX = container.Width / 2f;
                    newView.PivotY = container.Height / 2f;
                    break;

                case AnimationType.ZoomOut:
                    newView.Alpha = 0f;
                    newView.ScaleX = 1.15f;
                    newView.ScaleY = 1.15f;
                    // Set pivot to center for zoom
                    newView.PivotX = container.Width / 2f;
                    newView.PivotY = container.Height / 2f;
                    break;

                case AnimationType.WhirlIn:
                    newView.Alpha = 0f;
                    newView.ScaleX = 0.3f;
                    newView.ScaleY = 0.3f;
                    newView.Rotation = -180f; // Start rotated 180 degrees counter-clockwise
                    // Set pivot to center for rotation and zoom
                    newView.PivotX = container.Width / 2f;
                    newView.PivotY = container.Height / 2f;
                    break;

                case AnimationType.WhirlIn3:
                    newView.Alpha = 0f;
                    newView.ScaleX = 0.3f;
                    newView.ScaleY = 0.3f;
                    newView.Rotation = -1080f; // Start rotated 3 full rotations (1080 degrees)
                    // Set pivot to center for rotation and zoom
                    newView.PivotX = container.Width / 2f;
                    newView.PivotY = container.Height / 2f;
                    break;
            }
        }

        private void ApplyPushAnimation(AView newView, AView oldView, FrameLayout container, AnimationType transition, Page page, Action onComplete)
        {
            // Get custom speed and easing from page properties
            var customSpeed = LightNavigationPage.GetTransitionSpeed(page);
            var customEasing = LightNavigationPage.GetTransitionEasing(page);

            // Use custom speed if set (> 0), otherwise use default duration
            var duration = customSpeed > 0 ? customSpeed :
                          (transition == AnimationType.WhirlIn3 ? WHIRL3_DURATION_MS : ANIMATION_IN_DURATION_MS);

            // Create interpolator based on custom easing or use default
            var interpolator = GetInterpolatorForEasing(customEasing, isForward: true);

            var animator = newView.Animate()
                .SetDuration(duration)
                .SetInterpolator(interpolator)
                .WithEndAction(new Java.Lang.Runnable(onComplete));

            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                case AnimationType.SlideFromLeft:
                case AnimationType.SlideFromBottom:
                case AnimationType.SlideFromTop:
                    animator.Alpha(1f).TranslationX(0f).TranslationY(0f);
                    break;

                case AnimationType.ParallaxSlideFromRight:
                    animator.Alpha(1f).TranslationX(0f);
                    oldView.Animate()
                        .TranslationX(-container.Width * 0.3f)
                        .SetDuration(duration)
                        .SetInterpolator(interpolator)
                        .Start();
                    break;

                case AnimationType.ParallaxSlideFromLeft:
                    animator.Alpha(1f).TranslationX(0f);
                    oldView.Animate()
                        .TranslationX(container.Width * 0.3f)
                        .SetDuration(duration)
                        .SetInterpolator(interpolator)
                        .Start();
                    break;

                case AnimationType.Fade:
                    animator.Alpha(1f);
                    break;

                case AnimationType.ZoomIn:
                case AnimationType.ZoomOut:
                    animator.Alpha(1f).ScaleX(1f).ScaleY(1f);
                    break;

                case AnimationType.WhirlIn:
                case AnimationType.WhirlIn3:
                    animator.Alpha(1f).ScaleX(1f).ScaleY(1f).Rotation(0f);
                    break;
            }

            animator.Start();
        }

        private void ApplyPopAnimationStart(AView oldView, AView newView, FrameLayout container, AnimationType transition)
        {
            // Reset views to visible state
            oldView.TranslationX = 0;
            oldView.TranslationY = 0;
            oldView.Alpha = 1;
            oldView.ScaleX = 1;
            oldView.ScaleY = 1;
            oldView.Visibility = ViewStates.Visible;
            oldView.BringToFront();

            newView.TranslationX = 0;
            newView.TranslationY = 0;
            newView.Alpha = 1;
            newView.ScaleX = 1;
            newView.ScaleY = 1;
            newView.Visibility = ViewStates.Visible;

            // Position newView based on transition (it's the page we're returning to)
            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                case AnimationType.ParallaxSlideFromRight:
                    newView.TranslationX = -container.Width * 0.3f;
                    break;

                case AnimationType.SlideFromLeft:
                case AnimationType.ParallaxSlideFromLeft:
                    newView.TranslationX = container.Width * 0.3f;
                    break;

                case AnimationType.SlideFromBottom:
                    newView.TranslationY = -container.Height * 0.3f;
                    break;

                case AnimationType.SlideFromTop:
                    newView.TranslationY = container.Height * 0.3f;
                    break;

                case AnimationType.Fade:
                    newView.Alpha = 0.5f;
                    break;

                case AnimationType.ZoomIn:
                    newView.ScaleX = 1.15f;
                    newView.ScaleY = 1.15f;
                    newView.Alpha = 0.5f;
                    // Set pivot to center for zoom
                    newView.PivotX = container.Width / 2f;
                    newView.PivotY = container.Height / 2f;
                    break;

                case AnimationType.ZoomOut:
                    newView.ScaleX = 0.85f;
                    newView.ScaleY = 0.85f;
                    newView.Alpha = 0.5f;
                    // Set pivot to center for zoom
                    newView.PivotX = container.Width / 2f;
                    newView.PivotY = container.Height / 2f;
                    break;

                case AnimationType.WhirlIn:
                    newView.ScaleX = 1.3f;
                    newView.ScaleY = 1.3f;
                    newView.Rotation = 180f; // Rotate 180 degrees clockwise (reverse of push)
                    newView.Alpha = 0.5f;
                    // Set pivot to center for rotation and zoom
                    newView.PivotX = container.Width / 2f;
                    newView.PivotY = container.Height / 2f;
                    break;

                case AnimationType.WhirlIn3:
                    newView.ScaleX = 1.3f;
                    newView.ScaleY = 1.3f;
                    newView.Rotation = 1080f; // Rotate 3 full rotations clockwise (reverse of push)
                    newView.Alpha = 0.5f;
                    // Set pivot to center for rotation and zoom
                    newView.PivotX = container.Width / 2f;
                    newView.PivotY = container.Height / 2f;
                    break;
            }
        }

        private void ApplyPopAnimation(AView oldView, AView newView, FrameLayout container, AnimationType transition, Page page, Action onComplete)
        {
            // Get custom speed and easing from page properties
            var customSpeed = LightNavigationPage.GetTransitionSpeed(page);
            var customEasing = LightNavigationPage.GetTransitionEasing(page);

            // Use custom speed if set (> 0), otherwise use default duration
            var duration = customSpeed > 0 ? customSpeed :
                          (transition == AnimationType.WhirlIn3 ? WHIRL3_DURATION_MS : ANIMATION_OUT_DURATION_MS);

            // Create interpolator based on custom easing or use default for pop (backwards)
            var interpolator = GetInterpolatorForEasing(customEasing, isForward: false);

            // Set pivot points for zoom/rotation animations BEFORE creating animators
            if (transition == AnimationType.ZoomIn || transition == AnimationType.ZoomOut ||
                transition == AnimationType.WhirlIn || transition == AnimationType.WhirlIn3)
            {
                oldView.PivotX = container.Width / 2f;
                oldView.PivotY = container.Height / 2f;
                newView.PivotX = container.Width / 2f;
                newView.PivotY = container.Height / 2f;
            }

            var oldAnimator = oldView.Animate()
                .SetDuration(duration)
                .SetInterpolator(interpolator)
                .WithEndAction(new Java.Lang.Runnable(onComplete));

            var newAnimator = newView.Animate()
                .SetDuration(duration)
                .SetInterpolator(interpolator);

            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                case AnimationType.ParallaxSlideFromRight:
                    oldAnimator.Alpha(0f).TranslationX(container.Width * 0.3f);
                    newAnimator.TranslationX(0f);
                    break;

                case AnimationType.SlideFromLeft:
                case AnimationType.ParallaxSlideFromLeft:
                    oldAnimator.Alpha(0f).TranslationX(-container.Width * 0.3f);
                    newAnimator.TranslationX(0f);
                    break;

                case AnimationType.SlideFromBottom:
                    oldAnimator.Alpha(0f).TranslationY(container.Height * 0.3f);
                    newAnimator.TranslationY(0f);
                    break;

                case AnimationType.SlideFromTop:
                    oldAnimator.Alpha(0f).TranslationY(-container.Height * 0.3f);
                    newAnimator.TranslationY(0f);
                    break;

                case AnimationType.Fade:
                    oldAnimator.Alpha(0f);
                    newAnimator.Alpha(1f);
                    break;

                case AnimationType.ZoomIn:
                    oldAnimator.Alpha(0f).ScaleX(0.85f).ScaleY(0.85f);
                    newAnimator.Alpha(1f).ScaleX(1f).ScaleY(1f);
                    break;

                case AnimationType.ZoomOut:
                    oldAnimator.Alpha(0f).ScaleX(1.15f).ScaleY(1.15f);
                    newAnimator.Alpha(1f).ScaleX(1f).ScaleY(1f);
                    break;

                case AnimationType.WhirlIn:
                    oldAnimator.Alpha(0f).ScaleX(0.3f).ScaleY(0.3f).Rotation(-180f);
                    newAnimator.Alpha(1f).ScaleX(1f).ScaleY(1f).Rotation(0f);
                    break;

                case AnimationType.WhirlIn3:
                    oldAnimator.Alpha(0f).ScaleX(0.3f).ScaleY(0.3f).Rotation(-1080f);
                    newAnimator.Alpha(1f).ScaleX(1f).ScaleY(1f).Rotation(0f);
                    break;
            }

            oldAnimator.Start();
            newAnimator.Start();
        }

        #endregion
    }
}
#endif
