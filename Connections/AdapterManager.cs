using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Newtonsoft.Json;
using Plugin.BLE.Abstractions.Contracts;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using WkcCommunicator.Controls;
using WkcCommunicator.Types;

namespace WkcCommunicator.Connections
{
	internal class CheckPermissions : Permissions.BasePlatformPermission
	{
#if ANDROID
		public override (string androidPermission, bool isRuntime)[] RequiredPermissions => GetRequiredPermissions();

		private (string androidPermission, bool isRuntime)[] GetRequiredPermissions()
		{
			var permissions = new List<string>();

			if (DeviceInfo.Version.Major >= 12)
			{
				permissions.Add(global::Android.Manifest.Permission.BluetoothScan);
				permissions.Add(global::Android.Manifest.Permission.BluetoothConnect);
			}
			permissions.Add(global::Android.Manifest.Permission.Bluetooth);
			permissions.Add(global::Android.Manifest.Permission.BluetoothAdmin);
			permissions.Add(global::Android.Manifest.Permission.AccessCoarseLocation);
			permissions.Add(global::Android.Manifest.Permission.AccessFineLocation);

			var result = new List<(string androidPermission, bool isRuntime)>();
			foreach (var permission in permissions)
			{
				result.Add((permission, true));
			}

			return result.ToArray();
		}
#endif
	}

	public class AdapterManager
	{
		public List<WkcDeviceInfo> SavedDevices { get; private set; }
		public List<WkcDeviceInfo> ScannedDevices { get; private set; } = new List<WkcDeviceInfo>();
		public List<WkcDeviceInfo> ScanningDevices { get; private set; } = new List<WkcDeviceInfo>();
		private WkcDeviceInfo? _connectedDevice;
		ConcurrentQueue<TaskCompletionSource<bool>> RequestTcs = new ConcurrentQueue<TaskCompletionSource<bool>>();
		public bool AllowDisconnect { get; set; } = true;

		public WkcDeviceInfo? ConnectedDevice
		{
			get => _connectedDevice;
			set
			{
				_connectedDevice = value;
				ConnectedDeviceChanged?.Invoke(this, new EventArgs());
			}
		}

		public event EventHandler? ConnectedDeviceChanged;
		public event EventHandler? DeviceDeleted;

		public static string AddressToString(byte[]? addressBytes)
		{
			string result = "";
			if (addressBytes != null)
				for (int i = 0; i < addressBytes.Length; i++)
				{
					result += addressBytes[i].ToString("X2");
					if (i < addressBytes.Length - 1)
						result += ":";
				}
			return result;
		}

		public static bool CompareAddress(byte[]? address1, byte[]? address2)
		{
			if (address1 == null || address2 == null) return false;
			return AddressToString(address1) == AddressToString(address2);
		}

		public static bool CompareAddress(WkcDeviceInfo? device1, WkcDeviceInfo? device2)
		{
			if (device1 == null || device2 == null) return false;
			return CompareAddress(device1.Address, device2.Address);
		}

		public static bool CompareAddress(Guid addressGuid, byte[]? addressByte)
		{
			return CompareAddress(addressGuid.ToByteArray().TakeLast(6).ToArray(), addressByte);
		}

		public static bool CompareAddress(IDevice? physicalDevice, WkcDeviceInfo? virtualDevice)
		{
			if (physicalDevice == null || virtualDevice == null) return false;
			return CompareAddress(physicalDevice.Id, virtualDevice.Address);
		}

		public static IDevice? GetPhysicalDevice(WkcDeviceInfo? deviceInfo)
		{
			if (deviceInfo == null) return null;
			var adapter = Plugin.BLE.CrossBluetoothLE.Current.Adapter;
			foreach (var d in adapter.ConnectedDevices)
			{
				var rawAddress = d.Id.ToByteArray().TakeLast(6).ToArray();
				if (AddressToString(rawAddress) == AddressToString(deviceInfo.Address))
					return d;
			}
			return null;
		}

		private static async Task<ICharacteristic?> GetKnownCharacteristicAsync(IDevice? physicalDevice, ushort knownServiceId, ushort knownCharacteristicId)
		{
			if (physicalDevice == null)
				return null;
			try
			{
				await physicalDevice.RequestMtuAsync(240);
				var services = await physicalDevice.GetServicesAsync();
				foreach (var s in services)
				{
					var serviceId = s.Id.ToByteArray();
					if (serviceId[1] != knownServiceId / 256 || serviceId[0] != knownServiceId % 256) continue;
					var characteristics = await s.GetCharacteristicsAsync();
					foreach (var c in characteristics)
					{
						if (!c.CanRead || !c.CanWrite || !c.CanUpdate) continue;
						if (c.Uuid.Substring(4, 4) != knownCharacteristicId.ToString("x04")) continue;
						return c;
					}
				}
				return null;
			}
			catch { return null; }
		}

		private static async Task<ICharacteristic?> GetKnownCharacteristicAsync(WkcDeviceInfo? device, ushort knownServiceId, ushort knownCharacteristicId)
		{
			if (device == null) return null;
			var adapter = Plugin.BLE.CrossBluetoothLE.Current.Adapter;
			IDevice? physicalDevice = GetPhysicalDevice(device);
			return await GetKnownCharacteristicAsync(physicalDevice, knownServiceId, knownCharacteristicId);
		}

		public static async Task<ICharacteristic?> GetSecurityCharacteristicAsync(WkcDeviceInfo? device) =>
			await GetKnownCharacteristicAsync(device, 0xA000, 0xA001);
		public static async Task<ICharacteristic?> GetSecurityCharacteristicAsync(IDevice? device) =>
			await GetKnownCharacteristicAsync(device, 0xA000, 0xA001);

		public static async Task<ICharacteristic?> GetCommandCharacteristicAsync(WkcDeviceInfo? device)
			=> await GetKnownCharacteristicAsync(device, 0xA000, 0xA002);
		public static async Task<ICharacteristic?> GetCommandCharacteristicAsync(IDevice? device)
			=> await GetKnownCharacteristicAsync(device, 0xA000, 0xA002);

		public static async Task<bool> CheckPermissionAsync()
		{
#if ANDROID
			var status = await Permissions.CheckStatusAsync<CheckPermissions>();
			if (status == PermissionStatus.Granted) return true;

			status = await Permissions.RequestAsync<CheckPermissions>();
			if (status == PermissionStatus.Granted) return true;
			return false;
#else
			return true;
#endif
		}

		public AdapterManager()
		{
			string devicesPreference = Preferences.Get("SavedDevices", "[]");
			Debug.WriteLine(devicesPreference);
			SavedDevices = JsonConvert.DeserializeObject<List<WkcDeviceInfo>>(devicesPreference);
			if (SavedDevices == null) SavedDevices = new List<WkcDeviceInfo>();
			foreach (var d in SavedDevices)
			{
				Debug.WriteLine(AddressToString(d.Address));
			}
		}

		public void SaveDevicePreference()
		{
			string saved = JsonConvert.SerializeObject(SavedDevices);
			Preferences.Set("SavedDevices", saved);
		}

		public async Task<bool> RequestQueue()
		{
			TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
			RequestTcs.Enqueue(tcs);
			if(RequestTcs.Count > 1)
				return await tcs.Task;
			return true;
		}

		public void ClearRequest()
		{
			while (RequestTcs.Count > 0)
			{
				TaskCompletionSource<bool>? dequeue = null;
				RequestTcs.TryDequeue(out dequeue);
				if (dequeue != null) dequeue.TrySetResult(false);
				Debug.WriteLine($"Remove Tcs: {RequestTcs.Count}");
			}
		}

		public void ReleaseQueue()
		{
			if (RequestTcs.Count > 0)
			{
				TaskCompletionSource<bool>? dequeue, peek = null;
				RequestTcs.TryDequeue(out dequeue);
				RequestTcs.TryPeek(out peek);
				if (peek != null) peek.SetResult(true);
			}
		}

		public async Task SendCustomCommandAsync(byte[]? command, Action<byte[]>? callback, string failedInfo = "")
		{
			if (ConnectedDevice == null) return;
			bool request = await RequestQueue();
			if (!request) return;
			var commandCharacteristic = await GetCommandCharacteristicAsync(ConnectedDevice);
			if (commandCharacteristic != null && command != null)
			{
				try
				{
					await commandCharacteristic.WriteAsync(command);
					var result = commandCharacteristic.Value;
					if(callback != null)
						callback(result);
				}
				catch
				{
					Debug.WriteLine(failedInfo);
				}
			}
			ReleaseQueue();
		}

		public async Task DisconnectToDeviceForce(WkcDeviceInfo? deviceInfo, bool connectionLost = false)
		{
			ClearRequest();
			if (deviceInfo == null || !AllowDisconnect) return;
			var adapter = Plugin.BLE.CrossBluetoothLE.Current.Adapter;
			var physicalDevice = GetPhysicalDevice(deviceInfo);
			if (physicalDevice == null)
			{
				if (connectionLost)
				{
					ConnectedDevice = null;
				}
				return;
			}
			if (physicalDevice.State != Plugin.BLE.Abstractions.DeviceState.Disconnected)
			{
				try
				{
					Debug.WriteLine("Try to disconnect");
					if (CompareAddress(ConnectedDevice, deviceInfo))
					{
						try
						{
							var characteristic = await GetCommandCharacteristicAsync(ConnectedDevice);
							if (characteristic != null && characteristic.WriteType == Plugin.BLE.Abstractions.CharacteristicWriteType.WithResponse)
								await characteristic.StopUpdatesAsync();
						}
						catch { Debug.WriteLine("Failed to stop update"); }
						ConnectedDevice = null;
					}
					physicalDevice = GetPhysicalDevice(deviceInfo);
					if (!connectionLost && physicalDevice != null)
					{
						CancellationToken token = new CancellationToken();
						await adapter.DisconnectDeviceAsync(physicalDevice);
						physicalDevice.Dispose();
					}
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.Message);
				}
			}
		}

		public async Task DeleteDevice(WkcDeviceInfo deviceInfo)
		{
			if (!AllowDisconnect) return;
			await DisconnectToDeviceForce(deviceInfo);

			for (int i = SavedDevices.Count - 1; i >= 0; i--)
			{
				if (CompareAddress(SavedDevices[i], deviceInfo))
				{
					SavedDevices.RemoveAt(i);
					SaveDevicePreference();
					break;
				}
			}

			for (int i = ScannedDevices.Count - 1; i >= 0; i--)
			{
				if (CompareAddress(ScannedDevices[i], deviceInfo))
				{
					ScannedDevices.RemoveAt(i);
					break;
				}
			}

			DeviceDeleted?.Invoke(this, new EventArgs());
		}

		public async Task<(bool Confirmed, int Key)> ShowPairingPopupAsync(Page parent)
		{
			var pairingView = new PairingView(parent);
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
			popup.Content = pairingView;
			await parent.ShowPopupAsync(popup, popupOptions);
			return (pairingView.Confirmed, pairingView.Key);
		}
		public async Task ShowDeletePopupAsync(Page parent, WkcDeviceInfo deviceInfo)
		{
			var deleteView = new DeleteView(parent);
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
			popup.Content = deleteView;
			popupOptions.Shape = shape;
			await parent.ShowPopupAsync(popup, popupOptions);
			if (deleteView.Confirmed) await DeleteDevice(deviceInfo);
		}
	}
}
