using Android.App;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices;
using Newtonsoft.Json;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using WkcCommunicator.Connections;
using WkcCommunicator.Controls;
using WkcCommunicator.Types;
using Xamarin.JSpecify.Annotations;

namespace WkcCommunicator
{
	public partial class MainPage : ContentPage
	{
		// 0x3b1e stands for 'WKC' (22*26^2+10*26+2=15134=0x3b1e)
		readonly byte[] WKC_APPEARANCE = { 0x1e, 0x3b };

		int count = 0;
		AdapterManager Manager { get; set; } = new AdapterManager();
		public MainPage()
		{
			InitializeComponent();
			WeakReferenceMessenger.Default.Register<object, string>(this, "AdapterManagerRequest", (r, m) =>
			{
				WeakReferenceMessenger.Default.Send(Manager, "AdapterManager");
				Debug.WriteLine($"Send: {Manager == null}");
			});
			WeakReferenceMessenger.Default.Register<object, string>(this, "DeviceInfoUpdateRequest", (r, m) => ListSavedDevices());
			var ble = Plugin.BLE.CrossBluetoothLE.Current;
			var adapter = ble.Adapter;
			adapter.DeviceDiscovered += Adapter_DeviceDiscovered;
			adapter.DeviceDisconnected += Adapter_DeviceDisconnected;
			adapter.ScanTimeoutElapsed += Adapter_ScanTimeoutElapsed;
			adapter.DeviceConnectionLost += Adapter_DeviceConnectionLost;
			Manager.ConnectedDeviceChanged += Manager_ConnectedDeviceChanged;
			Manager.DeviceDeleted += Manager_DeviceDeleted;
			NavigatedTo += MainPage_NavigatedTo;
		}

		private void ListSavedDevices()
		{
			// Remove label
			for (int i = MyDeviceLayout.Count - 1; i >= 0; i--)
			{
				if (MyDeviceLayout[i].GetType() != typeof(DeviceLabel)) MyDeviceLayout.RemoveAt(i);
				bool isValid = false;
				var deviceLabel = (DeviceLabel)MyDeviceLayout[i];
				foreach(var device in Manager.SavedDevices)
				{
					if(AdapterManager.CompareAddress(device, deviceLabel.DeviceInfo))
					{
						isValid = true;
						if (AdapterManager.CompareAddress(Manager.ConnectedDevice, deviceLabel.DeviceInfo))
						{
							deviceLabel.DeviceInfo = Manager.ConnectedDevice;
							deviceLabel.Update();
						}
						break;
					}
				}
				if (!isValid)
				{
					var child = MyDeviceLayout[i];
					MyDeviceLayout.RemoveAt(i);
					child.DisconnectHandlers();
				}
			}
			// Add label not listed
			foreach(var device in Manager.SavedDevices)
			{
				bool isDeviceListed = false;
				foreach(var control in MyDeviceLayout)
				{
					var deviceLabel = control as DeviceLabel;
					if (deviceLabel == null) continue;
					if(AdapterManager.CompareAddress(deviceLabel.DeviceInfo ,device))
					{
						isDeviceListed = true;
						break;
					}
				}
				if(!isDeviceListed)
				{
					var deviceLabel = CreateDeviceLabel(device);
					deviceLabel.IsSaved = true;
					MyDeviceLayout.Add(deviceLabel);
				}
			}
		}

		private void ResetSignal()
		{
			foreach(var control in MyDeviceLayout)
			{
				var deviceLabel = control as DeviceLabel;
				if (deviceLabel == null) continue;
				if (deviceLabel.DeviceInfo != null)
				{
					deviceLabel.DeviceInfo.Signal = -1;
					deviceLabel.Update();
				}
			}
		}
		private void Manager_ConnectedDeviceChanged(object? sender, EventArgs e)
		{
			foreach (var saved in MyDeviceLayout)
			{
				var deviceLabel = saved as DeviceLabel;
				if (deviceLabel == null) continue;
					deviceLabel.IsConnected = 
						AdapterManager.CompareAddress(deviceLabel.DeviceInfo, Manager.ConnectedDevice);
			}
		}

		private async Task ScanAsync()
		{
			bool isPermissionGranted = await Connections.AdapterManager.CheckPermissionAsync();
			using var source = new CancellationTokenSource();
			if (!isPermissionGranted)
			{ 
				await Toast.Make(AppResources.MainPage_PermissionNotEnabled).Show(source.Token);
				return;
			}

			var ble = Plugin.BLE.CrossBluetoothLE.Current;
			var adapter = ble.Adapter;
			if (adapter.IsScanning) await adapter.StopScanningForDevicesAsync();
			Manager.ScanningDevices.Clear();
			try
			{
				adapter.ScanMode = ScanMode.Balanced;
				await adapter.StartScanningForDevicesAsync();
				Debug.WriteLine("Scanning...");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Scan failed, reason: {ex.Message}");
			}
		}

		private void ParseMfgData(byte[] data, WkcDeviceInfo deviceInfo)
		{
			if (data.Length < 6)
				return;
			deviceInfo.ProtocolVersion = new byte[4];
			for (int v = 0; v < 4; v++)
				deviceInfo.ProtocolVersion[v] = data[v + 2];

			// Parse remaining data
			if (data.Length > 6)
			{
				int readPos = 6;
				while (readPos < data.Length && data[readPos] != 0 && readPos + data[readPos] + 1 <= data.Length)
				{
					if (data[readPos] == 1)
					{
						readPos += 2;
						continue;
					}
					byte[] readDataRaw = data.Skip(readPos + 2).Take(data[readPos] - 1).ToArray();
					string readData = Encoding.UTF8.GetString(readDataRaw);
					switch (data[readPos + 1])
					{
						case 0: deviceInfo.Owner = readData; break;
						case 1: deviceInfo.Character = readData; break;
						case 2: deviceInfo.Manufacturer = readData; break;
						case 3: deviceInfo.Model = readData; break;
					}
					readPos += data[readPos] + 1;
				}
			}
		}

		private async void Adapter_DeviceDiscovered(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
		{
			var ble = Plugin.BLE.CrossBluetoothLE.Current;
			var adapter = ble.Adapter;
			MainRefreshView.IsRefreshing = false;
			bool isDeviceSkipped = true;

			int scannedDeviceIndex = -1;
			var device = e.Device;
			WkcDeviceInfo deviceInfo = new WkcDeviceInfo();
			deviceInfo.Address = device.Id.ToByteArray().TakeLast(6).ToArray();
			deviceInfo.Name = device.Name;
			int signal = device.Rssi + 120;
			if (signal > 100) signal = 100;
			deviceInfo.Signal = signal;
			Manager.ScanningDevices.Add(deviceInfo);
			foreach (var adv in device.AdvertisementRecords)
			{
				// Filter devices by appearance
				if (adv.Type == AdvertisementRecordType.Appearance)
				{
					Debug.WriteLine($"Appearance: {adv.Data.Length} {adv.Data[0].ToString("x2")} {adv.Data[1].ToString("x2")}");
					if (adv.Data.SequenceEqual(WKC_APPEARANCE))
					{
						isDeviceSkipped = false;
					}
				}
				if (adv.Type == AdvertisementRecordType.ManufacturerSpecificData)
				{
					ParseMfgData(adv.Data, deviceInfo);
					break;
				}
			}
			if (isDeviceSkipped)
			{
				return;
			}
			for (int dev = 0; dev < Manager.ScannedDevices.Count; dev++)
			{
				if (AdapterManager.CompareAddress(Manager.ScannedDevices[dev], deviceInfo))
				{
					scannedDeviceIndex = dev;
					break;
				}
			}

			// Update UI
			bool isDeviceSaved = false;
			DeviceLabel? deviceLabel = null;
			foreach (var saved in Manager.SavedDevices)
				if (AdapterManager.CompareAddress(saved, deviceInfo))
				{
					isDeviceSaved = true;
					break;
				}
			if (isDeviceSaved)
			{
				foreach (var saved in MyDeviceLayout)
				{
					var savedLabel = saved as DeviceLabel;
					if (savedLabel == null) continue;
					if (AdapterManager.CompareAddress(savedLabel.DeviceInfo, deviceInfo))
					{
						if (savedLabel.DeviceInfo != null)
						{
							savedLabel.DeviceInfo.Signal = deviceInfo.Signal;
							savedLabel.DeviceInfo.Name = deviceInfo.Name;
							savedLabel.DeviceInfo.ProtocolVersion = deviceInfo.ProtocolVersion;
							savedLabel.DeviceInfo.Owner = deviceInfo.Owner;
							savedLabel.DeviceInfo.Character = deviceInfo.Character;
							savedLabel.DeviceInfo.Manufacturer = deviceInfo.Manufacturer;
							savedLabel.Update();
							Manager.SaveDevicePreference();
						}
						deviceLabel = savedLabel;
						break;
					}
				}
			}
			else
			{
				bool isDeviceListed = false;
				foreach (var nearby in NearbyDeviceLayout)
				{
					var nearbyLabel = nearby as DeviceLabel;
					if (nearbyLabel == null) continue;
					if (AdapterManager.CompareAddress(nearbyLabel.DeviceInfo, deviceInfo))
					{
						deviceLabel = nearbyLabel;
						isDeviceListed = true;
						break;
					}
				}
				if (!isDeviceListed)
				{
					deviceLabel = CreateDeviceLabel(deviceInfo);
					NearbyDeviceLayout.Add(deviceLabel);
				}
			}

			if(scannedDeviceIndex != -1)
			{
				Manager.ScannedDevices[scannedDeviceIndex].Name = deviceInfo.Name;
				Manager.ScannedDevices[scannedDeviceIndex].ProtocolVersion = deviceInfo.ProtocolVersion;
				Manager.ScannedDevices[scannedDeviceIndex].Signal = deviceInfo.Signal;
			}
		}

		private async void Adapter_DeviceDisconnected(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
		{
			Debug.WriteLine("Device disconnected");
			if (AdapterManager.GetPhysicalDevice(Manager.ConnectedDevice) == null && Manager.ConnectedDevice != null)
			{
				foreach (var control in MyDeviceLayout)
				{
					var deviceLabel = control as DeviceLabel;
					if (deviceLabel == null) continue;
					if (AdapterManager.CompareAddress(deviceLabel.DeviceInfo, Manager.ConnectedDevice))
					{
						if(deviceLabel.DeviceInfo != null)
							deviceLabel.DeviceInfo.Signal = -1;
						deviceLabel.IsConnected = false;
						break;
					}
				}
				Manager.ConnectedDevice = null;
			}
			await ScanAsync();
		}

		private void Adapter_DeviceConnectionLost(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceErrorEventArgs e)
		{
			Debug.WriteLine("Connection lost");
			if (Manager.ConnectedDevice != null)
			{
				Dispatcher.Dispatch(async () =>
				{
					await Manager.DisconnectToDeviceForce(Manager.ConnectedDevice, true);
				});
			}
		}

		private async void Manager_DeviceDeleted(object? sender, EventArgs e)
		{
			ListSavedDevices();
			await ScanAsync();
		}

		private DeviceLabel CreateDeviceLabel(WkcDeviceInfo deviceInfo)
		{
			DeviceLabel deviceLabel = new DeviceLabel(deviceInfo);
			deviceLabel.Selected += async (s, e) =>
			{
				MyDeviceLayout.InputTransparent = NearbyDeviceLayout.InputTransparent = true;
				await ConnectDeviceByLabel(deviceLabel);
				MyDeviceLayout.InputTransparent = NearbyDeviceLayout.InputTransparent = false;
			};
			deviceLabel.DeviceDelete += async (s, e) =>
				await Manager.ShowDeletePopupAsync(this, deviceInfo);
			deviceLabel.Disconnect += async (s, e) =>
				await Manager.DisconnectToDeviceForce(deviceInfo);
			deviceLabel.Reauthorize += async (s, e) =>
			{
				MyDeviceLayout.InputTransparent = NearbyDeviceLayout.InputTransparent = true;
				await ConnectDeviceByLabel(deviceLabel, true);
				MyDeviceLayout.InputTransparent = NearbyDeviceLayout.InputTransparent = false;
			};
			return deviceLabel;
		}

		private Guid AddressToGuid(byte[]? address)
		{
			byte[] addressFilled = Enumerable.Repeat<byte>(0, 16).ToArray();
			if (address != null)
			{
				int startIndex = 16 - address.Length;
				for (int i = 0; i < address.Length; i++)
					addressFilled[i + startIndex] = address[i];
			}
			return new Guid(addressFilled);
		}

		private async Task SyncTime(WkcDeviceInfo device)
		{
			var characteristic = await AdapterManager.GetCommandCharacteristicAsync(device);
			if (characteristic == null) return;
			try
			{
				DateTime dateTime = DateTime.Now;
				await characteristic.WriteAsync(new byte[] { (byte)CommandType.TimeSync,
					(byte)(dateTime.Year - 2000), (byte)dateTime.Month, (byte)dateTime.Day,
					(byte)dateTime.Hour, (byte)dateTime.Minute, (byte)dateTime.Second
					});
			}
			catch{; }
		}

		private async Task ConnectDeviceByLabel(DeviceLabel deviceLabel, bool reauthorize = false)
		{
			if (!await Manager.RequestQueue()) return;
			using var rsaService = RSA.Create();
			using var source = new CancellationTokenSource();
			if (deviceLabel == null) goto connectReturn;
			if (deviceLabel.DeviceInfo == null) goto connectReturn;
			if(AdapterManager.GetPhysicalDevice(deviceLabel.DeviceInfo) != null) goto connectReturn;
			var adapter = Plugin.BLE.CrossBluetoothLE.Current.Adapter;
			async Task DisconnectNotify(string? notification)
			{
				await Manager.DisconnectToDeviceForce(deviceLabel.DeviceInfo, requestQueue: false);
				if (notification != null)
				{
					using var source = new CancellationTokenSource();
					await Toast.Make(notification).Show(source.Token);
				}
			}
			// Disconnect pervious device
			if (Manager.ConnectedDevice != null)
			{
				await Manager.DisconnectToDeviceForce(Manager.ConnectedDevice, requestQueue: false);
				if (!await Manager.RequestQueue()) return;
				Manager.ConnectedDevice = null;
			}

			// Check if the device is saved
			WkcDeviceInfo? savedDevice = null;
			foreach (var d in Manager.SavedDevices)
			{
				if (AdapterManager.CompareAddress(d, deviceLabel.DeviceInfo))
				{
					savedDevice = d;
					break;
				}
			}

			// Connect to the device first
			IDevice currentDevice;
			byte[]? publicKey = null;
			ICharacteristic? securityCharacteristic;
			try
			{
				currentDevice = await adapter.ConnectToKnownDeviceAsync(AddressToGuid(deviceLabel.DeviceInfo.Address));
				await currentDevice.RequestMtuAsync(240);
				// Grab public key
				securityCharacteristic = await AdapterManager.GetSecurityCharacteristicAsync(currentDevice);
				if (securityCharacteristic == null)
				{
					await DisconnectNotify(AppResources.MainPage_FailedToReadCharacteristicsForVerification);
					goto connectReturn;
				}
				var readResult = await securityCharacteristic.ReadAsync();
				publicKey = readResult.data;
				if (publicKey == null)
				{
					await DisconnectNotify(AppResources.MainPage_FailedToReadVerificationData);
					goto connectReturn;
				}
			}
			catch
			{
				await DisconnectNotify(AppResources.MainPage_FailedToConnectDevice);
				goto connectReturn;
			}

			rsaService.ImportRSAPublicKey(publicKey, out _);
			List<byte> verifyMessage;

			if (savedDevice == null || reauthorize)
			{
				var pairingResult = await Manager.ShowPairingPopupAsync(this);
				if (!pairingResult.Confirmed)
				{
					await DisconnectNotify(null);
					goto connectReturn;
				}
				verifyMessage = new([0]);
				verifyMessage.AddRange(BitConverter.GetBytes(pairingResult.Key));
				// Generate a random sequence
				string randomSequence = "";
				Random r = new Random();
				for (int i = 0; i < 16; i++)
					randomSequence += r.Next(0, 16).ToString("X1");
				verifyMessage.AddRange(Encoding.UTF8.GetBytes(randomSequence));
				deviceLabel.DeviceInfo.Key = randomSequence;
			}
			else
			{
				verifyMessage = new([1]);
				if (savedDevice == null || savedDevice.Key == null)
				{
					await DisconnectNotify(AppResources.MainPage_DeviceNotVerified);
					goto connectReturn;
				}
				verifyMessage.AddRange(Encoding.UTF8.GetBytes(savedDevice.Key));
			}

			if (securityCharacteristic.CanUpdate)
				await securityCharacteristic.StartUpdatesAsync();
			else
			{
				await DisconnectNotify(AppResources.MainPage_DeviceSecurityUpdateNotSupported);
				goto connectReturn;
			}
			await securityCharacteristic.WriteAsync(rsaService.Encrypt(verifyMessage.ToArray(), RSAEncryptionPadding.OaepSHA256));
			if (securityCharacteristic.Value.Length == 0 || securityCharacteristic.Value[0] != 0)
			{
				await DisconnectNotify(AppResources.MainPage_FailedToVerify);
				goto connectReturn;
			}
			await securityCharacteristic.StopUpdatesAsync();
			if (savedDevice != null && reauthorize) savedDevice.Key = deviceLabel.DeviceInfo.Key;
			if (savedDevice == null)
			{
				Manager.SavedDevices.Add(deviceLabel.DeviceInfo);
				savedDevice = deviceLabel.DeviceInfo;
				deviceLabel.IsSaved = true;
				Manager.SaveDevicePreference();
				NearbyDeviceLayout.Remove(deviceLabel);
				MyDeviceLayout.Add(deviceLabel);
			}
			var commandCharacteristics = await AdapterManager.GetCommandCharacteristicAsync(savedDevice);
			if (commandCharacteristics != null && commandCharacteristics.CanUpdate)
				await commandCharacteristics.StartUpdatesAsync();
			else
			{
				await DisconnectNotify(AppResources.MainPage_DeviceCommandUpdateNotSupported);
				goto connectReturn;
			}
			await SyncTime(savedDevice);
			await Toast.Make(AppResources.MainPage_DeviceConnected).Show(source.Token);
			Manager.ConnectedDevice = savedDevice;
			MyDeviceLayout.Remove(deviceLabel);
			MyDeviceLayout.Insert(0, deviceLabel);
		connectReturn:
			Manager.ReleaseQueue();
		}

		private void Adapter_ScanTimeoutElapsed(object? sender, EventArgs e)
		{
			foreach (var scanned in Manager.ScannedDevices)
			{
				bool isDeviceExists = false;
				foreach (var scanning in Manager.ScanningDevices)
				{
					if (AdapterManager.CompareAddress(scanning, scanned))
					{
						isDeviceExists = true;
						break;
					}
				}
				if (!isDeviceExists)
				{
					scanned.Signal = -1;
					for(int i = NearbyDeviceLayout.Count - 1; i >= 0; i--)
					{
						var deviceLabel = NearbyDeviceLayout[i] as DeviceLabel;
						if (deviceLabel == null) continue;
						if(AdapterManager.CompareAddress(deviceLabel.DeviceInfo, scanned))
							NearbyDeviceLayout.RemoveAt(i);
					}
					foreach (var control in MyDeviceLayout)
					{
						var deviceLabel = control as DeviceLabel;
						if (deviceLabel == null) continue;
						if (AdapterManager.CompareAddress(deviceLabel.DeviceInfo, scanned) &&
							deviceLabel.DeviceInfo != null)
						{
							deviceLabel.DeviceInfo.Signal = -1;
							deviceLabel.Update();
						}
					}
				}
			}
		}

		private async void ContentPage_Loaded(object sender, EventArgs e)
		{
			ListSavedDevices();
			ResetSignal();
			await ScanAsync();
		}

		private async void RefreshView_Refreshing(object sender, EventArgs e)
		{
			MainRefreshView.IsRefreshing = false;
			await ScanAsync();
		}

		private async void MainPage_NavigatedTo(object? sender, NavigatedToEventArgs e)
		{
			ListSavedDevices();
			await ScanAsync();
		}
	}
}
