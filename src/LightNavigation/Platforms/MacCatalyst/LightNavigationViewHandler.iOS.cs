#if IOS || MACCATALYST
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UIKit;
using Debug = System.Diagnostics.Debug;
using MauiNavigationPage = Microsoft.Maui.Controls.NavigationPage;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace LightNavigation.Platform
{
    /// <summary>
    /// Custom UIView that wraps a UINavigationController.
    /// This allows MAUI to get the ViewController when needed.
    /// </summary>
    public class NavigationView : UIView
    {
        private UINavigationController? _navigationController;

        public UINavigationController? NavigationController
        {
            get => _navigationController;
            set
            {
                if (_navigationController != value)
                {
                    // Remove old navigation controller's view
                    _navigationController?.View.RemoveFromSuperview();

                    _navigationController = value;

                    // Add new navigation controller's view
                    if (_navigationController?.View != null)
                    {
                        AddSubview(_navigationController.View);
                        _navigationController.View.Frame = Bounds;
                        _navigationController.View.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
                    }
                }
            }
        }

        public NavigationView()
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
            BackgroundColor = UIColor.Clear;
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();

            // Ensure navigation controller's view fills this view
            if (_navigationController?.View != null)
            {
                _navigationController.View.Frame = Bounds;
            }
        }
    }

    /// <summary>
    /// Custom NavigationPage handler for iOS that eliminates flashing during navigation.
    ///
    /// CRITICAL: Must implement IPlatformViewHandler.ViewController property
    /// because MAUI's MapPrefersLargeTitles and MapIsNavigationBarTranslucent
    /// cast handler.ViewController to UINavigationController!
    ///
    /// APPROACH:
    /// - Uses UINavigationController for proper iOS navigation
    /// - Wraps it in a NavigationView (UIView) to satisfy ViewHandler<,UIView> constraint
    /// - Implements IPlatformViewHandler.ViewController to return the UINavigationController
    /// - Custom animation to prevent flashing during transitions
    /// </summary>
    public class LightNavigationViewHandler : ViewHandler<LightNavigationPage, NavigationView>, IPlatformViewHandler
    {
        private const string TAG = "[LightNavigation_iOS]";
        private const double ANIMATION_IN_DURATION = 0.3; // 300ms for push
        private const double ANIMATION_OUT_DURATION = 0.3; // 300ms for pop
        private const double WHIRL3_DURATION = 0.4; // 400ms for WhirlIn3

        private UINavigationController? _navigationController;
        private readonly List<UIViewController> _viewControllerStack = new();
        private readonly List<MauiPage> _pageStack = new();

        // Navigation operation queue to prevent concurrent operations and black screens
        private readonly Queue<Func<Task>> _navigationQueue = new();
        private readonly SemaphoreSlim _navigationSemaphore = new(1, 1);
        private bool _isProcessingQueue = false;

        // CRITICAL: IPlatformViewHandler requires this property!
        // MAUI casts this to UINavigationController in MapPrefersLargeTitles/MapIsNavigationBarTranslucent
        public new UIViewController? ViewController => _navigationController;

        public LightNavigationViewHandler() : base(PropertyMapper, CommandMapper)
        {
            var hashCode = GetHashCode();
            Debug.WriteLine($"{TAG} ✅ Handler created - HashCode: {hashCode}");
            Debug.WriteLine($"{TAG} 📋 CommandMapper registered");
        }

        /// <summary>
        /// Enqueues a navigation operation and processes the queue sequentially.
        /// This prevents concurrent navigation operations that cause crashes and black screens.
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

        public static IPropertyMapper<LightNavigationPage, LightNavigationViewHandler> PropertyMapper =
            new PropertyMapper<LightNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewMapper)
            {
                [Microsoft.Maui.Controls.NavigationPage.HasNavigationBarProperty.PropertyName] = MapHasNavigationBar
            };

        public static CommandMapper<LightNavigationPage, LightNavigationViewHandler> CommandMapper =
            new CommandMapper<LightNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewCommandMapper)
            {
                [nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation
            };

        private static void MapHasNavigationBar(LightNavigationViewHandler handler, LightNavigationPage view)
        {
            if (handler._navigationController != null)
            {
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(view);
                handler._navigationController.NavigationBarHidden = !hasNavBar;
                Debug.WriteLine($"{TAG} 🔵 NavigationBar visibility changed: {hasNavBar}");
            }
        }

        protected override NavigationView CreatePlatformView()
        {
            // Create UINavigationController
            _navigationController = new UINavigationController();

            // Set initial NavigationBar visibility based on VirtualView
            // Default is visible unless explicitly hidden
            if (VirtualView != null)
            {
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(VirtualView);
                _navigationController.NavigationBarHidden = !hasNavBar;
                Debug.WriteLine($"{TAG} 🔵 NavigationBar visibility: {hasNavBar}");
            }
            else
            {
                // Default to visible
                _navigationController.NavigationBarHidden = false;
            }

            // Return the navigation controller's View wrapped in NavigationView
            var navigationView = new NavigationView
            {
                NavigationController = _navigationController
            };

            return navigationView;
        }

        protected override void ConnectHandler(NavigationView platformView)
        {
            base.ConnectHandler(platformView);

            // Subscribe to navigation events from the NavigationPage
            if (VirtualView is INavigationPageController navController)
            {
                navController.PushRequested += OnPushRequested;
                navController.PopRequested += OnPopRequested;
                navController.PopToRootRequested += OnPopToRootRequested;
                Debug.WriteLine($"{TAG} ✅ Subscribed to navigation events");
            }
            else
            {
                Debug.WriteLine($"{TAG} ⚠️ VirtualView is NOT INavigationPageController!");
            }

            // Subscribe to property changes for BarTextColor, etc.
            if (VirtualView != null)
            {
                VirtualView.PropertyChanged += OnNavigationPagePropertyChanged;
            }

            // Manually show initial page if we have one
            if (VirtualView?.CurrentPage != null && MauiContext != null)
            {
                _ = EnqueueNavigationAsync(async () =>
                {
                    await ShowPageAsync(VirtualView.CurrentPage, false, AnimationType.Default, isInitial: true);

                    // IMPORTANT: Update navigation bar visibility for the initial page
                    // The property mapper might not have run yet during initial setup
                    if (_navigationController != null && VirtualView != null)
                    {
                        var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(VirtualView);
                        _navigationController.NavigationBarHidden = !hasNavBar;
                        Debug.WriteLine($"{TAG} 🔵 Initial page NavigationBar visibility: {hasNavBar}");
                    }
                });
            }
            else
            {
                Debug.WriteLine($"{TAG} ⚠️ No CurrentPage to show!");
            }

            // Apply initial navbar styling
            UpdateBarTextColor();
        }

        protected override void DisconnectHandler(NavigationView platformView)
        {
            // Unsubscribe from navigation events
            if (VirtualView is INavigationPageController navController)
            {
                navController.PushRequested -= OnPushRequested;
                navController.PopRequested -= OnPopRequested;
                navController.PopToRootRequested -= OnPopToRootRequested;
            }

            // Unsubscribe from property changes
            if (VirtualView != null)
            {
                VirtualView.PropertyChanged -= OnNavigationPagePropertyChanged;
            }

            // Clean up
            _navigationQueue.Clear();
            _viewControllerStack.Clear();
            _pageStack.Clear();
            _navigationController?.View.RemoveFromSuperview();
            _navigationController = null;
            _navigationSemaphore?.Dispose();

            base.DisconnectHandler(platformView);
        }

        private async void OnPushRequested(object? sender, NavigationRequestedEventArgs e)
        {
            var tcs = new TaskCompletionSource<bool>();
            e.Task = tcs.Task;

            await EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var transition = LightNavigationPage.GetEffectiveTransition(e.Page);
                    Debug.WriteLine($"{TAG} 🟢 OnPushRequested - Page: {e.Page.GetType().Name}, Animated: {e.Animated}, Transition: {transition}");
                    await ShowPageAsync(e.Page, e.Animated, transition, isInitial: false);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG} ❌ OnPushRequested error: {ex.Message}");
                    tcs.TrySetException(ex);
                }
            });
        }

        private async void OnPopRequested(object? sender, NavigationRequestedEventArgs e)
        {
            Debug.WriteLine($"{TAG} 🟢 OnPopRequested - Animated: {e.Animated}");

            var tcs = new TaskCompletionSource<bool>();
            e.Task = tcs.Task;

            await EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var poppingPage = _pageStack.LastOrDefault();
                    var transition = poppingPage != null
                        ? LightNavigationPage.GetEffectiveTransition(poppingPage)
                        : AnimationType.Default;

                    await PopPageAsync(e.Animated, transition);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG} ❌ OnPopRequested error: {ex.Message}");
                    tcs.TrySetException(ex);
                }
            });
        }

        private async void OnPopToRootRequested(object? sender, NavigationRequestedEventArgs e)
        {
            Debug.WriteLine($"{TAG} 🟢 OnPopToRootRequested - Animated: {e.Animated}");

            var tcs = new TaskCompletionSource<bool>();
            e.Task = tcs.Task;

            await EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var poppingPage = _pageStack.LastOrDefault();
                    var transition = poppingPage != null
                        ? LightNavigationPage.GetEffectiveTransition(poppingPage)
                        : AnimationType.Default;

                    await PopToRootAsync(e.Animated, transition);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{TAG} ❌ OnPopToRootRequested error: {ex.Message}");
                    tcs.TrySetException(ex);
                }
            });
        }

        private static void MapRequestNavigation(LightNavigationViewHandler handler, LightNavigationPage view, object? args)
        {
            Debug.WriteLine($"{TAG} 🟢 MapRequestNavigation called! Handler HashCode: {handler.GetHashCode()}, args type: {args?.GetType().Name ?? "null"}");

            if (args is not NavigationRequest request)
            {
                Debug.WriteLine($"{TAG} ⚠️ Invalid args - not NavigationRequest");
                return;
            }

            Debug.WriteLine($"{TAG} 🟢 Calling HandleNavigationRequest with {request.NavigationStack.Count} pages, Animated: {request.Animated}");
            handler.HandleNavigationRequest(request);
        }

        private void HandleNavigationRequest(NavigationRequest request)
        {
            _ = EnqueueNavigationAsync(async () =>
            {
                try
                {
                    var newStack = request.NavigationStack;
                    var currentStackCount = _viewControllerStack.Count;

                    Debug.WriteLine($"{TAG} 📊 Current: {currentStackCount}, New: {newStack.Count}");

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

                        // Get the transition from the page being popped (last page in current stack)
                        var poppingPage = _pageStack.LastOrDefault();
                        var transition = poppingPage != null
                            ? LightNavigationPage.GetEffectiveTransition(poppingPage)
                            : AnimationType.Default;

                        if (diff == currentStackCount - 1)
                        {
                            // Pop to root
                            await PopToRootAsync(request.Animated, transition);
                        }
                        else
                        {
                            // Pop one or more pages
                            for (int i = 0; i < diff; i++)
                            {
                                await PopPageAsync(request.Animated, transition);
                            }
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
                    Debug.WriteLine($"{TAG} ❌ Error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Gets the animation duration based on the transition type and page settings.
        /// </summary>
        private double GetAnimationDuration(MauiPage page, bool isPush)
        {
            var customSpeed = LightNavigationPage.GetTransitionSpeed(page);
            if (customSpeed > 0)
            {
                return customSpeed / 1000.0; // Convert ms to seconds
            }

            var transition = LightNavigationPage.GetEffectiveTransition(page);
            if (transition == AnimationType.WhirlIn3)
            {
                return WHIRL3_DURATION;
            }

            return isPush ? ANIMATION_IN_DURATION : ANIMATION_OUT_DURATION;
        }

        /// <summary>
        /// Gets the UIViewAnimationCurve based on the transition easing setting.
        /// </summary>
        private UIViewAnimationCurve GetAnimationCurve(MauiPage page, bool isPush)
        {
            var easing = LightNavigationPage.GetTransitionEasing(page);

            switch (easing)
            {
                case TransitionEasing.Linear:
                    return UIViewAnimationCurve.Linear;
                case TransitionEasing.Decelerate:
                    return UIViewAnimationCurve.EaseOut;
                case TransitionEasing.Accelerate:
                    return UIViewAnimationCurve.EaseIn;
                case TransitionEasing.AccelerateDecelerate:
                    return UIViewAnimationCurve.EaseInOut;
                case TransitionEasing.Default:
                default:
                    // Use iOS standard: EaseOut for push, EaseIn for pop
                    return isPush ? UIViewAnimationCurve.EaseOut : UIViewAnimationCurve.EaseIn;
            }
        }

        /// <summary>
        /// Applies the initial position/state for the new view before push animation.
        /// </summary>
        private void ApplyPushAnimationStart(UIView newView, UIView oldView, CoreGraphics.CGRect containerBounds, AnimationType transition)
        {
            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                    // Start off-screen to the right
                    newView.Frame = new CoreGraphics.CGRect(
                        containerBounds.Width,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.SlideFromLeft:
                    // Start off-screen to the left
                    newView.Frame = new CoreGraphics.CGRect(
                        -containerBounds.Width,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.SlideFromBottom:
                    // Start off-screen at the bottom
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        containerBounds.Height,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.SlideFromTop:
                    // Start off-screen at the top
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        -containerBounds.Height,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.ParallaxSlideFromRight:
                    // New view starts off-screen to the right
                    newView.Frame = new CoreGraphics.CGRect(
                        containerBounds.Width,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    // Old view starts at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.ParallaxSlideFromLeft:
                    // New view starts off-screen to the left
                    newView.Frame = new CoreGraphics.CGRect(
                        -containerBounds.Width,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    // Old view starts at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.Fade:
                    // New view at center but transparent
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 0;
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Alpha = 1;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.ZoomIn:
                    // New view at center but scaled down
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 0;
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeScale(0.3f, 0.3f);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.ZoomOut:
                    // New view at center but scaled up
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 0;
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeScale(1.5f, 1.5f);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.WhirlIn:
                    // New view at center but scaled down and rotated 180 degrees
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 0;
                    var whirlTransform = CoreGraphics.CGAffineTransform.MakeRotation((float)(-Math.PI)); // -180 degrees
                    whirlTransform = CoreGraphics.CGAffineTransform.Scale(whirlTransform, 0.3f, 0.3f);
                    newView.Transform = whirlTransform;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.WhirlIn3:
                    // For WhirlIn3, we need to use layer animation for rotation to preserve the 3 full rotations
                    // Transform-based rotation gets optimized by iOS to shortest path (0 degrees)
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 0;
                    // Start with scale only, rotation will be handled by layer animation
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeScale(0.3f, 0.3f);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.None:
                    // No animation - just position at center
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;
            }
        }

        /// <summary>
        /// Creates the push animation action.
        /// </summary>
        private Action CreatePushAnimation(UIView newView, UIView oldView, CoreGraphics.CGRect containerBounds, AnimationType transition)
        {
            return () =>
            {
                switch (transition)
                {
                    case AnimationType.Default:
                    case AnimationType.SlideFromRight:
                        // Slide new view to center
                        newView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        // Old view stays in place
                        break;

                    case AnimationType.SlideFromLeft:
                        // Slide new view from left to center
                        newView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.SlideFromBottom:
                        // Slide new view from bottom to center
                        newView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.SlideFromTop:
                        // Slide new view from top to center
                        newView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.ParallaxSlideFromRight:
                        // Slide new view to center
                        newView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        // Slide old view to the left (parallax effect)
                        oldView.Frame = new CoreGraphics.CGRect(
                            -containerBounds.Width * 0.3,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.ParallaxSlideFromLeft:
                        // Slide new view to center
                        newView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        // Slide old view to the right (parallax effect)
                        oldView.Frame = new CoreGraphics.CGRect(
                            containerBounds.Width * 0.3,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.Fade:
                        // Fade in new view, fade out old view
                        newView.Alpha = 1;
                        oldView.Alpha = 0;
                        break;

                    case AnimationType.ZoomIn:
                        // Zoom in new view and fade in
                        newView.Alpha = 1;
                        newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                        break;

                    case AnimationType.ZoomOut:
                        // Zoom out new view to normal and fade in
                        newView.Alpha = 1;
                        newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                        break;

                    case AnimationType.WhirlIn:
                        // Rotate to 0 degrees, scale to 1, and fade in
                        newView.Alpha = 1;
                        newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                        break;

                    case AnimationType.WhirlIn3:
                        // Rotate to 0 degrees, scale to 1, and fade in
                        newView.Alpha = 1;
                        newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                        break;

                    case AnimationType.None:
                        // No animation
                        break;
                }
            };
        }

        /// <summary>
        /// Applies the initial position/state for pop animation (reverse of push).
        /// </summary>
        private void ApplyPopAnimationStart(UIView oldView, UIView newView, CoreGraphics.CGRect containerBounds, AnimationType transition)
        {
            switch (transition)
            {
                case AnimationType.Default:
                case AnimationType.SlideFromRight:
                    // Old view at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    // New (previous) view at left (will be revealed)
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    break;

                case AnimationType.SlideFromLeft:
                    // Old view at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    // New (previous) view at center
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    break;

                case AnimationType.SlideFromBottom:
                    // Old view at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    break;

                case AnimationType.SlideFromTop:
                    // Old view at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    break;

                case AnimationType.ParallaxSlideFromRight:
                    // Old view at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    // New (previous) view slightly to the left
                    newView.Frame = new CoreGraphics.CGRect(
                        -containerBounds.Width * 0.3,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    break;

                case AnimationType.ParallaxSlideFromLeft:
                    // Old view at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    // New (previous) view slightly to the right
                    newView.Frame = new CoreGraphics.CGRect(
                        containerBounds.Width * 0.3,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    break;

                case AnimationType.Fade:
                    // Both at center
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Alpha = 1;
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 0;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.ZoomIn:
                    // Reverse: old view will zoom out and fade out
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Alpha = 1;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.ZoomOut:
                    // Reverse: old view will zoom in and fade out
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Alpha = 1;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.WhirlIn:
                case AnimationType.WhirlIn3:
                    // Reverse: old view will whirl out
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Alpha = 1;
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Alpha = 1;
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;

                case AnimationType.None:
                    // No animation
                    oldView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    oldView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    newView.Frame = new CoreGraphics.CGRect(
                        0,
                        0,
                        containerBounds.Width,
                        containerBounds.Height);
                    newView.Transform = CoreGraphics.CGAffineTransform.MakeIdentity();
                    break;
            }
        }

        /// <summary>
        /// Creates the pop animation action (reverse of push).
        /// </summary>
        private Action CreatePopAnimation(UIView oldView, UIView newView, CoreGraphics.CGRect containerBounds, AnimationType transition)
        {
            return () =>
            {
                switch (transition)
                {
                    case AnimationType.Default:
                    case AnimationType.SlideFromRight:
                        // Slide old view out to the right
                        oldView.Frame = new CoreGraphics.CGRect(
                            containerBounds.Width,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.SlideFromLeft:
                        // Slide old view out to the left
                        oldView.Frame = new CoreGraphics.CGRect(
                            -containerBounds.Width,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.SlideFromBottom:
                        // Slide old view out to the bottom
                        oldView.Frame = new CoreGraphics.CGRect(
                            0,
                            containerBounds.Height,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.SlideFromTop:
                        // Slide old view out to the top
                        oldView.Frame = new CoreGraphics.CGRect(
                            0,
                            -containerBounds.Height,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.ParallaxSlideFromRight:
                        // Slide old view out to the right
                        oldView.Frame = new CoreGraphics.CGRect(
                            containerBounds.Width,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        // Slide new view back to center
                        newView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.ParallaxSlideFromLeft:
                        // Slide old view out to the left
                        oldView.Frame = new CoreGraphics.CGRect(
                            -containerBounds.Width,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        // Slide new view back to center
                        newView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);
                        break;

                    case AnimationType.Fade:
                        // Fade out old view, fade in new view
                        oldView.Alpha = 0;
                        newView.Alpha = 1;
                        break;

                    case AnimationType.ZoomIn:
                        // Reverse: zoom out old view and fade out
                        oldView.Alpha = 0;
                        oldView.Transform = CoreGraphics.CGAffineTransform.MakeScale(0.3f, 0.3f);
                        break;

                    case AnimationType.ZoomOut:
                        // Reverse: zoom in old view and fade out
                        oldView.Alpha = 0;
                        oldView.Transform = CoreGraphics.CGAffineTransform.MakeScale(1.5f, 1.5f);
                        break;

                    case AnimationType.WhirlIn:
                        // Reverse: whirl out with 180 degree rotation
                        oldView.Alpha = 0;
                        var whirlOutTransform = CoreGraphics.CGAffineTransform.MakeRotation((float)Math.PI); // 180 degrees
                        whirlOutTransform = CoreGraphics.CGAffineTransform.Scale(whirlOutTransform, 0.3f, 0.3f);
                        oldView.Transform = whirlOutTransform;
                        break;

                    case AnimationType.WhirlIn3:
                        // Reverse: whirl out with 1080 degree rotation
                        oldView.Alpha = 0;
                        var whirl3OutTransform = CoreGraphics.CGAffineTransform.MakeRotation((float)(Math.PI * 6)); // 1080 degrees
                        whirl3OutTransform = CoreGraphics.CGAffineTransform.Scale(whirl3OutTransform, 0.3f, 0.3f);
                        oldView.Transform = whirl3OutTransform;
                        break;

                    case AnimationType.None:
                        // No animation
                        break;
                }
            };
        }

        private Task ShowPageAsync(MauiPage page, bool animate, AnimationType transition, bool isInitial)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                Debug.WriteLine($"{TAG} 🔵 ShowPageAsync called: page={page.GetType().Name}, animate={animate}, transition={transition}, isInitial={isInitial}");

                if (MauiContext == null)
                {
                    Debug.WriteLine($"{TAG} ❌ MauiContext is null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                if (_navigationController == null)
                {
                    Debug.WriteLine($"{TAG} ❌ NavigationController is null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                // IMPORTANT: Get the page's ViewController (not just the View)
                // This ensures proper navigation bar integration with title, back button, etc.
                UIViewController? viewController = null;
                UIView? pageView = null;

                // Try to get the existing ViewController from the page's handler
                if (page.Handler is IPlatformViewHandler handler && handler.ViewController is UIViewController existingVC)
                {
                    viewController = existingVC;
                    pageView = existingVC.View;
                    Debug.WriteLine($"{TAG} 🔵 Using existing ViewController from page handler");
                }
                else
                {
                    // Fallback: Create view and wrap in ViewController
                    pageView = page.ToPlatform(MauiContext);
                    Debug.WriteLine($"{TAG} 🔵 pageView created: {pageView != null}");

                    if (pageView == null)
                    {
                        Debug.WriteLine($"{TAG} ❌ ToPlatform returned null!");
                        tcs.SetResult(false);
                        return tcs.Task;
                    }

                    viewController = new UIViewController();
                    viewController.View = pageView;

                    Debug.WriteLine($"{TAG} 🔵 Created new ViewController wrapper");
                }

                // Set the navigation item title from the page
                if (viewController != null && !string.IsNullOrEmpty(page.Title))
                {
                    viewController.NavigationItem.Title = page.Title;
                    Debug.WriteLine($"{TAG} 🔵 Set navigation title: {page.Title}");
                }

                // CRITICAL FIX: MAUI's ToPlatform() doesn't always transfer BackgroundColor to the native view
                // We need to manually apply it from the MAUI page
                if (pageView != null && page.BackgroundColor != null)
                {
                    var mauiBgColor = page.BackgroundColor;
                    var nativeColor = mauiBgColor.ToPlatform();
                    pageView.BackgroundColor = nativeColor;
                    Debug.WriteLine($"{TAG} 🔵 Applied background color: {mauiBgColor} -> {nativeColor}");
                }
                else
                {
                    Debug.WriteLine($"{TAG} ⚠️ Page BackgroundColor is null, using default");
                }

                // Get old page and invoke INavigationAware
                var oldPage = _pageStack.Count > 0 ? _pageStack.Last() : null;
                var newAware = page as INavigationAware;
                var oldAware = oldPage as INavigationAware;

                newAware?.OnPushing();
                oldAware?.OnPopping();

                if (isInitial)
                {
                    // Set as root - no animation
                    _navigationController.SetViewControllers(new[] { viewController }, false);
                    _viewControllerStack.Add(viewController);
                    _pageStack.Add(page);
                    oldAware?.OnCovered();
                    newAware?.OnTopmost();

                    tcs.SetResult(true);
                }
                else
                {
                    // Check if we should use native iOS animation or custom animation
                    if (transition == AnimationType.Default)
                    {
                        // Use native UINavigationController animation (includes navigation bar animation)
                        Debug.WriteLine($"{TAG} 🔵 Using native iOS push animation");
                        _navigationController.PushViewController(viewController, animate);
                        _viewControllerStack.Add(viewController);
                        _pageStack.Add(page);

                        // Update navbar visibility based on the page's attached property
                        UpdateNavigationBarVisibility(page);

                        newAware?.OnTopmost();
                        oldAware?.OnCovered();

                        tcs.SetResult(true);
                    }
                    else if (animate && transition != AnimationType.None)
                    {
                        // Custom transition - do manual animation
                        var oldView = _navigationController.TopViewController?.View;

                        if (oldView != null && _navigationController.View != null && pageView != null)
                        {
                            var containerBounds = oldView.Frame;
                            var animationContainer = _navigationController.View;

                            // CRITICAL: Force navbar to stay visible during custom animation
                            // iOS auto-hides navbar when we manipulate view hierarchy
                            Debug.WriteLine($"{TAG} 🔵 NavBar hidden before animation: {_navigationController.NavigationBarHidden}");
                            _navigationController.SetNavigationBarHidden(false, false);

                            // Add both views to the container for animation
                            oldView.Hidden = false;
                            animationContainer.AddSubview(oldView);
                            animationContainer.AddSubview(pageView);

                            // Apply starting state for the transition
                            Debug.WriteLine($"{TAG} 🎬 Applying push animation start for transition: {transition}");
                            ApplyPushAnimationStart(pageView, oldView, containerBounds, transition);

                            // Get animation duration and curve
                            var duration = GetAnimationDuration(page, isPush: true);
                            var curve = GetAnimationCurve(page, isPush: true);

                            Debug.WriteLine($"{TAG} 🎬 Creating animator for transition: {transition}, duration: {duration}");

                            // Special handling for WhirlIn3 - add rotation animation via CALayer
                            if (transition == AnimationType.WhirlIn3)
                            {
                                var rotationAnimation = CoreAnimation.CABasicAnimation.FromKeyPath("transform.rotation.z");
                                rotationAnimation.From = Foundation.NSNumber.FromDouble(-Math.PI * 6); // -1080 degrees
                                rotationAnimation.To = Foundation.NSNumber.FromDouble(0);
                                rotationAnimation.Duration = duration;
                                rotationAnimation.TimingFunction = CoreAnimation.CAMediaTimingFunction.FromName(CoreAnimation.CAMediaTimingFunction.EaseInEaseOut);
                                pageView.Layer.AddAnimation(rotationAnimation, "whirl3Rotation");
                            }

                            // Create the animation for scale/alpha/position
                            var animator = new UIViewPropertyAnimator(duration, curve, CreatePushAnimation(pageView, oldView, containerBounds, transition));

                            animator.AddCompletion((position) =>
                            {
                                Debug.WriteLine($"{TAG} 🔵 NavBar hidden after animation: {_navigationController.NavigationBarHidden}");

                                // Animation complete - now push the view controller
                                // This adds it to the navigation stack with the navigation bar
                                _navigationController.PushViewController(viewController, false);
                                _viewControllerStack.Add(viewController);
                                _pageStack.Add(page);

                                // Clean up temporary views
                                oldView.RemoveFromSuperview();
                                pageView.RemoveFromSuperview();

                                // Update navbar visibility based on the page's attached property
                                UpdateNavigationBarVisibility(page);

                                newAware?.OnTopmost();
                                oldAware?.OnCovered();

                                tcs.SetResult(true);
                            });

                            animator.StartAnimation();
                        }
                        else
                        {
                            _navigationController.PushViewController(viewController, false);
                            _viewControllerStack.Add(viewController);
                            _pageStack.Add(page);

                            newAware?.OnTopmost();
                            oldAware?.OnCovered();

                            tcs.SetResult(true);
                        }
                    }
                    else
                    {
                        // No animation - just push
                        Debug.WriteLine($"{TAG} 🔵 No animation push - calling PushViewController");
                        _navigationController.PushViewController(viewController, false);
                        _viewControllerStack.Add(viewController);
                        _pageStack.Add(page);
                        Debug.WriteLine($"{TAG} 🔵 Stack updated - count: {_viewControllerStack.Count}");

                        newAware?.OnTopmost();
                        oldAware?.OnCovered();

                        tcs.SetResult(true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in ShowPageAsync: {ex.Message}");
                Debug.WriteLine($"{TAG} ❌ Stack trace: {ex.StackTrace}");
                tcs.TrySetException(ex);
            }

            Debug.WriteLine($"{TAG} 🔵 ShowPageAsync returning task");
            return tcs.Task;
        }

        private Task PopPageAsync(bool animate, AnimationType transition)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                if (_viewControllerStack.Count <= 1)
                {
                    Debug.WriteLine($"{TAG} ⚠️ Cannot pop - only root page");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                if (_navigationController == null)
                {
                    Debug.WriteLine($"{TAG} ❌ NavigationController is null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                Debug.WriteLine($"{TAG} ⬅️ Popping, animate: {animate}");

                var oldView = _navigationController.TopViewController?.View;

                // Get the view that will be revealed (the one we're going back to)
                var newViewController = _navigationController.ViewControllers.Length > 1
                    ? _navigationController.ViewControllers[_navigationController.ViewControllers.Length - 2]
                    : null;

                var newView = newViewController?.View;

                // Get pages for INavigationAware
                var oldPage = _pageStack.Count > 0 ? _pageStack.Last() : null;
                var newPage = _pageStack.Count > 1 ? _pageStack[_pageStack.Count - 2] : null;

                var newAware = newPage as INavigationAware;
                var oldAware = oldPage as INavigationAware;

                // Check if we should use native iOS animation or custom animation
                if (transition == AnimationType.Default)
                {
                    // Use native UINavigationController animation (includes navigation bar animation)
                    Debug.WriteLine($"{TAG} 🔵 Using native iOS pop animation");
                    _navigationController.PopViewController(animate);

                    if (_viewControllerStack.Count > 0)
                    {
                        _viewControllerStack.RemoveAt(_viewControllerStack.Count - 1);
                    }

                    if (_pageStack.Count > 0)
                    {
                        _pageStack.RemoveAt(_pageStack.Count - 1);
                    }

                    oldAware?.OnRemoved();
                    newAware?.OnTopmost();

                    tcs.SetResult(true);
                }
                else if (animate && transition != AnimationType.None)
                {
                    if (oldView != null && newView != null && _navigationController.View != null)
                    {
                        // Use oldView's frame as the container bounds (it has the correct size!)
                        var containerBounds = oldView.Frame;
                        var animationContainer = _navigationController.View;

                        oldView.Hidden = false;
                        newView.Hidden = false;

                        animationContainer.AddSubview(newView);
                        animationContainer.AddSubview(oldView);

                        // Get the page being popped to get custom settings
                        var poppingPage = oldPage;

                        // Apply starting state for the pop transition
                        if (poppingPage != null)
                        {
                            ApplyPopAnimationStart(oldView, newView, containerBounds, transition);

                            // Get animation duration and curve
                            var duration = GetAnimationDuration(poppingPage, isPush: false);
                            var curve = GetAnimationCurve(poppingPage, isPush: false);

                            Debug.WriteLine($"{TAG} 🎬 Pop animation starting with {transition}, duration: {duration}s");

                            // Use UIViewPropertyAnimator for modern, interruptible animations
                            var animator = new UIViewPropertyAnimator(duration, curve, CreatePopAnimation(oldView, newView, containerBounds, transition));

                            animator.AddCompletion((position) =>
                            {
                                Debug.WriteLine($"{TAG} ✅ Pop animation complete - position: {position}");

                                // Remove views from animation container
                                oldView.RemoveFromSuperview();
                                newView.RemoveFromSuperview();

                                // Now actually pop from navigation controller
                                _navigationController.PopViewController(false);

                                if (_viewControllerStack.Count > 0)
                                {
                                    _viewControllerStack.RemoveAt(_viewControllerStack.Count - 1);
                                }

                                if (_pageStack.Count > 0)
                                {
                                    _pageStack.RemoveAt(_pageStack.Count - 1);
                                }

                                oldAware?.OnRemoved();
                                newAware?.OnTopmost();

                                tcs.SetResult(true);
                            });

                            animator.StartAnimation();
                        }
                        else
                        {
                            // Fallback if no page info
                            _navigationController.PopViewController(false);

                            if (_viewControllerStack.Count > 0)
                            {
                                _viewControllerStack.RemoveAt(_viewControllerStack.Count - 1);
                            }

                            if (_pageStack.Count > 0)
                            {
                                _pageStack.RemoveAt(_pageStack.Count - 1);
                            }

                            oldAware?.OnRemoved();
                            newAware?.OnTopmost();

                            tcs.SetResult(true);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"{TAG} ⚠️ Cannot animate - missing views");
                        // Fallback to no animation
                        _navigationController.PopViewController(false);

                        if (_viewControllerStack.Count > 0)
                        {
                            _viewControllerStack.RemoveAt(_viewControllerStack.Count - 1);
                        }

                        if (_pageStack.Count > 0)
                        {
                            _pageStack.RemoveAt(_pageStack.Count - 1);
                        }

                        oldAware?.OnRemoved();
                        newAware?.OnTopmost();

                        tcs.SetResult(true);
                    }
                }
                else
                {
                    // No animation - just pop
                    // Ensure the previous view is visible before popping
                    if (newView != null)
                    {
                        newView.Hidden = false;
                        newView.Alpha = 1;
                    }

                    _navigationController.PopViewController(false);

                    if (_viewControllerStack.Count > 0)
                    {
                        _viewControllerStack.RemoveAt(_viewControllerStack.Count - 1);
                    }

                    if (_pageStack.Count > 0)
                    {
                        _pageStack.RemoveAt(_pageStack.Count - 1);
                    }

                    oldAware?.OnRemoved();
                    newAware?.OnTopmost();

                    tcs.SetResult(true);
                }

                Debug.WriteLine($"{TAG} ✅ Popped - Stack: {_viewControllerStack.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ PopPageAsync error: {ex.Message}");
                Debug.WriteLine($"{TAG} Stack trace: {ex.StackTrace}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task PopToRootAsync(bool animate, AnimationType transition)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                if (_viewControllerStack.Count <= 1)
                {
                    Debug.WriteLine($"{TAG} ℹ️ Already at root");
                    tcs.SetResult(true);
                    return tcs.Task;
                }

                if (_navigationController == null)
                {
                    Debug.WriteLine($"{TAG} ❌ NavigationController is null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                Debug.WriteLine($"{TAG} 🏠 PopToRoot, animate: {animate}, stack: {_viewControllerStack.Count}");

                // Get references BEFORE popping
                var oldView = _navigationController.TopViewController?.View;
                var rootViewController = _viewControllerStack.First();
                var rootPage = _pageStack.First();

                // Get all pages that will be removed (for INavigationAware)
                var removedPages = _pageStack.Skip(1).ToList();

                if (animate && transition != AnimationType.None && oldView != null && _navigationController.View != null)
                {
                    var containerBounds = oldView.Frame;
                    var animationContainer = _navigationController.View;

                    // Get root view
                    var rootView = rootViewController.View;

                    if (rootView != null)
                    {
                        Debug.WriteLine($"{TAG} 🎬 Starting PopToRoot animation with {transition}");

                        // Setup views for animation
                        oldView.Hidden = false;
                        rootView.Hidden = false;

                        animationContainer.AddSubview(rootView);
                        animationContainer.AddSubview(oldView);

                        // Get the top page being removed for custom settings
                        var topPage = _pageStack.LastOrDefault();

                        if (topPage != null)
                        {
                            // Apply starting state for the pop transition
                            ApplyPopAnimationStart(oldView, rootView, containerBounds, transition);

                            // Get animation duration and curve
                            var duration = GetAnimationDuration(topPage, isPush: false);
                            var curve = GetAnimationCurve(topPage, isPush: false);

                            var animator = new UIViewPropertyAnimator(duration, curve, CreatePopAnimation(oldView, rootView, containerBounds, transition));

                            animator.AddCompletion((position) =>
                            {
                                Debug.WriteLine($"{TAG} ✅ PopToRoot animation complete");

                                // Remove views from animation container
                                oldView.RemoveFromSuperview();
                                rootView.RemoveFromSuperview();

                                // Now actually pop to root
                                _navigationController.PopToRootViewController(false);

                                // Update stacks
                                _viewControllerStack.Clear();
                                _viewControllerStack.Add(rootViewController);

                                _pageStack.Clear();
                                _pageStack.Add(rootPage);

                                // Notify INavigationAware - all removed pages
                                foreach (var removedPage in removedPages)
                                {
                                    if (removedPage is INavigationAware aware)
                                    {
                                        aware.OnRemoved();
                                    }
                                }

                                // Root page is now topmost
                                if (rootPage is INavigationAware rootAware)
                                {
                                    rootAware.OnTopmost();
                                }

                                Debug.WriteLine($"{TAG} ✅ PopToRoot complete - Stack: {_viewControllerStack.Count}");
                                tcs.SetResult(true);
                            });

                            animator.StartAnimation();
                            return tcs.Task;
                        }
                    }
                }

                // No animation or animation not possible - just pop immediately
                // Ensure root view is visible
                if (rootViewController.View != null)
                {
                    rootViewController.View.Hidden = false;
                    rootViewController.View.Alpha = 1;
                }

                _navigationController.PopToRootViewController(false);

                // Update stacks
                _viewControllerStack.Clear();
                _viewControllerStack.Add(rootViewController);

                _pageStack.Clear();
                _pageStack.Add(rootPage);

                // Notify INavigationAware - all removed pages
                foreach (var removedPage in removedPages)
                {
                    if (removedPage is INavigationAware aware)
                    {
                        aware.OnRemoved();
                    }
                }

                // Root page is now topmost
                if (rootPage is INavigationAware rootAware)
                {
                    rootAware.OnTopmost();
                }

                Debug.WriteLine($"{TAG} ✅ PopToRoot complete - Stack: {_viewControllerStack.Count}");
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ PopToRootAsync error: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        // Property change handler for NavigationPage properties
        private void OnNavigationPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == Microsoft.Maui.Controls.NavigationPage.BarTextColorProperty.PropertyName)
            {
                UpdateBarTextColor();
            }
            else if (e.PropertyName == Microsoft.Maui.Controls.NavigationPage.BarBackgroundColorProperty.PropertyName)
            {
                UpdateBarBackground();
            }
        }

        // Copied from MAUI NavigationRenderer - handles BarTextColor styling
        private void UpdateBarTextColor()
        {
            if (_navigationController?.NavigationBar == null || VirtualView == null)
                return;

            var barTextColor = VirtualView.BarTextColor;

            // Determine new title text attributes via global static data
            var globalTitleTextAttributes = UINavigationBar.Appearance.TitleTextAttributes;
            var titleTextAttributes = new UIKit.UIStringAttributes
            {
                ForegroundColor = barTextColor == null ? globalTitleTextAttributes?.ForegroundColor : barTextColor.ToPlatform(),
                Font = globalTitleTextAttributes?.Font
            };

            // Determine new large title text attributes via global static data
            var largeTitleTextAttributes = titleTextAttributes;
            if (OperatingSystem.IsIOSVersionAtLeast(11))
            {
                var globalLargeTitleTextAttributes = UINavigationBar.Appearance.LargeTitleTextAttributes;

                largeTitleTextAttributes = new UIKit.UIStringAttributes
                {
                    ForegroundColor = barTextColor == null ? globalLargeTitleTextAttributes?.ForegroundColor : barTextColor.ToPlatform(),
                    Font = globalLargeTitleTextAttributes?.Font
                };
            }

            if (OperatingSystem.IsIOSVersionAtLeast(13))
            {
                if (_navigationController.NavigationBar.CompactAppearance != null)
                {
                    _navigationController.NavigationBar.CompactAppearance.TitleTextAttributes = titleTextAttributes;
                    _navigationController.NavigationBar.CompactAppearance.LargeTitleTextAttributes = largeTitleTextAttributes;
                }

                if (_navigationController.NavigationBar.StandardAppearance != null)
                {
                    _navigationController.NavigationBar.StandardAppearance.TitleTextAttributes = titleTextAttributes;
                    _navigationController.NavigationBar.StandardAppearance.LargeTitleTextAttributes = largeTitleTextAttributes;
                }

                if (_navigationController.NavigationBar.ScrollEdgeAppearance != null)
                {
                    _navigationController.NavigationBar.ScrollEdgeAppearance.TitleTextAttributes = titleTextAttributes;
                    _navigationController.NavigationBar.ScrollEdgeAppearance.LargeTitleTextAttributes = largeTitleTextAttributes;
                }
            }
            else
            {
                _navigationController.NavigationBar.TitleTextAttributes = titleTextAttributes;

                if (OperatingSystem.IsIOSVersionAtLeast(11))
                    _navigationController.NavigationBar.LargeTitleTextAttributes = largeTitleTextAttributes;
            }

            // Set TintColor (i.e. Back Button arrow and Text)
            var iconColor = barTextColor;

            _navigationController.NavigationBar.TintColor = iconColor == null
                ? UINavigationBar.Appearance.TintColor
                : iconColor.ToPlatform();

            Debug.WriteLine($"{TAG} 🎨 Updated BarTextColor to: {barTextColor}");
        }

        // Placeholder for bar background (can be implemented later)
        private void UpdateBarBackground()
        {
            // TODO: Implement if needed
            Debug.WriteLine($"{TAG} 🎨 UpdateBarBackground called");
        }

        // Update navigation bar visibility based on the current page's attached property
        private void UpdateNavigationBarVisibility(Page? page)
        {
            if (_navigationController == null)
                return;

            if (page != null)
            {
                // Check the attached property on the page
                var hasNavBar = Microsoft.Maui.Controls.NavigationPage.GetHasNavigationBar(page);
                _navigationController.SetNavigationBarHidden(!hasNavBar, false);
                Debug.WriteLine($"{TAG} 🔵 Updated NavigationBar visibility for {page.GetType().Name}: {hasNavBar}");
            }
        }
    }
}
#endif
