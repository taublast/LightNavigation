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
        private const int ANIMATION_IN_DURATION_MS = 150;
        private const int ANIMATION_OUT_DURATION_MS = 150;

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
                        Debug.WriteLine($"{TAG} ⬅️ Pop {diff} page(s)");

                        // Pop the required number of pages
                        for (int i = 0; i < diff; i++)
                        {
                            await PopPageAsync(request.Animated);
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

        private Task ShowPageAsync(MauiPage page, bool animate, bool isInitial)
        {
            var tcs = new TaskCompletionSource<bool>();

            try
            {
                Debug.WriteLine($"{TAG} 📄 ShowPageAsync: {page.GetType().Name}, isInitial: {isInitial}");

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
                    oldAware?.OnRemoved();
                    newAware?.OnTopmost();
                    tcs.SetResult(true);
                }
                else
                {
                    // Navigation - add new view BEFORE hiding old view
                    Debug.WriteLine($"{TAG} 👁️ Adding new view while keeping old view visible");

                    // ALWAYS ensure views have TranslateTransform (replace if it's a different type)
                    if (newView.RenderTransform == null || newView.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform)
                    {
                        newView.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform();
                    }
                    if (oldView.RenderTransform == null || oldView.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform)
                    {
                        oldView.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform();
                    }

                    if (animate)
                    {
                        // Set initial animation state
                        newView.Opacity = 0.3; // Start slightly transparent
                        ((Microsoft.UI.Xaml.Media.TranslateTransform)newView.RenderTransform).X = container.ActualWidth * 0.15;
                        oldView.Visibility = WVisibility.Visible;
                    }
                    else
                    {
                        // No animation - set initial state
                        newView.Opacity = 1;
                        ((Microsoft.UI.Xaml.Media.TranslateTransform)newView.RenderTransform).X = 0;
                    }

                    Debug.WriteLine($"{TAG} OLDVIEW visibility: {oldView.Visibility}");

                    // Add new view on top (fully visible - no black flash!)
                    container.Children.Add(newView);
                    newView.Visibility = WVisibility.Visible;

                    _viewStack.Add(newView);
                    _pageStack.Add(page);

                    // Wait for new view to be laid out and rendered
                    newView.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        Debug.WriteLine($"{TAG} ✅ New view rendered");

                        if (animate)
                        {
                            Debug.WriteLine($"{TAG} 🎬 Starting push animation");
                            Debug.WriteLine($"{TAG} OLDVIEW 2 visibility: {oldView.Visibility}");

                            newView.Visibility = WVisibility.Visible;

                            // Create storyboard for animation
                            var storyboard = new Storyboard();

                            // Opacity animation
                            var opacityAnim = new DoubleAnimation
                            {
                                From = 0.3,
                                To = 1.0,
                                Duration = new Duration(TimeSpan.FromMilliseconds(ANIMATION_IN_DURATION_MS)),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            };
                            Storyboard.SetTarget(opacityAnim, newView);
                            Storyboard.SetTargetProperty(opacityAnim, "Opacity");
                            storyboard.Children.Add(opacityAnim);

                            // Translation animation
                            var translateAnim = new DoubleAnimation
                            {
                                From = container.ActualWidth * 0.15,
                                To = 0,
                                Duration = new Duration(TimeSpan.FromMilliseconds(ANIMATION_IN_DURATION_MS)),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                            };
                            Storyboard.SetTarget(translateAnim, newView.RenderTransform);
                            Storyboard.SetTargetProperty(translateAnim, "X");
                            storyboard.Children.Add(translateAnim);

                            storyboard.Completed += (s, e) =>
                            {
                                // AFTER animation completes, hide old view
                                newView.Opacity = 1;
                                if (newView.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform t)
                                {
                                    t.X = 0;
                                }
                                oldView.Visibility = WVisibility.Collapsed;
                                Debug.WriteLine($"{TAG} ✅ Animation complete - old view hidden - NO BLACK FLASH!");

                                newAware?.OnTopmost();
                                oldAware?.OnRemoved();
                                tcs.SetResult(true);
                            };

                            storyboard.Begin();
                        }
                        else
                        {
                            // No animation - hide old view immediately
                            oldView.Visibility = WVisibility.Collapsed;
                            Debug.WriteLine($"{TAG} ✅ NO BLACK FLASH - seamless transition!");

                            newAware?.OnTopmost();
                            oldAware?.OnRemoved();
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

        private Task PopPageAsync(bool animate)
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

                    // ALWAYS ensure both views have TranslateTransform (replace if it's a different type)
                    if (oldView.RenderTransform == null || oldView.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform)
                    {
                        oldView.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform();
                    }
                    if (newView.RenderTransform == null || newView.RenderTransform is not Microsoft.UI.Xaml.Media.TranslateTransform)
                    {
                        newView.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform();
                    }

                    // ALWAYS make newView visible and reset its transform (it was hidden when we pushed on top)
                    newView.Opacity = 1;
                    newView.Visibility = WVisibility.Visible;
                    ((Microsoft.UI.Xaml.Media.TranslateTransform)newView.RenderTransform).X = 0;

                    if (animate)
                    {
                        // Prepare old view for animation
                        oldView.Opacity = 1;
                        oldView.Visibility = WVisibility.Visible;
                        ((Microsoft.UI.Xaml.Media.TranslateTransform)oldView.RenderTransform).X = 0;
                    }

                    Debug.WriteLine($"{TAG} OLDVIEW visibility: {oldView.Visibility}");

                    newView.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        if (animate)
                        {
                            Debug.WriteLine($"{TAG} 🎬 Starting pop animation");
                            oldView.Visibility = WVisibility.Visible;

                            // Create storyboard for animation
                            var storyboard = new Storyboard();

                            // Opacity animation (fade out)
                            var opacityAnim = new DoubleAnimation
                            {
                                From = 1.0,
                                To = 0.0,
                                Duration = new Duration(TimeSpan.FromMilliseconds(ANIMATION_OUT_DURATION_MS)),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                            };
                            Storyboard.SetTarget(opacityAnim, oldView);
                            Storyboard.SetTargetProperty(opacityAnim, "Opacity");
                            storyboard.Children.Add(opacityAnim);

                            // Translation animation (slide out to right)
                            var translateAnim = new DoubleAnimation
                            {
                                From = 0,
                                To = container.ActualWidth * 0.3,
                                Duration = new Duration(TimeSpan.FromMilliseconds(ANIMATION_OUT_DURATION_MS)),
                                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                            };
                            Storyboard.SetTarget(translateAnim, oldView.RenderTransform);
                            Storyboard.SetTargetProperty(translateAnim, "X");
                            storyboard.Children.Add(translateAnim);

                            storyboard.Completed += (s, e) =>
                            {
                                container.Children.Remove(oldView);
                                newView.Opacity = 1;
                                ((Microsoft.UI.Xaml.Media.TranslateTransform)newView.RenderTransform).X = 0;
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
                            newView.Opacity = 1;
                            newView.Visibility = WVisibility.Visible;
                            ((Microsoft.UI.Xaml.Media.TranslateTransform)newView.RenderTransform).X = 0;
                            Debug.WriteLine($"{TAG} ✅ Pop complete - no black flash!");

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

                    Debug.WriteLine($"{TAG} ✅ PopToRoot complete - no black flash!");
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
