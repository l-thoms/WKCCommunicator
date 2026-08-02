using Microsoft.Maui.Handlers;

namespace WkcCommunicator.Controls;

public class BorderlessPicker : Picker
{
	protected override void OnHandlerChanged()
	{
		base.OnHandlerChanged();
#if ANDROID
		if (Handler is PickerHandler handler && handler.PlatformView != null)
		{
			handler.PlatformView.Background = null;
		}
#endif
	}
}