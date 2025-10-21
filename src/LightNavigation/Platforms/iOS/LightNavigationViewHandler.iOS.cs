#if IOS || MACCATALYST
using Microsoft.Maui;
using Microsoft.Maui.Controls;
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
    public class LightNavigationViewHandler : ViewHandler<MauiNavigationPage, NavigationView>, IPlatformViewHandler
    {
        private const string TAG = "[LightNavigation_iOS]";
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
            Debug.WriteLine($"{TAG} ✅ Handler created");
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

        public static IPropertyMapper<MauiNavigationPage, LightNavigationViewHandler> PropertyMapper =
            new PropertyMapper<MauiNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewMapper)
            {
            };

        public static CommandMapper<MauiNavigationPage, LightNavigationViewHandler> CommandMapper =
            new CommandMapper<MauiNavigationPage, LightNavigationViewHandler>(ViewHandler.ViewCommandMapper)
            {
                [nameof(IStackNavigation.RequestNavigation)] = MapRequestNavigation
            };

        protected override NavigationView CreatePlatformView()
        {
            // Create UINavigationController
            _navigationController = new UINavigationController
            {
                NavigationBarHidden = true
            };

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

            // Manually show initial page if we have one
            if (VirtualView?.CurrentPage != null && MauiContext != null)
            {
                _ = EnqueueNavigationAsync(async () =>
                {
                    await ShowPageAsync(VirtualView.CurrentPage, false, isInitial: true);
                });
            }
            else
            {
                Debug.WriteLine($"{TAG} ⚠️ No CurrentPage to show!");
            }
        }

        protected override void DisconnectHandler(NavigationView platformView)
        {
            // Clean up
            _navigationQueue.Clear();
            _viewControllerStack.Clear();
            _pageStack.Clear();
            _navigationController?.View.RemoveFromSuperview();
            _navigationController = null;
            _navigationSemaphore?.Dispose();

            base.DisconnectHandler(platformView);
        }

        private static void MapRequestNavigation(LightNavigationViewHandler handler, MauiNavigationPage view, object? args)
        {
            if (args is not NavigationRequest request)
            {
                Debug.WriteLine($"{TAG} ⚠️ Invalid args");
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
                    var currentStackCount = _viewControllerStack.Count;

                    Debug.WriteLine($"{TAG} 📊 Current: {currentStackCount}, New: {newStack.Count}");

                    if (newStack.Count > currentStackCount)
                    {
                        // Push - show the new page
                        var newPage = newStack[newStack.Count - 1] as MauiPage;
                        if (newPage != null)
                        {
                            if (currentStackCount == 0)
                            {
                                Debug.WriteLine($"{TAG} ➡️ Initial page: {newPage.GetType().Name}");
                                await ShowPageAsync(newPage, false, isInitial: true);
                            }
                            else
                            {
                                Debug.WriteLine($"{TAG} ➡️ Push: {newPage.GetType().Name}");
                                await ShowPageAsync(newPage, request.Animated, isInitial: false);
                            }
                        }
                    }
                    else if (newStack.Count < currentStackCount)
                    {
                        // Pop
                        var diff = currentStackCount - newStack.Count;

                        if (diff == currentStackCount - 1)
                        {
                            // Pop to root
                            await PopToRootAsync(request.Animated);
                        }
                        else
                        {
                            // Pop one or more pages
                            for (int i = 0; i < diff; i++)
                            {
                                await PopPageAsync(request.Animated);
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

        private Task ShowPageAsync(MauiPage page, bool animate, bool isInitial)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
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

                var pageView = page.ToPlatform(MauiContext);

                if (pageView == null)
                {
                    Debug.WriteLine($"{TAG} ❌ ToPlatform returned null!");
                    tcs.SetResult(false);
                    return tcs.Task;
                }

                // Each page gets its own UIViewController with its own view
                var viewController = new UIViewController();
                viewController.View = pageView;

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
                    oldAware?.OnRemoved();
                    newAware?.OnTopmost();

                    tcs.SetResult(true);
                }
                else
                {
                    if (animate)
                    {
                        var oldView = _navigationController.TopViewController?.View;

                        if (oldView != null && _navigationController.View != null)
                        {
                            var containerBounds = oldView.Frame;

                            var animationContainer = _navigationController.View;

                            oldView.Hidden = false;
                            animationContainer.AddSubview(oldView);
                            animationContainer.AddSubview(pageView);

                            // Position old view at center
                            oldView.Frame = new CoreGraphics.CGRect(
                                0,
                                0,
                                containerBounds.Width,
                                containerBounds.Height);

                            // Position new view off-screen to the right
                            pageView.Frame = new CoreGraphics.CGRect(
                                containerBounds.Width,
                                0,
                                containerBounds.Width,
                                containerBounds.Height);

                            var animator = new UIViewPropertyAnimator(0.3, UIViewAnimationCurve.EaseOut, () =>
                            {
                                // Slide new view to center
                                pageView.Frame = new CoreGraphics.CGRect(
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
                            });

                            animator.AddCompletion((position) =>
                            {
                                oldView.RemoveFromSuperview();
                                pageView.RemoveFromSuperview();

                                _navigationController.PushViewController(viewController, false);
                                _viewControllerStack.Add(viewController);
                                _pageStack.Add(page);

                                newAware?.OnTopmost();
                                oldAware?.OnRemoved();

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
                            oldAware?.OnRemoved();

                            tcs.SetResult(true);
                        }
                    }
                    else
                    {
                        // No animation - just push
                        _navigationController.PushViewController(viewController, false);
                        _viewControllerStack.Add(viewController);
                        _pageStack.Add(page);

                        newAware?.OnTopmost();
                        oldAware?.OnRemoved();

                        tcs.SetResult(true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{TAG} ❌ Error in ShowPageAsync: {ex.Message}");
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        private Task PopPageAsync(bool animate)
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

                if (animate)
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

                        // Position new (previous) view slightly to the left
                        newView.Frame = new CoreGraphics.CGRect(
                            -containerBounds.Width * 0.3,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);

                        Debug.WriteLine($"{TAG} 📐 NewView frame after positioning: {newView.Frame}");

                        // Old view is at center
                        oldView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);

                        Debug.WriteLine($"{TAG} 📐 OldView frame after positioning: {oldView.Frame}");

                        // Use UIViewPropertyAnimator for modern, interruptible animations
                        var animator = new UIViewPropertyAnimator(0.3, UIViewAnimationCurve.EaseOut, () =>
                        {
                            Debug.WriteLine($"{TAG} 🎬 Pop animation block executing...");

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
                        });

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

        private Task PopToRootAsync(bool animate)
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

                if (animate && oldView != null && _navigationController.View != null)
                {
                    var containerBounds = oldView.Frame;
                    var animationContainer = _navigationController.View;

                    // Get root view
                    var rootView = rootViewController.View;

                    if (rootView != null)
                    {
                        Debug.WriteLine($"{TAG} 🎬 Starting PopToRoot animation");

                        // Setup views for animation
                        oldView.Hidden = false;
                        rootView.Hidden = false;

                        animationContainer.AddSubview(rootView);
                        animationContainer.AddSubview(oldView);

                        // Position root view slightly to the left (like it was before)
                        rootView.Frame = new CoreGraphics.CGRect(
                            -containerBounds.Width * 0.3,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);

                        // Old view is at center
                        oldView.Frame = new CoreGraphics.CGRect(
                            0,
                            0,
                            containerBounds.Width,
                            containerBounds.Height);

                        var animator = new UIViewPropertyAnimator(0.3, UIViewAnimationCurve.EaseOut, () =>
                        {
                            // Slide old view out to the right
                            oldView.Frame = new CoreGraphics.CGRect(
                                containerBounds.Width,
                                0,
                                containerBounds.Width,
                                containerBounds.Height);

                            // Slide root view back to center
                            rootView.Frame = new CoreGraphics.CGRect(
                                0,
                                0,
                                containerBounds.Width,
                                containerBounds.Height);
                        });

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

                // No animation or animation not possible - just pop immediately
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
    }
}
#endif
