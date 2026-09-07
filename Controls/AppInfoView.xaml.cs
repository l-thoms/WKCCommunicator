using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;

namespace WkcCommunicator.Controls;

public partial class AppInfoView : ContentView
{
	private Page? ParentPage{ get; set; }
	
	private AppInfoView()
	{
		InitializeComponent();
	}

	public AppInfoView(Page parent)
	{
		InitializeComponent();
		this.BindingContext = this;
		ParentPage = parent;
	}

	private async Task Close()
	{
		if (ParentPage != null)
			await ParentPage.ClosePopupAsync();
	}

	private async void ButtonRepo_Clicked(object sender, EventArgs e)
	{
		try
		{
			Uri uri = new Uri("https://github.com/l-thoms/WKCCommunicator");

			var options = new BrowserLaunchOptions()
			{
				LaunchMode = BrowserLaunchMode.External,
			};
			await Browser.Default.OpenAsync(uri, options);
		}
		catch{; }
	}

	private async void ButtonOk_Clicked(object sender, EventArgs e)
	{
		await Close();
	}

	public static async Task ShowAppInfo(Page parent)
	{
		var appInfoView = new AppInfoView(parent);
		await App.ShowCommonPopupAsync(parent, appInfoView);
	}
}