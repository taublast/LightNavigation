#if WINDOWS
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Debug = System.Diagnostics.Debug;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using MauiPage = Microsoft.Maui.Controls.Page;
using MauiNavigationPage = Microsoft.Maui.Controls.NavigationPage;
using WVisibility = Microsoft.UI.Xaml.Visibility;

namespace LightNavigation.Platform
{
    /// <summary>
    /// COMPLETELY CUSTOM NavigationPage handler for Windows that eliminates black flashing.
    ///
    /// WHY: Default MAUI navigation on Windows shows black flashing during page transitions
    /// because old page is removed before new page is fully rendered.
    ///
    /// OUR APPROACH:
    /// 1. Use a simple Grid container (no complex navigation controls)
    /// 2. Convert MAUI pages directly to Windows FrameworkElements
    /// 3. Add new element to container (both elements visible)
    /// 4. Wait for new element to render
    /// 5. Remove old element
    /// 6. NO BLACK FLASH!
    /// </summary>
    public class LightNavigationViewHandler : ViewHandler<MauiNavigationPage, WGrid>
    {
        private const string TAG = "[LightNavigation_Windows]";
        private readonly List<FrameworkElement> _viewStack = new();
        private readonly List<MauiPage> _pageStack = new();
        private FrameworkElement? _currentView;

        // Animation settings
        private const int ANIMATION_IN_DURATION_MS = 200;
        private const int ANIMATION_OUT_DURATION_MS = 150;
        private const int WHIRL3_DURATION_MS = 400; // Longer duration for 3-rotation whirl effect

        // Navigation operation queue to prevent concurrent operations and ensure smooth transitions
        private readonly Queue<Func<Task>> _navigationQueue = new();
        private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);
        private bool _isProcessingQueue = false;

        public LightNavigationViewHandler() : base(PropertyMapper, CommandMapper)
        {
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

        public static IPropertyMapper<MauiNavigationPage, LightNavigationViewHandler> PropertyMapper =
            new PropertyMapper<MauiNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewMapper)
            {
            };

        public static CommandMapper<MauiNavigationPage, LightNavigationViewHandler> CommandMapper =
            new CommandMapper<MauiNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewCommandMapper)
            {
                [nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation
            };

        protected override WGrid CreatePlatformView()
        {
            Debug.WriteLine($"{TAG} 🔨 CreatePlatformView called");

            // Create a simple Grid container - NO complex navigation controls!
            var container = new WGrid
            {
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch
            };

            // Set background to transparent
            container.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Transparent);

            Debug.WriteLine($"{TAG} ✅ Created Grid container");

            return container;
        }

        protected override void ConnectHandler(WGrid platformView)
        {
            base.ConnectHandler(platformView);
            Debug.WriteLine($"{TAG} 🔗 ConnectHandler called");

            // Show the initial page
            if (VirtualView?.CurrentPage != null)
            {
                Debug.WriteLine($"{TAG} 📄 Showing initial page: {VirtualView.CurrentPage.GetType().Name}");
                // Initial page will be shown via MapRequestNavigation
            }
        }

        protected override void DisconnectHandler(WGrid platformView)
        {
            // Clean up views
            platformView.Children.Clear();
            _navigationQueue.Clear();
            _viewStack.Clear();
            _pageStack.Clear();
            _currentView = null;
            _navigationSemaphore?.Dispose();

            base.DisconnectHandler(platformView);
        }

        private static void MapRequestNavigation(LightNavigationViewHandler handler, MauiNavigationPage view, object? args)
        {
            if (args is not NavigationRequest request)
            {
                Debug.WriteLine($"{TAG} ⚠️ MapRequestNavigation called with invalid args");
                return;
            }

            Debug.WriteLine(
                $"{TAG} 🔄 MapRequestNavigation called - NavigationStack count: {request.NavigationStack.Count}");

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
                        var newPage = newStack[newStack.Count - 1] as MauiPage;
                        if (newPage != null)
                        {
                            // Get the effective transition for this page
                            var transition = LightNavigationPage.GetEffectiveTransition(newPage);

                            if (currentStackCount == 0)
                            {
                                Debug.WriteLine($"{TAG} ➡️ Initial page: {newPage.GetType().Name}");
                                await ShowPageAsync(newPage, false, transition, isInitial: true);
                            }
                            else
                            {
                                Debug.WriteLine($"{TAG} ➡️ Push: {newPage.GetType().Name}, Transition: {transition}");
                                await ShowPageAsync(newPage, request.Animated, transition, isInitial: false);
                            }
                        }
                    }
                    else if (newStack.Count < currentStackCount)
                    {
                        // Pop
                        var diff = currentStackCount - newStack.Count;
                        Debug.WriteLine($"{TAG} ⬅️ Pop {diff} page(s)");

                        // Get the transition from the page being popped (last page in current stack)
                        var poppingPage = _pageStack.LastOrDefault();
                        var transition = poppingPage != null
                            ? LightNavigationPage.GetEffectiveTransition(poppingPage)
                            : AnimationType.Default;

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

        /// <summary>
        /// Gets the appropriate Windows easing function for the specified easing type.
        /// </summary>
        /// <param name="easing">The easing type (Default = use built-in behavior).</param>
        /// <param name="isForward">True for push animations (use EaseOut), false for pop animations (use EaseIn).</param>
        /// <returns>The Windows EasingFunctionBase to use.</returns>
        private Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase GetEasingForTransition(TransitionEasing easing, bool isForward)
        {
            // If Default (0), use the standard behavior based on direction
            if (easing == TransitionEasing.Default)
            {
                return isForward
                    ? new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    : new QuadraticEase { EasingMode = EasingMode.EaseIn };
            }

            // Otherwise, use the specified easing type
            return easing switch
            {
                TransitionEasing.Linear => new QuadraticEase { EasingMode = EasingMode.EaseInOut }, // Closest to linear in Windows
                TransitionEasing.Decelerate => new QuadraticEase { EasingMode = EasingMode.EaseOut },
                TransitionEasing.Accelerate => new QuadraticEase { EasingMode = EasingMode.EaseIn },
                TransitionEasing.AccelerateDecelerate => new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                _ => isForward
                    ? new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    : new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
        }

        private void ResetViewTransform(FrameworkElement view)
        {
            view.Opacity = 1;
            if (view.RenderTransform is Microsoft.UI.Xaml.Media.CompositeTransform transform)
            {
                transform.TranslateX = 0;
                transform.TranslateY = 0;
                transform.ScaleX = 1;
                transform.ScaleY = 1;
                transform.Rotation = 0;
            }
        }

        private Storyboard CreatePushAnimationStoryboard(FrameworkElement newView, FrameworkElement oldView, WGrid container,
            AnimationType transition, int duration, Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase easingFunction)
        {
            var storyboard = new Storyboard();
            var newTransform = newView.RenderTransform as Microsoft.UI.Xaml.Media.CompositeTransform;

            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "TranslateX", null, 0, duration, easingFunction));
                    break;

                case AnimationType.SlideFromLeft:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "TranslateX", null, 0, duration, easingFunction));
                    break;

                case AnimationType.SlideFromBottom:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "TranslateY", null, 0, duration, easingFunction));
                    break;

                case AnimationType.SlideFromTop:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "TranslateY", null, 0, duration, easingFunction));
                    break;

                case AnimationType.ParallaxSlideFromRight:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "TranslateX", null, 0, duration, easingFunction));
                    // Parallax effect on old view
                    if (oldView.RenderTransform is Microsoft.UI.Xaml.Media.CompositeTransform oldTransform)
                    {
                        storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "TranslateX", null, -container.ActualWidth * 0.3, duration, easingFunction));
                    }
                    break;

                case AnimationType.ParallaxSlideFromLeft:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "TranslateX", null, 0, duration, easingFunction));
                    // Parallax effect on old view
                    if (oldView.RenderTransform is Microsoft.UI.Xaml.Media.CompositeTransform oldTransform2)
                    {
                        storyboard.Children.Add(CreateDoubleAnimation(oldTransform2, "TranslateX", null, container.ActualWidth * 0.3, duration, easingFunction));
                    }
                    break;

                case AnimationType.Fade:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    break;

                case AnimationType.ZoomIn:
                case AnimationType.ZoomOut:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "ScaleX", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "ScaleY", null, 1.0, duration, easingFunction));
                    break;

                case AnimationType.WhirlIn:
                case AnimationType.WhirlIn3:
                    storyboard.Children.Add(CreateDoubleAnimation(newView, "Opacity", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "ScaleX", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "ScaleY", null, 1.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(newTransform, "Rotation", null, 0, duration, easingFunction));
                    break;
            }

            return storyboard;
        }

        private DoubleAnimation CreateDoubleAnimation(DependencyObject target, string propertyPath, double? from, double to,
            int duration, Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase easingFunction)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
                EasingFunction = easingFunction
            };

            if (from.HasValue)
            {
                animation.From = from.Value;
            }

            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, propertyPath);

            return animation;
        }

        private Storyboard CreatePopAnimationStoryboard(FrameworkElement oldView, FrameworkElement newView, WGrid container,
            AnimationType transition, int duration, Microsoft.UI.Xaml.Media.Animation.EasingFunctionBase easingFunction)
        {
            var storyboard = new Storyboard();
            var oldTransform = oldView.RenderTransform as Microsoft.UI.Xaml.Media.CompositeTransform;

            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                case AnimationType.ParallaxSlideFromRight:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "TranslateX", null, container.ActualWidth * 0.3, duration, easingFunction));
                    break;

                case AnimationType.SlideFromLeft:
                case AnimationType.ParallaxSlideFromLeft:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "TranslateX", null, -container.ActualWidth * 0.3, duration, easingFunction));
                    break;

                case AnimationType.SlideFromBottom:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "TranslateY", null, container.ActualHeight * 0.3, duration, easingFunction));
                    break;

                case AnimationType.SlideFromTop:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "TranslateY", null, -container.ActualHeight * 0.3, duration, easingFunction));
                    break;

                case AnimationType.Fade:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    break;

                case AnimationType.ZoomIn:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "ScaleX", null, 0.85, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "ScaleY", null, 0.85, duration, easingFunction));
                    break;

                case AnimationType.ZoomOut:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "ScaleX", null, 1.15, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "ScaleY", null, 1.15, duration, easingFunction));
                    break;

                case AnimationType.WhirlIn:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "ScaleX", null, 0.3, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "ScaleY", null, 0.3, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "Rotation", null, -180, duration, easingFunction));
                    break;

                case AnimationType.WhirlIn3:
                    storyboard.Children.Add(CreateDoubleAnimation(oldView, "Opacity", null, 0.0, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "ScaleX", null, 0.3, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "ScaleY", null, 0.3, duration, easingFunction));
                    storyboard.Children.Add(CreateDoubleAnimation(oldTransform, "Rotation", null, -1080, duration, easingFunction));
                    break;
            }

            return storyboard;
        }

        private void ApplyPushAnimationStart(FrameworkElement newView, FrameworkElement oldView, WGrid container, AnimationType transition)
        {
            // Ensure views have appropriate transforms
            if (newView.RenderTransform == null || newView.RenderTransform is not Microsoft.UI.Xaml.Media.CompositeTransform)
            {
                newView.RenderTransform = new Microsoft.UI.Xaml.Media.CompositeTransform();
            }

            var transform = (Microsoft.UI.Xaml.Media.CompositeTransform)newView.RenderTransform;

            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                case AnimationType.ParallaxSlideFromRight:
                    newView.Opacity = 0.3;
                    transform.TranslateX = container.ActualWidth * 0.15;
                    break;

                case AnimationType.SlideFromLeft:
                case AnimationType.ParallaxSlideFromLeft:
                    newView.Opacity = 0.3;
                    transform.TranslateX = -container.ActualWidth * 0.15;
                    break;

                case AnimationType.SlideFromBottom:
                    newView.Opacity = 0.3;
                    transform.TranslateY = container.ActualHeight * 0.15;
                    break;

                case AnimationType.SlideFromTop:
                    newView.Opacity = 0.3;
                    transform.TranslateY = -container.ActualHeight * 0.15;
                    break;

                case AnimationType.Fade:
                    newView.Opacity = 0;
                    break;

                case AnimationType.ZoomIn:
                    newView.Opacity = 0;
                    transform.ScaleX = 0.85;
                    transform.ScaleY = 0.85;
                    transform.CenterX = container.ActualWidth / 2;
                    transform.CenterY = container.ActualHeight / 2;
                    break;

                case AnimationType.ZoomOut:
                    newView.Opacity = 0;
                    transform.ScaleX = 1.15;
                    transform.ScaleY = 1.15;
                    transform.CenterX = container.ActualWidth / 2;
                    transform.CenterY = container.ActualHeight / 2;
                    break;

                case AnimationType.WhirlIn:
                    newView.Opacity = 0;
                    transform.ScaleX = 0.3;
                    transform.ScaleY = 0.3;
                    transform.Rotation = -180; // Start rotated 180 degrees
                    transform.CenterX = container.ActualWidth / 2;
                    transform.CenterY = container.ActualHeight / 2;
                    break;

                case AnimationType.WhirlIn3:
                    newView.Opacity = 0;
                    transform.ScaleX = 0.3;
                    transform.ScaleY = 0.3;
                    transform.Rotation = -1080; // Start rotated 3 full rotations (1080 degrees)
                    transform.CenterX = container.ActualWidth / 2;
                    transform.CenterY = container.ActualHeight / 2;
                    break;

                case AnimationType.None:
                    // No initial state change
                    break;
            }
        }

        private Task ShowPageAsync(MauiPage page, bool animate, AnimationType transition, bool isInitial)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                Debug.WriteLine($"{TAG} 📄 ShowPageAsync: {page.GetType().Name}, isInitial: {isInitial}, Transition: {transition}");

                // Convert MAUI page to Windows FrameworkElement
                var newView = page.ToPlatform(MauiContext!);

                if (newView == null)
                {
                    Debug.WriteLine($"{TAG} ❌ ToPlatform returned null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                Debug.WriteLine($"{TAG} ✅ Created Windows element for page");

                var container = PlatformView;
                var oldView = _currentView;
                var oldPage = _pageStack.Count > 0 ? _pageStack.Last() : null;

                var newAware = page as INavigationAware;
                var oldAware = oldPage as INavigationAware;

                newAware?.OnPushing();
                oldAware?.OnPopping();

                if (isInitial || oldView == null)
                {
                    // First page - just add it
                    Debug.WriteLine($"{TAG} 🆕 Adding initial page");
                    container.Children.Add(newView);
                    _currentView = newView;
                    _viewStack.Add(newView);
                    _pageStack.Add(page);
                    oldAware?.OnCovered();
                    newAware?.OnTopmost();
                    tcs.SetResult(true);
                }
                else
                {
                    // Navigation - add new view BEFORE hiding old view
                    Debug.WriteLine($"{TAG} 👁️ Adding new view while keeping old view visible");

                    // ALWAYS ensure views have CompositeTransform for full animation support
                    if (newView.RenderTransform == null || newView.RenderTransform is not Microsoft.UI.Xaml.Media.CompositeTransform)
                    {
                        newView.RenderTransform = new Microsoft.UI.Xaml.Media.CompositeTransform();
                    }
                    if (oldView.RenderTransform == null || oldView.RenderTransform is not Microsoft.UI.Xaml.Media.CompositeTransform)
                    {
                        oldView.RenderTransform = new Microsoft.UI.Xaml.Media.CompositeTransform();
                    }

                    if (animate && transition != AnimationType.None)
                    {
                        // Set initial animation state based on transition type
                        ApplyPushAnimationStart(newView, oldView, container, transition);
                        oldView.Visibility = WVisibility.Visible;
                    }
                    else
                    {
                        // No animation - set initial state
                        newView.Opacity = 1;
                        var transform = (Microsoft.UI.Xaml.Media.CompositeTransform)newView.RenderTransform;
                        transform.TranslateX = 0;
                        transform.TranslateY = 0;
                        transform.ScaleX = 1;
                        transform.ScaleY = 1;
                        transform.Rotation = 0;
                    }

                    Debug.WriteLine($"{TAG} OLDVIEW visibility: {oldView.Visibility}");

                    // Add new view on top (fully visible)
                    container.Children.Add(newView);
                    newView.Visibility = WVisibility.Visible;

                    _viewStack.Add(newView);
                    _pageStack.Add(page);

                    // Wait for new view to be laid out and rendered
                    newView.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        Debug.WriteLine($"{TAG} ✅ New view rendered");

                        if (animate && transition != AnimationType.None)
                        {
                            Debug.WriteLine($"{TAG} 🎬 Starting push animation - {transition}");
                            Debug.WriteLine($"{TAG} OLDVIEW 2 visibility: {oldView.Visibility}");

                            newView.Visibility = WVisibility.Visible;

                            // Get custom speed and easing from page properties
                            var customSpeed = LightNavigationPage.GetTransitionSpeed(page);
                            var customEasing = LightNavigationPage.GetTransitionEasing(page);

                            // Use custom speed if set (> 0), otherwise use default duration
                            var duration = customSpeed > 0 ? customSpeed :
                                          (transition == AnimationType.WhirlIn3 ? WHIRL3_DURATION_MS : ANIMATION_IN_DURATION_MS);

                            // Get easing function based on custom easing or use default
                            var easingFunction = GetEasingForTransition(customEasing, isForward: true);

                            // Create and configure animations based on transition type
                            var storyboard = CreatePushAnimationStoryboard(newView, oldView, container, transition, duration, easingFunction);

                            storyboard.Completed += (s, e) =>
                            {
                                // AFTER animation completes, reset and hide old view
                                ResetViewTransform(newView);
                                oldView.Visibility = WVisibility.Collapsed;
                                Debug.WriteLine($"{TAG} ✅ Animation complete - old view hidden");

                                newAware?.OnTopmost();
                                oldAware?.OnCovered();
                                tcs.SetResult(true);
                            };

                            storyboard.Begin();
                        }
                        else
                        {
                            // No animation - hide old view immediately
                            oldView.Visibility = WVisibility.Collapsed;

                            newAware?.OnTopmost();
                            oldAware?.OnCovered();
                            tcs.SetResult(true);
                        }
                    });

                    _currentView = newView;
                }

                Debug.WriteLine($"{TAG} 📊 Stack count: {_viewStack.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in ShowPageAsync: {ex.Message}");
                Debug.WriteLine($"{TAG} Stack trace: {ex.StackTrace}");
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
                    var newView = _viewStack[_viewStack.Count - 2];
                    var oldView = _viewStack.Last();
                    var newPage = _pageStack[_pageStack.Count - 2];
                    var oldPage = _pageStack.Last();

                    var newAware = newPage as INavigationAware;
                    var oldAware = oldPage as INavigationAware;

                    oldAware?.OnPopping();

                    // ALWAYS ensure both views have CompositeTransform for full animation support
                    if (oldView.RenderTransform == null || oldView.RenderTransform is not Microsoft.UI.Xaml.Media.CompositeTransform)
                    {
                        oldView.RenderTransform = new Microsoft.UI.Xaml.Media.CompositeTransform();
                    }
                    if (newView.RenderTransform == null || newView.RenderTransform is not Microsoft.UI.Xaml.Media.CompositeTransform)
                    {
                        newView.RenderTransform = new Microsoft.UI.Xaml.Media.CompositeTransform();
                    }

                    // ALWAYS make newView visible and reset its transform (it was hidden when we pushed on top)
                    ResetViewTransform(newView);
                    newView.Visibility = WVisibility.Visible;

                    if (animate && transition != AnimationType.None)
                    {
                        // Prepare old view for animation
                        oldView.Opacity = 1;
                        oldView.Visibility = WVisibility.Visible;
                        ResetViewTransform(oldView);
                    }

                    Debug.WriteLine($"{TAG} OLDVIEW visibility: {oldView.Visibility}");

                    newView.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        if (animate && transition != AnimationType.None)
                        {
                            Debug.WriteLine($"{TAG} 🎬 Starting pop animation - {transition}");
                            oldView.Visibility = WVisibility.Visible;

                            // Get custom speed and easing from oldPage properties (page being removed)
                            var customSpeed = LightNavigationPage.GetTransitionSpeed(oldPage);
                            var customEasing = LightNavigationPage.GetTransitionEasing(oldPage);

                            // Use custom speed if set (> 0), otherwise use default duration
                            var duration = customSpeed > 0 ? customSpeed :
                                          (transition == AnimationType.WhirlIn3 ? WHIRL3_DURATION_MS : ANIMATION_OUT_DURATION_MS);

                            // Get easing function based on custom easing or use default
                            var easingFunction = GetEasingForTransition(customEasing, isForward: false);

                            // Create and configure animations based on transition type
                            var storyboard = CreatePopAnimationStoryboard(oldView, newView, container, transition, duration, easingFunction);

                            storyboard.Completed += (s, e) =>
                            {
                                container.Children.Remove(oldView);
                                ResetViewTransform(newView);
                                newView.Visibility = WVisibility.Visible;
                                Debug.WriteLine($"{TAG} ✅ Pop animation complete!");

                                oldAware?.OnRemoved();
                                newAware?.OnTopmost();
                                tcs.SetResult(true);
                            };

                            storyboard.Begin();
                        }
                        else
                        {
                            // No animation - just remove old view and ensure new view is visible
                            container.Children.Remove(oldView);
                            ResetViewTransform(newView);
                            newView.Visibility = WVisibility.Visible;
                            Debug.WriteLine($"{TAG} ✅ Pop complete");

                            oldAware?.OnRemoved();
                            newAware?.OnTopmost();
                            tcs.SetResult(true);
                        }
                    });

                    _currentView = newView;
                    _viewStack.Remove(oldView);
                    _pageStack.Remove(oldPage);
                }

                Debug.WriteLine($"{TAG} 📊 Stack count after pop: {_viewStack.Count}");
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
                var rootView = _viewStack.First();
                var rootPage = _pageStack.First();

                Debug.WriteLine($"{TAG} 🏠 PopToRoot - showing root view");

                // Make root view visible
                rootView.Visibility = WVisibility.Visible;

                // Remove all other views from container
                var viewsToRemove = _viewStack.Skip(1).ToList();
                var pagesToRemove = _pageStack.Skip(1).ToList();

                rootView.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    foreach (var view in viewsToRemove)
                    {
                        container.Children.Remove(view);
                    }

                    foreach (var page in pagesToRemove)
                    {
                        if (page is INavigationAware oldAware)
                        {
                            oldAware.OnRemoved();
                        }
                    }

                    Debug.WriteLine($"{TAG} ✅ PopToRoot complete");
                    tcs.SetResult(true);
                });

                // Clear stack except root
                _viewStack.Clear();
                _viewStack.Add(rootView);
                _pageStack.Clear();
                _pageStack.Add(rootPage);
                _currentView = rootView;

                if (rootPage is INavigationAware newAware)
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
    }
}
#endif
