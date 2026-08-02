using CommunityToolkit.Maui;
using Google.Android.Material.TextField;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
#if ANDROID
using Android.Content.Res;
using Android.Views;
using Google.Android.Material.TextField;
using Microsoft.Maui.Platform;
#endif

namespace WkcCommunicator
{
    public static class MauiProgram
    {
		public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit();

#if DEBUG
			builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
