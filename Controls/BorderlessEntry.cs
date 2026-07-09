using Microsoft.Maui.Handlers;

namespace WkcCommunicator.Controls;

public class BorderlessEntry : Entry
{
	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
		if (this.Handler is EntryHandler handler)
		{
#if ANDROID
			var editText = handler.PlatformView as Android.Widget.EditText;
			if(editText != null)
			{
				editText.Background = null;
			}
#endif
		}
	}
}