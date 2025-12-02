using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace LightNavigation
{
    /// <summary>
    /// Extension methods for configuring LightNavigation handlers in your MAUI application.
    /// </summary>
    public static class LightNavigationExtensions
    {
        /// <summary>
        /// Registers the LightNavigationPage handlers for all platforms.
        /// Call this in your MauiProgram.cs CreateMauiApp method:
        /// <code>
        /// builder.UseLightNavigation();
        /// </code>
        /// </summary>
        /// <param name="builder">The MauiAppBuilder to configure.</param>
        /// <returns>The MauiAppBuilder for method chaining.</returns>
        public static MauiAppBuilder UseLightNavigation(this MauiAppBuilder builder)
        {
            builder.ConfigureMauiHandlers(handlers =>
            {
#if ANDROID || IOS || MACCATALYST || WINDOWS
                handlers.AddHandler<LightNavigationPage, Platform.LightNavigationViewHandler>();
#endif
            });

            return builder;
        }
    }
}
