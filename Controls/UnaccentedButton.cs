using Microsoft.Maui.Handlers;

namespace WkcCommunicator.Controls;

public partial class UnaccentedButton : Button
{
	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		if(this.Handler is ButtonHandler handler)
		{
#if ANDROID
			if (handler.PlatformView.Background is Android.Graphics.Drawables.RippleDrawable ripple)
			{
				if(App.Current.UserAppTheme == AppTheme.Dark)
					ripple.SetColor(Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Argb(48, 255, 255, 255)));
				else
					ripple.SetColor(Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Argb(48, 0, 0, 0)));
			}
#endif
		}
	}
}