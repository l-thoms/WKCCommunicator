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
#if ANDROID
		// 递归向上查找指定类型的父控件
		private static T? FindParent<T>(Android.Views.View? view) where T : Android.Views.View
		{
			if (view == null) return null;
			var parent = view.Parent;
			if (parent == null) return null;
			if (parent is T target) return target;
			return FindParent<T>(parent as Android.Views.View);
		}

		private static Android.Graphics.Color MauiColorToAndroidColor(Microsoft.Maui.Graphics.Color mauiColor)
		{
			int alpha = (int)(mauiColor.Alpha * 255);
			int red = (int)(mauiColor.Red * 255);
			int green = (int)(mauiColor.Green * 255);
			int blue = (int)(mauiColor.Blue * 255);
			return Android.Graphics.Color.Argb(alpha, red, green, blue);
		}
#endif
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
