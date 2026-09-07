using Android.OS.Strictmode;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Behaviors;
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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
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
		public List<WkcDeviceInfo>? SavedDevices { get; private set; }
		public List<WkcDeviceInfo> ScannedDevices { get; private set; } = new List<WkcDeviceInfo>();
		public List<WkcDeviceInfo> ScanningDevices { get; private set; } = new List<WkcDeviceInfo>();
		private WkcDeviceInfo? _connectedDevice;
		ConcurrentQueue<TaskCompletionSource<bool>> RequestTcs { get; set; } = new ConcurrentQueue<TaskCompletionSource<bool>>();
		public bool AllowDisconnect { get; set; } = true;
		private List<Border> RegisteredGroups { get; set; } = new List<Border>();
		private List<TableItemControl> RegisteredControls { get; set; } = new List<TableItemControl>();
		public byte[]? AES { get; set; }
		public DateTime NotifyTimestamp{ get; set; }
		public DateTime ValueTimestamp{ get; set; }

		public WkcDeviceInfo? ConnectedDevice
		{
			get => _connectedDevice;
			set
			{
				_connectedDevice = value;
				ConnectedDeviceChanged?.Invoke(this, new EventArgs());
				Task.Run(async () =>
				{
					var characteristic = await GetCommandCharacteristicAsync(value);
					if (characteristic != null)
						characteristic.ValueUpdated += CommandCharacteristic_ValueUpdated;
				});
			}
		}

		private async void CommandCharacteristic_ValueUpdated(object? sender, Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
		{
			string itemName;
			byte[]? result = DecodeResult(ConnectedDevice, e.Characteristic.Value, true, false);
			if (result == null || result.Length < 1 || result[0] != 0x10) return;
			itemName = Encoding.UTF8.GetString(result.Skip(1).ToArray());
			foreach (var control in RegisteredControls)
				if (control.Name == itemName)
				{
					await control.UpdateAsync();
				}
		}

		private void ClearControl()
		{
			foreach (var control in RegisteredControls)
				control.DisconnectHandlers();
			RegisteredControls.Clear();
			foreach (var group in RegisteredGroups)
				group.DisconnectHandlers();
			RegisteredGroups.Clear();
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

		public static DateTime InitTime() => new DateTime(0, DateTimeKind.Local).AddYears(1970);

		public static byte[] GetTimeBytes(DateTime time)
		{
			DateTime standardTime = InitTime();
			TimeSpan timeDiff = time - standardTime;
			long timeResult = (long)timeDiff.TotalMicroseconds;
			return [
				.. BitConverter.GetBytes(timeResult)
			];

		}

		public static DateTime GetTimeFromBytes(byte[] data)
		{
			DateTime result = InitTime();
			if (data.Length < 8) return result;
			result = result.AddMicroseconds(BitConverter.ToInt64(data, 0));
			return result;
		}

		public static bool IsDeviceLegacy(WkcDeviceInfo ?device)
		{
			if (device == null) return false;
			return device.ProtocolVersion == null || device.ProtocolVersion[0] == 0 && device.ProtocolVersion[1] <= 1;
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

		private void ClearRequest()
		{
			while (RequestTcs.Count > 0)
			{
				TaskCompletionSource<bool>? dequeue = null;
				RequestTcs.TryDequeue(out dequeue);
				if (dequeue != null) dequeue.TrySetResult(false);
				Debug.WriteLine($"Remove Tcs: {RequestTcs.Count}");
			}
			ClearControl();
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

		public async Task<byte[]?> SendCustomCommandAsync(byte[]? command)
		{
			if (ConnectedDevice == null) return null;
			var commandCharacteristic = await GetCommandCharacteristicAsync(ConnectedDevice);
			if (commandCharacteristic != null && command != null)
			{
				try
				{
					byte[]? commandEncoded = EncodeCommand(command);
					if (commandEncoded == null) return null;
					await commandCharacteristic.WriteAsync(commandEncoded);
					var result = DecodeResult(ConnectedDevice, commandCharacteristic.Value, false, true);
					return result;
				}
				catch
				{
					return null;
				}
			}
			else return null;
		}

		public async Task<byte[]?> GetCommandOutputAsync(Action? failedCallback = null)
		{
			if (ConnectedDevice == null) return null;
			var commandCharacteristic = await GetCommandCharacteristicAsync(ConnectedDevice);
			//byte[]? output = null;
			List<byte> outputList = new List<byte>();
			if (commandCharacteristic != null)
			{
				try
				{
					byte[]? output = null;
					do
					{
						var result = await commandCharacteristic.ReadAsync();
						if (result.resultCode == 0)
						{
							output = DecodeResult(ConnectedDevice, result.data, false, true);
							if (output == null) throw new InvalidDataException();
							outputList.AddRange(output.Skip(1));
						}
						else break;
					} while (output != null && output.Length > 0 && output[0] != 0);
				}
				catch
				{
					if (failedCallback != null)
						failedCallback();
					return null;
				}
			}
			return outputList.ToArray();
		}

		public TableGroup[]? ParseTableGroups(string tableGroups)
		{
			if (ConnectedDevice == null) return null;
			JsonNode? node;
			try
			{
				node = JsonNode.Parse(tableGroups);
			}
			catch
			{
				return null;
			}

			if (node == null || node.GetValueKind() != JsonValueKind.Array) return null;
			JsonArray array = node.AsArray();
			List<TableGroup> result = new List<TableGroup>();
			foreach (var group in array)
			{
				if (group == null) continue;
				JsonNode? groupNameNode = group["name"];
				JsonNode? groupDisplayNameNode = group["display_name"];
				string groupName, groupDisplayName;
				if (groupNameNode == null || groupNameNode.GetValueKind() != JsonValueKind.String)
				{
					if (IsDeviceLegacy(ConnectedDevice))
					{
						groupName = AppResources.Table_UnnamedGroup;
						groupDisplayName = groupName;
					}
					else continue;
				}
				else
				{
					groupName = groupNameNode.GetValue<string>();
					if (IsDeviceLegacy(ConnectedDevice))
						groupDisplayName = groupName;
					else
					{
						if (groupDisplayNameNode == null || groupDisplayNameNode.GetValueKind() != JsonValueKind.String)
							groupDisplayName = AppResources.Table_UnnamedGroup;
						else
							groupDisplayName = groupDisplayNameNode.GetValue<string>();
					}
				}
				List<TableItem> items = new List<TableItem>();
				JsonNode? itemsNode = group["items"];
				JsonArray itemsArray;
				if (itemsNode != null && itemsNode.GetValueKind() == JsonValueKind.Array)
					itemsArray = itemsNode.AsArray();
				else
				{
					result.Add(new TableGroup() { Name = groupName, DisplayName = groupDisplayName, Items = null });
					continue;
				}

				foreach (var item in itemsArray)
				{
					try
					{
						if (item == null) continue;
						JsonNode? typeNode = item["type"];
						if (typeNode == null || typeNode.GetValueKind() != JsonValueKind.String) continue;
						string typeString = typeNode.GetValue<string>();
						TableItem tableItem = new TableItem();
						JsonNode? itemNameNode = item["name"];
						JsonNode? displayNameNode = item["display_name"];
						JsonNode? optionsNode = item["options"];
						JsonNode? valueNode = item["value"];
						JsonNode? minNode = item["min"];
						JsonNode? maxNode = item["max"];
						JsonNode? lengthNode = item["length"];
						if (itemNameNode == null || itemNameNode.GetValueKind() != JsonValueKind.String)
							continue;
						else
							tableItem.Name = itemNameNode.GetValue<string>();
						if (displayNameNode == null || displayNameNode.GetValueKind() != JsonValueKind.String)
							tableItem.DisplayName = AppResources.Table_UnnamedProperty;
						else
							tableItem.DisplayName = displayNameNode.GetValue<string>();
						tableItem.Type = typeString switch
						{
							"action" => TableItemType.Action,
							"switch" => TableItemType.Switch,
							"integer" => TableItemType.Integer,
							"decimal" => TableItemType.Decimal,
							"picker" => TableItemType.Picker,
							"string" => TableItemType.String,
							_ => TableItemType.Unknown
						};
						switch (tableItem.Type)
						{
							case TableItemType.Action:
							case TableItemType.Picker:
								if (optionsNode != null && optionsNode.GetValueKind() == JsonValueKind.Array)
								{
									List<string> optionsList = new List<string>();
									JsonArray optionsArray = optionsNode.AsArray();
									foreach (var option in optionsArray)
									{
										if (option == null || option.GetValueKind() != JsonValueKind.String)
											optionsList.Add(AppResources.Table_UnnamedOption);
										else
											optionsList.Add(option.GetValue<string>());
									}
									tableItem.Options = optionsList.ToArray();
								}
								if (valueNode != null && valueNode.GetValueKind() == JsonValueKind.Number)
									tableItem.NumberValue = Convert.ToInt32(valueNode.GetValue<double>());
								break;
							case TableItemType.Switch:
								if (valueNode != null && valueNode.GetValueKind() == JsonValueKind.True)
									tableItem.BoolValue = true;
								break;
							case TableItemType.Integer:
							case TableItemType.Decimal:
								if (minNode != null && minNode.GetValueKind() == JsonValueKind.Number)
									tableItem.Min = tableItem.Type == TableItemType.Integer ? Convert.ToInt32(minNode.GetValue<double>()) : minNode.GetValue<double>();
								else
									tableItem.Min = tableItem.Type == TableItemType.Integer ? int.MinValue : double.MinValue;

								if (maxNode != null && maxNode.GetValueKind() == JsonValueKind.Number)
									tableItem.Max = tableItem.Type == TableItemType.Integer ? Convert.ToInt32(maxNode.GetValue<double>()) : maxNode.GetValue<double>();
								else
									tableItem.Max = tableItem.Type == TableItemType.Integer ? int.MaxValue : double.MaxValue;

								if (tableItem.Min > tableItem.Max)
									continue;
								if (valueNode != null && valueNode.GetValueKind() == JsonValueKind.Number)
									tableItem.NumberValue = tableItem.Type == TableItemType.Integer ? Convert.ToInt32(valueNode.GetValue<double>()) : valueNode.GetValue<double>();
								if (tableItem.NumberValue < tableItem.Min) tableItem.NumberValue = tableItem.Min;
								if (tableItem.NumberValue > tableItem.Max) tableItem.NumberValue = tableItem.Max;
								break;
							case TableItemType.String:
								if (lengthNode != null && lengthNode.GetValueKind() == JsonValueKind.Number)
									tableItem.StringLength = Convert.ToInt32(lengthNode.GetValue<double>());
								else
									tableItem.StringLength = 30;
								if (valueNode != null && valueNode.GetValueKind() == JsonValueKind.String)
									tableItem.StringValue = valueNode.GetValue<string>();
								else
									tableItem.StringValue = "";
								break;
							default:
								continue;
						}
						items.Add(tableItem);
					}
					catch
					{
						continue;
					}
				}

				result.Add(new TableGroup() { Name = groupName, DisplayName = groupDisplayName, Items = items.ToArray() });
			}
			return result.ToArray();
		}

		public void InsetTableToLayout(TableGroup[]? groups, Layout layout, TableGroupType type)
		{
			if (groups == null) return;
			foreach (var group in groups)
			{
				Border groupBorder = new Border()
				{
					Margin = new Thickness(24, 0),
					Padding = new Thickness(12),
					StrokeThickness = 0,
					StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle()
					{
						CornerRadius = 12
					}
				};
				if (Application.Current != null)
					groupBorder.SetAppTheme(VisualElement.BackgroundColorProperty, Application.Current.Resources["SurfaceContainerLight"],
					Application.Current.Resources["SurfaceContainerDark"]);

				VerticalStackLayout groupLayout = new VerticalStackLayout()
				{
					Spacing = 12
				};
				Label groupTitle = new Label()
				{
					Margin = new Thickness(4),
					FontSize = Convert.ToDouble(new FontSizeConverter().ConvertFromString("Large")),
					Text = group.DisplayName ?? Resources.AppResources.Table_UnnamedGroup,
				};
				groupLayout.Add(groupTitle);
				if (group.Items != null)
					foreach (var item in group.Items)
					{
						if (item == null) continue;
						TableItemControl control = new TableItemControl(this, item, type);
						if (control.Name != null)
						{
							RegisteredControls.Add(control);
							groupLayout.Add(control);
						}
						else
						{
							control.DisconnectHandlers();
						}
					}
				groupBorder.Content = groupLayout;
				RegisteredGroups.Add(groupBorder);
				layout.Add(groupBorder);
			}
		}

		public async Task DisconnectToDeviceForce(WkcDeviceInfo? deviceInfo, bool connectionLost = false, bool requestQueue = true)
		{
			if (requestQueue && !await RequestQueue()) return;
			try
			{
				if (deviceInfo == null || !AllowDisconnect)
				{
					return;
				}
				var adapter = Plugin.BLE.CrossBluetoothLE.Current.Adapter;
				var physicalDevice = GetPhysicalDevice(deviceInfo);
				if (physicalDevice == null)
				{
					ConnectedDevice = null;
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
			finally
			{
				ClearRequest();
				NotifyTimestamp = InitTime();
				ValueTimestamp = InitTime();
			}
		}

		public byte[]? DecodeResult(WkcDeviceInfo? device, byte[]? source, bool appendNotify, bool appendValue)
		{
			if (device == null) return null;
			if (IsDeviceLegacy(device) || source == null) 
				return source;
			// Data format: 12-byte IV, AES(Data, Timestamp(microseconds from 1970/1/1)), 16-byte tag
			if (source.Length <= 36 || AES == null) return null;
			byte[] iv = source.Take(12).ToArray();
			byte[] tag = source.TakeLast(16).ToArray();
			byte[] ciphertext = source.Skip(12).SkipLast(16).ToArray();
			byte[] result = new byte[ciphertext.Length];
			using var aes = new AesGcm(AES, 16);
			try
			{
				aes.Decrypt(iv, source.Skip(12).SkipLast(16).ToArray(), tag, result);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.Message);
				return null;
			}
			byte[] timestamp = result.TakeLast(8).ToArray();
			byte[] data = result.SkipLast(8).ToArray();
			DateTime timestampValue = GetTimeFromBytes(timestamp);
			bool notifyPass = false, valuePass = false;
			if (appendNotify)
			{
				if (timestampValue > NotifyTimestamp) notifyPass = true;
			}
			else
				notifyPass = true;

			if (appendValue)
			{
				if (timestampValue > ValueTimestamp) valuePass = true;
			}
			else
				valuePass = true;

			if (notifyPass && valuePass)
			{
				if (appendNotify) NotifyTimestamp = timestampValue;
				if (appendValue) ValueTimestamp = timestampValue;
				return data;
			}
			else
			{
				return null;
			}
		}

		public byte[]? EncodeCommand(byte[]? source)
		{
			if (source == null || ConnectedDevice == null) return null;
			bool legacy = IsDeviceLegacy(ConnectedDevice);
			if (legacy) return source;
			if (AES == null) return null;
			using var aes = new AesGcm(AES, 16);
			byte[] iv = new byte[12];
			byte[] tag = new byte[16];
			byte[] plaintext = [.. source, .. GetTimeBytes(DateTime.Now)];
			byte[] result = new byte[plaintext.Length];
			RandomNumberGenerator.Fill(iv);
			aes.Encrypt(iv, plaintext, result, tag);
			return [.. iv, .. result, .. tag];
		}

		public bool VerifySecurity(WkcDeviceInfo device, byte[] result)
		{
			if (device == null) return false;
			byte[]? decoded = DecodeResult(device, result, true, true);
			if (decoded == null || decoded.Length < 1) return false;
			return decoded[0] == 0;
		}

		public async Task DeleteDevice(WkcDeviceInfo deviceInfo)
		{
			if (!AllowDisconnect) return;
			await DisconnectToDeviceForce(deviceInfo);
			if (SavedDevices == null) throw new NullReferenceException("SavedDevice is null");
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
			await App.ShowCommonPopupAsync(parent, pairingView);
			return (pairingView.Confirmed, pairingView.Key);
		}
		public async Task ShowDeletePopupAsync(Page parent, WkcDeviceInfo deviceInfo)
		{
			var deleteView = new DeleteView(parent);
			await App.ShowCommonPopupAsync(parent, deleteView);
			if (deleteView.Confirmed) await DeleteDevice(deviceInfo);
		}
	}
}
