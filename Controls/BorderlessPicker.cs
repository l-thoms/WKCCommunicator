using AndroidX.AppCompat.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace WkcCommunicator.Controls;

public class BorderlessPicker : Picker
{
	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();

		if (Handler is PickerHandler handler && handler.PlatformView != null)
		{
#if ANDROID
			handler.PlatformView.Background = null;
#endif
		}
	}
}