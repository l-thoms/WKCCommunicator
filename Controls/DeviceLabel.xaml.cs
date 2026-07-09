using Android.App;
using CommunityToolkit.Maui.Alerts;
using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.Threading.Tasks;
using WkcCommunicator.Types;

namespace WkcCommunicator.Controls;

public delegate void DeviceSelectedEventHandler(object? sender, DeviceSelectedEventArgs e);

public class DeviceSelectedEventArgs : EventArgs
{
	private DeviceSelectedEventArgs(){ }
	public WkcDeviceInfo? DeviceInfo;
	public DeviceSelectedEventArgs(WkcDeviceInfo? deviceInfo)
	{
		DeviceInfo = deviceInfo;
	}
}

public partial class DeviceLabel : ContentView
{
	public static readonly BindableProperty DeviceInfoProperty =
		BindableProperty.Create(nameof(DeviceInfo), typeof(WkcDeviceInfo), typeof(DeviceLabel), null);

	public static readonly BindableProperty IsConnectedProperty =
		BindableProperty.Create(nameof(IsConnected), typeof(bool), typeof(DeviceLabel), false);

	public event EventHandler? DeviceDelete;
	public event EventHandler? Disconnect;
	public event DeviceSelectedEventHandler? Reauthorize;
	public event DeviceSelectedEventHandler? Selected;
	private bool BypassClick{ get; set; } = false;
	public bool IsSaved { get; set; } = false;


	public WkcDeviceInfo? DeviceInfo
	{
		get => (WkcDeviceInfo)GetValue(DeviceInfoProperty); 
		set
		{
			SetValue(DeviceInfoProperty, value);
			Update();
		}
	}

	public bool IsConnected
	{
		get => (bool)GetValue(IsConnectedProperty);
		set
		{
			SetValue(IsConnectedProperty, value);
			Update();
		}
	}

	private void Init(WkcDeviceInfo? info)
	{
		InitializeComponent();
		this.DeviceInfo = info;
		Update();
	}

	public DeviceLabel()
	{
		Init(null);
	}

	public DeviceLabel(WkcDeviceInfo? info)
	{
		Init(info);
	}

	string FormatInfo(string? info)
	{
		if (info == null || info == "")
			return AppResources.DeviceLabel_Unknown;
		return info;
	}

	public void Update()
	{
		if (DeviceInfo == null)
		{
			DeviceInfo = new WkcDeviceInfo();
			return;
		}
		if (DeviceInfo.Name == null || DeviceInfo.Name == "")
			NameLabel.Text = AppResources.DeviceLabel_UnnamedDevice;
		else NameLabel.Text = DeviceInfo.Name;
		LabelOwner.Text = FormatInfo(DeviceInfo.Owner);
		LabelCharacter.Text = FormatInfo(DeviceInfo.Character);
		LabelManufacturer.Text = FormatInfo(DeviceInfo.Manufacturer);
		LabelModel.Text = FormatInfo(DeviceInfo.Model);

		LabelProtocolVersion.Text = AppResources.DeviceLabel_Unknown;
		if (DeviceInfo.ProtocolVersion != null)
		{
			LabelProtocolVersion.Text = "";
			for (int i = 0; i < DeviceInfo.ProtocolVersion.Length; i++)
				LabelProtocolVersion.Text += $"{(i == 0 ? "v" : ".")}{Convert.ToString(DeviceInfo.ProtocolVersion[i])}";
		}

		if (DeviceInfo.Address != null)
		{
			LabelAddress.Text = "";
			for (int i = 0; i < DeviceInfo.Address.Length; i++)
			{
				LabelAddress.Text += DeviceInfo.Address[i].ToString("X2");
				if (i < DeviceInfo.Address.Length - 1)
					LabelAddress.Text += ":";
			}
		}
		else LabelAddress.Text = AppResources.DeviceLabel_Unknown;

		Debug.WriteLine($"Signal: {DeviceInfo.Signal}");

		if (IsConnected)
		{
			SignalIndicator.IsVisible = true;
			SignalBackground.Source = App.Current.PlatformAppTheme == AppTheme.Dark ? "check_dark.svg" : "check.svg";
			SignalIndicator.Source = SignalBackground.Source;
		}
		else if (DeviceInfo.Signal < 0)
		{
			SignalIndicator.IsVisible = false;
			SignalBackground.Source = App.Current.PlatformAppTheme == AppTheme.Dark ? "signal_disconnected_dark.png" : "signal_disconnected.png";
		}
		else
		{
			SignalIndicator.IsVisible = true;
			SignalBackground.Source = App.Current.PlatformAppTheme == AppTheme.Dark ? "signal_cellular_alt_dark.png" : "signal_cellular_alt.png";
			if (DeviceInfo.Signal < 10)
				SignalIndicator.Source = null;
			else if (DeviceInfo.Signal < 40)
				SignalIndicator.Source = App.Current.PlatformAppTheme == AppTheme.Dark ? "signal_cellular_alt_1_dark.png" : "signal_cellular_alt_1.png";
			else if (DeviceInfo.Signal < 70)
				SignalIndicator.Source = App.Current.PlatformAppTheme == AppTheme.Dark ? "signal_cellular_alt_2_dark.png" : "signal_cellular_alt_2.png";
			else
				SignalIndicator.Source = App.Current.PlatformAppTheme == AppTheme.Dark ? "signal_cellular_alt_dark.png" : "signal_cellular_alt.png";
		}
	}

	private Page? GetParentPage()
	{
		Element? parent = this.Parent;
		while (parent != null && parent is not Page)
		{
			parent = parent.Parent;
		}
		return parent as Page;
	}

	private async void TouchBehavior_LongPressCompleted(object sender, CommunityToolkit.Maui.Core.LongPressCompletedEventArgs e)
	{
		BypassClick = true;
		string? action = null;
		var page = this.GetParentPage();
		string cancel = AppResources.DeviceLabel_Cancel;
		string deleteDevice = AppResources.DeviceLabel_DeleteDevice;
		string disconnect = AppResources.DeviceLabel_Disconnect;
		string reauthorize = AppResources.DeviceLabel_Reverify;
		List<string> options = new List<string>();
		if (IsConnected) options.Add(disconnect);
		if (!IsConnected && IsSaved) options.Add(reauthorize);
		if (IsSaved) options.Add(deleteDevice);
		if (page != null && DeviceInfo?.Key != "")
		{
			if (options.Count > 0)
				action = await page.DisplayActionSheet(null, cancel, null, options.ToArray());
			if (IsEnabled)
			{
				if (action == deleteDevice)
					DeviceDelete?.Invoke(this, new EventArgs());
				else if (action == disconnect)
					Disconnect?.Invoke(this, new EventArgs());
				else if (action == reauthorize)
					Reauthorize?.Invoke(this, new DeviceSelectedEventArgs(this.DeviceInfo));
			}
			else
			{
				using var source = new CancellationTokenSource();
				await Toast.Make(AppResources.DeviceLabel_TryAgain).Show(source.Token);
			}
		}
		BypassClick = false;
	}

	private void UnaccentedButton_Clicked(object sender, EventArgs e)
	{
		if (BypassClick || IsConnected) return;
		Selected?.Invoke(this, new DeviceSelectedEventArgs(this.DeviceInfo));
	}
}