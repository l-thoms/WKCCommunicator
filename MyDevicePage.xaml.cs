using CommunityToolkit.Mvvm.Messaging;
using Plugin.BLE.Android;
using System.Diagnostics;
using System.Text;
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
		if (Manager != null)
		{
			if (!await Manager.RequestQueue()) return;
			byte[]? keyResult = await Manager.SendCustomCommandAsync([(byte)CommandType.KeyCode, keyCode]);
			if (keyResult == null) Debug.WriteLine("Failed to send key code");
			Manager.ReleaseQueue();
		}
	}

	private async Task FetchShortcutsAsync()
	{
		if (Manager == null || Manager.ConnectedDevice == null) return;
		if (!await Manager.RequestQueue()) return;
		byte[]? result = await Manager.SendCustomCommandAsync([(byte)CommandType.ReadShortcutTable], true);
		if (result != null)
		{
			if (result.Length >= 1 && result[0] == 0)
			{
				byte[]? fetchResult = await Manager.GetCommandOutputAsync();
				Manager.ReleaseQueue();
				if (fetchResult == null) return;
				string fetchString = Encoding.UTF8.GetString(fetchResult);
				TableGroup[]? groups = AdapterManager.ParseTableGroups(fetchString);
				if (groups is null) return;
				Manager.InsetTableToLayout(groups, ShortcutTableLayout, TableGroupType.Shortcut);
			}
			else Manager.ReleaseQueue();
		}
		else
		{
			Manager.ReleaseQueue();
			Debug.WriteLine("Failed to fetch shortcuts");
		}
	}

	private async Task UpdateManagerAsync()
	{
		if (Manager?.ConnectedDevice == null)
		{
			MyDeviceDisconnectedSign.IsVisible = true;
			MyDeviceView.IsVisible = false;
			foreach (var child in ShortcutTableLayout)
				child.DisconnectHandlers();
			ShortcutTableLayout.Clear();
			return;
		}
		var physicalDevice = AdapterManager.GetPhysicalDevice(Manager.ConnectedDevice);
		if (physicalDevice == null) return;
		MyDeviceDisconnectedSign.IsVisible = false;
		MyDeviceView.IsVisible = true;
		await FetchShortcutsAsync();
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