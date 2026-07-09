using CommunityToolkit.Mvvm.Messaging;
using Plugin.BLE.Android;
using System.Diagnostics;
using WkcCommunicator.Connections;
using WkcCommunicator.Controls;
using WkcCommunicator.Types;

namespace WkcCommunicator;

public partial class MyDevicePage : ContentPage
{
	public AdapterManager? Manager { get; set; }
	private bool IsBatteryUpdating { get; set; } = false;
	private bool IsKeyButtonSending { get; set; } = false;
	private byte KeyBacklog { get; set; } = 0;
	private bool IsLocking { get; set; } = false;
	private int LockBacklog{ get; set; } = -1;

	private async Task SendKeyCodeAsync(byte keyCode)
	{
		if(Manager != null)
			await Manager.SendCustomCommandAsync([(byte)CommandType.KeyCode, keyCode], null, "Failed to send key code");
	}

	private async Task UpdateManagerAsync()
	{
		if (Manager?.ConnectedDevice == null)
		{
			MyDeviceDisconnectedSign.IsVisible = true;
			MyDeviceView.IsVisible = false;
			return;
		}
		var physicalDevice = AdapterManager.GetPhysicalDevice(Manager.ConnectedDevice);
		if (physicalDevice == null) return;
		MyDeviceDisconnectedSign.IsVisible = false;
		MyDeviceView.IsVisible = true;
		await Task.Delay(1);
	}

	public MyDevicePage()
	{
		InitializeComponent();
		WeakReferenceMessenger.Default.Register<AdapterManager, string>(this, "AdapterManager", async (r, m) =>
		{
			bool isInit = Manager == null;
			this.Manager = m;
			if (isInit && Manager != null)
			{
				await UpdateManagerAsync();
				this.Manager.ConnectedDeviceChanged += async (s, e) => await UpdateManagerAsync();
			}
		});
	}

	private void ContentPage_Loaded(object sender, EventArgs e)
	{
		WeakReferenceMessenger.Default.Send(new object(), "AdapterManagerRequest");
	}

	private async void KeyButton_Clicked(object sender, EventArgs e)
	{
		byte keyCode =
			sender == KeyUpButton ? (byte)1 :
			sender == KeyLeftButton ? (byte)2 :
			sender == KeyRightButton ? (byte)3 :
			sender == KeyDownButton ? (byte)4 :
			sender == KeyOkButton ? (byte)5 :
			sender == KeyMenuButton ? (byte)6 :
			sender == KeyBackButton ? (byte)7 :
			(byte)0;
		if (IsKeyButtonSending)
		{
			KeyBacklog = keyCode;
			return;
		}
		IsKeyButtonSending = true;
		await SendKeyCodeAsync(keyCode);
		if(KeyBacklog != 0)
		{
			await SendKeyCodeAsync(KeyBacklog);
			KeyBacklog = 0;
		}
		 IsKeyButtonSending = false;
	}
}