using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace WkcCommunicator
{
	public partial class App : Application
	{
		public App()
		{
			InitializeComponent();
		}

		protected override Window CreateWindow(IActivationState? activationState)
		{
			return new Window(new AppShell());
		}

		public static async Task ShowCommonPopupAsync(Page parent, View content)
		{
			Popup popup = new Popup() { Padding = new Thickness(0) };
			var popupOptions = new PopupOptions();
			if (popupOptions.Shadow != null)
			{
				popupOptions.Shadow.Opacity = 0.25f;
				popupOptions.Shadow.Offset = new Point(0, 4);
				popupOptions.Shadow.Radius = 8;
			}
			var shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle();
			shape.CornerRadius = 24;
			shape.StrokeThickness = 0;
			popupOptions.Shape = shape;
			popup.Content = content;
			await parent.ShowPopupAsync(popup, popupOptions);
		}
	}
}