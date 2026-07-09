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
	private bool IsSettingsUpdating { get; set; } = false;

	private void UpdateBasicInformation()
	{
		if (Manager == null || Manager.ConnectedDevice == null) return;
		DeviceNameEntry.Text = Manager.ConnectedDevice.Name;
		OwnerEntry.Text = Manager.ConnectedDevice.Owner;
		CharacterEntry.Text = Manager.ConnectedDevice.Character;
		ManufacturerEntry.Text = Manager.ConnectedDevice.Manufacturer;
	}

	private async Task FetchSettingsAsync()
	{
		if (Manager != null)
			await Manager.SendCustomCommandAsync([(byte)CommandType.ReadSettingsItem], (result) =>
			{
				if (result.Length <= 1) return;
				var resultText = Encoding.UTF8.GetString(result);
				Debug.WriteLine(resultText);
				var resultRaw = JsonConvert.DeserializeObject<WkcSettingsRaw>(resultText);
				resultRaw.brightness = resultRaw.brightness > 10 ? 10 : resultRaw.brightness < 0 ? 0 : resultRaw.brightness;
			}, "Failed to read settings");
	}

	private async void UpdateManagerAsync()
	{
		IsSettingsUpdating = true;
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
		UpdateBasicInformation();
		//await FetchSettingsAsync();
		IsSettingsUpdating = false;
	}

	public DeviceSettingsPage()
	{
		InitializeComponent();
		WeakReferenceMessenger.Default.Register<AdapterManager, string>(this, "AdapterManager", (r, m) =>
		{
			bool isInit = Manager == null;
			this.Manager = m;
			if (isInit && Manager != null)
			{
				UpdateManagerAsync();
				this.Manager.ConnectedDeviceChanged += (s, e) => UpdateManagerAsync();
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
			await Manager.SendCustomCommandAsync(sendResult, null, "Failed to send settings");
	}

	private async void BasicInformationTextChanged(object sender, TextChangedEventArgs e)
	{
		if (Manager == null) return;
		if (Manager.ConnectedDevice == null) return;
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