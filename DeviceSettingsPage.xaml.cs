using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using WkcCommunicator.Connections;
using WkcCommunicator.Controls;
using WkcCommunicator.Types;

namespace WkcCommunicator;

public partial class DeviceSettingsPage : ContentPage
{
	public AdapterManager? Manager{ get; set; }

	private async Task UpdateBasicInformation()
	{
		foreach (var child in SettingsTableLayout)
			child.DisconnectHandlers();
		SettingsTableLayout.Clear();
		if (Manager == null || Manager.ConnectedDevice == null) return;
		if (Manager.ConnectedDevice.ProtocolVersion == null || Manager.ConnectedDevice.ProtocolVersion[1] == 0)
		{
			LegacySettingsBorder.IsVisible = true;
			DeviceNameEntry.Text = Manager.ConnectedDevice.Name;
			OwnerEntry.Text = Manager.ConnectedDevice.Owner;
			CharacterEntry.Text = Manager.ConnectedDevice.Character;
			ManufacturerEntry.Text = Manager.ConnectedDevice.Manufacturer;
		}
		else
		{
			LegacySettingsBorder.IsVisible = false;
			await FetchSettingsAsync();
		}
	}

	private async Task FetchSettingsAsync()
	{
		if (Manager != null)
		{
			if (!await Manager.RequestQueue()) return;
			byte[]? result = await Manager.SendCustomCommandAsync([(byte)CommandType.ReadSettingsTable]);
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
					Manager.InsetTableToLayout(groups, SettingsTableLayout, TableGroupType.Settings);
				}
				else Manager.ReleaseQueue();
			}
			else
			{
				Manager.ReleaseQueue();
				Debug.WriteLine("Failed to fetch settings");
			}
		}
	}

	private async Task UpdateManagerAsync()
	{
		if(Manager?.ConnectedDevice == null)
		{
			DeviceSettingsDisconnectedSign.IsVisible = true;
			DeviceSettingsView.IsVisible = false;
			DeviceNameEntry.ClearValue(Entry.TextProperty);
			OwnerEntry.ClearValue(Entry.TextProperty);
			CharacterEntry.ClearValue(Entry.TextProperty);
			ManufacturerEntry.ClearValue(Entry.TextProperty);
			return;
		}
		var physicalDevice = AdapterManager.GetPhysicalDevice(Manager.ConnectedDevice);
		if (physicalDevice == null) return;
		DeviceSettingsDisconnectedSign.IsVisible = false;
		DeviceSettingsView.IsVisible = true;
		await UpdateBasicInformation();
	}

	public DeviceSettingsPage()
	{
		InitializeComponent();
		WeakReferenceMessenger.Default.Register<AdapterManager, string>(this, "AdapterManager", async (r, m) =>
		{
			bool isInit = Manager == null;
			this.Manager = m;
			if (isInit && Manager != null)
			{
				this.Manager.ConnectedDeviceChanged += async (s, e) => await UpdateManagerAsync();
				await UpdateManagerAsync();
			}
		});
	}

	private void ContentPage_Loaded(object sender, EventArgs e)
	{
		WeakReferenceMessenger.Default.Send(new object(), "AdapterManagerRequest");
	}

	private async Task UploadSettingsAsync<TVal>(string key, TVal value) where TVal : notnull
	{
		if (Manager == null) return;
		if (Manager.ConnectedDevice == null) return;
		JsonObject jObject = new JsonObject();
		JsonValue? jValue = JsonValue.Create(value);
		if (jValue == null) return;
		jObject.Add(key, jValue);
		string jsonResult = jObject.ToJsonString();
		byte[] sendResult = new byte[Encoding.UTF8.GetByteCount(jsonResult) + 1];
		sendResult[0] = (byte)CommandType.WriteSettings;
		Buffer.BlockCopy(Encoding.UTF8.GetBytes(jsonResult), 0, sendResult, 1, sendResult.Length - 1);
		if (Manager != null)
		{
			if (!await Manager.RequestQueue()) return;
			byte[]? settingsResult = await Manager.SendCustomCommandAsync(sendResult);
			if (settingsResult == null) Debug.WriteLine("Failed to send settings");
			Manager.ReleaseQueue();
		}
	}

	private async void LegacyTextChanged(object sender, TextChangedEventArgs e)
	{
		if (Manager == null) return;
		if (Manager.ConnectedDevice == null) return;
		if (Manager.ConnectedDevice.ProtocolVersion != null && Manager.ConnectedDevice.ProtocolVersion[1] != 0) return;
		string key = "", value = "";
		var entry = sender as Entry;
		if (entry != null) value = entry.Text;
		if (entry == DeviceNameEntry) // Limit name to 30 bytes
		{
			while (Encoding.UTF8.GetByteCount(value) > 30)
				value = value.Substring(0, value.Length - 1);
			key = "device_name";
			Manager.ConnectedDevice.Name = value;
		}
		else if(entry == OwnerEntry)
		{
			key = "owner";
			Manager.ConnectedDevice.Owner = value;
		}
		else if(entry == CharacterEntry)
		{
			key = "character";
			Manager.ConnectedDevice.Character = value;
		}
		else if(entry == ManufacturerEntry)
		{
			key = "manufacturer";
			Manager.ConnectedDevice.Manufacturer = value;
		}
		await UploadSettingsAsync(key, value);
		Manager.SaveDevicePreference();
	}

	private async void DisconnectButton_Clicked(object sender, EventArgs e)
	{
		if (Manager != null)
			await Manager.DisconnectToDeviceForce(Manager.ConnectedDevice);
	}

	private async void DeleteDeviceButton_Clicked(object sender, EventArgs e)
	{
		if (Manager != null && Manager.ConnectedDevice != null)
			await Manager.ShowDeletePopupAsync(this, Manager.ConnectedDevice);
	}
}