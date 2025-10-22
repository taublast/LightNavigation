namespace LightNavigation
{
    /// <summary>
    /// Interface for pages that want to be notified of navigation lifecycle events.
    /// Implement this interface on your MAUI Page to receive navigation callbacks.
    /// </summary>
    public interface INavigationAware
    {
        /// <summary>
        /// Called when this page becomes the topmost page in the navigation stack (visible to user).
        /// This is called after the navigation animation completes.
        /// </summary>
        void OnTopmost();

        /// <summary>
        /// Called when this page is removed from the navigation stack (no longer in memory).
        /// Use this to clean up resources, unsubscribe from events, call Dispose() etc.
        /// </summary>
        void OnRemoved();

        /// <summary>
        /// Went behind topmost, still in navigation stack
        /// </summary>
        void OnCovered();

        /// <summary>
        /// Called just before this page is about to be popped from the navigation stack.
        /// This is called before the navigation animation starts.
        /// </summary>
        void OnPopping();

        /// <summary>
        /// Called just before this page is about to be pushed onto the navigation stack.
        /// This is called before the navigation animation starts.
        /// </summary>
        void OnPushing();
    }
}
