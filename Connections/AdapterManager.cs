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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

		public async Task<byte[]?> SendCustomCommandAsync(byte[]? command, bool notify = false)
		{
			if (ConnectedDevice == null) return null;
			var commandCharacteristic = await GetCommandCharacteristicAsync(ConnectedDevice);
			if (commandCharacteristic != null && command != null)
			{
				try
				{
					if (notify)
						await commandCharacteristic.StartUpdatesAsync();
					await commandCharacteristic.WriteAsync(command);
					if (notify)
						await commandCharacteristic.StopUpdatesAsync();
					var result = commandCharacteristic.Value;
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
							output = result.data;
							outputList.AddRange(output.Skip(1));
						}
						else break;
					} while (output != null && output.Length > 0 && output[0] != 0);
				}
				catch
				{
					if (failedCallback != null)
						failedCallback();
				}
			}
			return outputList.ToArray();
		}

		public static TableGroup[]? ParseTableGroups(string tableGroups)
		{
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
				string groupName;
				if (groupNameNode == null || groupNameNode.GetValueKind() != JsonValueKind.String)
					groupName = AppResources.Table_UnnamedGroup;
				else
					groupName = groupNameNode.GetValue<string>();
				List<TableItem> items = new List<TableItem>();
				JsonNode? itemsNode = group["items"];
				JsonArray itemsArray;
				if (itemsNode != null && itemsNode.GetValueKind() == JsonValueKind.Array)
					itemsArray = itemsNode.AsArray();
				else
				{
					result.Add(new TableGroup() { Name = groupName, Items = null });
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

				result.Add(new TableGroup() { Name = groupName, Items = items.ToArray() });
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
					Text = group.Name ?? Resources.AppResources.Table_UnnamedGroup
				};
				groupLayout.Add(groupTitle);
				if (group.Items != null)
					foreach (var item in group.Items)
					{
						if (item.Name == null) continue;
						Grid itemGrid = new Grid()
						{
							HorizontalOptions = LayoutOptions.Fill
						};
						Label displayNameLabel = new Label()
						{
							Text = item.DisplayName,
							HorizontalOptions = LayoutOptions.Start,
							VerticalOptions = LayoutOptions.Center,
							FontSize = Convert.ToDouble(new FontSizeConverter().ConvertFromString("Medium")),
							MaxLines = 1,
							MinimumWidthRequest = 150,
						};
						itemGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
						itemGrid.Add(displayNameLabel);
						itemGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
						itemGrid.ColumnSpacing = 12;
						ConcurrentQueue<JsonNode?> transmissionQueue = new ConcurrentQueue<JsonNode?>();
						bool onTransmit = false;
						byte[]? generateCommit(JsonNode? data)
						{
							if (item.Name == null) return null;
							JsonObject commandObject = new JsonObject();
							commandObject.Add(item.Name, data);
							List<byte> optionCommand = new List<byte>();
							optionCommand.Add(Convert.ToByte(type == TableGroupType.Shortcut ? CommandType.WriteShortcut : CommandType.WriteSettings));
							optionCommand.AddRange(Encoding.UTF8.GetBytes(commandObject.ToJsonString()));
							return optionCommand.ToArray();
						}
						async Task commit(JsonNode? data)
						{
							if (item.Name == null) return;
							bool lastOnTransmit = onTransmit;
							onTransmit = true;
							if (lastOnTransmit)
							{
								transmissionQueue.Enqueue(data);
								return;
							}
							await RequestQueue();
							byte[]? generated;
							generated = generateCommit(data);
							if (generated != null)
								await this.SendCustomCommandAsync(generated);
							while (transmissionQueue.Count != 0)
							{
								int tCount = transmissionQueue.Count;
								JsonNode? outNode = null;
								for(int i = 0; i < tCount; i++)
									transmissionQueue.TryDequeue(out outNode);
								generated = generateCommit(outNode);
								if (generated != null)
									await this.SendCustomCommandAsync(generated);
							}
							ReleaseQueue();
							onTransmit = false;
						}
						switch (item.Type)
						{
							case TableItemType.Action:
								ScrollView actionScroll = new ScrollView()
								{
									Orientation = ScrollOrientation.Horizontal,
									HorizontalOptions = LayoutOptions.Fill,
									VerticalOptions = LayoutOptions.Center,
								};
								itemGrid.Add(actionScroll);
								itemGrid.SetColumn(actionScroll, 1);
								Grid actionGrid = new Grid()
								{
									HorizontalOptions = LayoutOptions.Fill,
									VerticalOptions = LayoutOptions.Fill,
								};
								actionScroll.Content = actionGrid;
								HorizontalStackLayout actionStackLayout = new HorizontalStackLayout()
								{
									VerticalOptions = LayoutOptions.Center,
									HorizontalOptions = LayoutOptions.End,
									Spacing = 6
								};
								async Task updateStackAlignment(ScrollView scrollView, HorizontalStackLayout stackLayout)
								{
									double viewportWidth = scrollView.Width;
									double contentDesiredWidth = stackLayout.DesiredSize.Width;

									if (viewportWidth <= 0)
										return;

									if (contentDesiredWidth <= viewportWidth)
									{
										stackLayout.HorizontalOptions = LayoutOptions.End;
										scrollView.Orientation = ScrollOrientation.Neither;
										await scrollView.ScrollToAsync(0, 0, false);
									}
									else
									{
										stackLayout.HorizontalOptions = LayoutOptions.Start;
										scrollView.Orientation = ScrollOrientation.Horizontal;
									}
								}
								actionScroll.SizeChanged += async (sender, e) => await updateStackAlignment(actionScroll, actionStackLayout);
								actionStackLayout.SizeChanged += async (sender, e) => await updateStackAlignment(actionScroll, actionStackLayout);
								actionGrid.Add(actionStackLayout);
								if (item.Options != null)
									for (int i = 0; i < item.Options.Length; i++)
									{
										Controls.UnaccentedButton optionButton = new UnaccentedButton()
										{
											Text = item.Options[i],
											MinimumWidthRequest = 60
										};
										int optionIndex = i;
										optionButton.Clicked += async (sender, e) => await commit(optionIndex);
										actionStackLayout.Add(optionButton);
									}
								break;
							case TableItemType.Switch:
								Microsoft.Maui.Controls.Switch commandSwitch = new Microsoft.Maui.Controls.Switch();
								commandSwitch.IsToggled = item.BoolValue;
								itemGrid.Add(commandSwitch);
								itemGrid.SetColumn(commandSwitch, 1);
								commandSwitch.HorizontalOptions = LayoutOptions.End;
								commandSwitch.Toggled += async (sender, e) => await commit(commandSwitch.IsToggled);
								break;
							case TableItemType.Integer:
							case TableItemType.Decimal:
								Grid numberGrid = new Grid();
								numberGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
								numberGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
								numberGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
								numberGrid.ColumnSpacing = 6;
								Entry numberEntry = new Entry();
								UnaccentedButton numberUpButton = new UnaccentedButton() { Text = "+"};
								UnaccentedButton numberDownButton = new UnaccentedButton() { Text = "-" };
								numberEntry.Keyboard = Keyboard.Numeric;
								double currentValueDecimal = item.NumberValue;
								int currentValueInt = Convert.ToInt32(item.NumberValue);
								numberEntry.Text = item.Type == TableItemType.Decimal ? currentValueDecimal.ToString() : currentValueInt.ToString();
								numberUpButton.WidthRequest = 24;
								numberDownButton.WidthRequest = 24;
								numberEntry.VerticalOptions = LayoutOptions.Center;
								numberUpButton.VerticalOptions = LayoutOptions.Center;
								numberDownButton.VerticalOptions = LayoutOptions.Center;
								bool isValueValid = true;
								numberEntry.TextChanged += async (sender, e) =>
								{
									string originalText = numberEntry.Text;
									string formattedText = "";
									bool exceed = true;
									for (int i = 0; i < originalText.Length; i++)
									{
										if (char.IsAsciiDigit(originalText[i]) || originalText[i] == '-' ||
											originalText[i] == '.' && item.Type == TableItemType.Decimal)
											formattedText += originalText[i];
									}
									try
									{
										if (item.Type == TableItemType.Integer)
										{
											currentValueInt = Convert.ToInt32(formattedText);
											if (currentValueInt < Convert.ToInt32(item.Min)) currentValueInt = Convert.ToInt32(item.Min);
											else if (currentValueInt > Convert.ToInt32(item.Max)) currentValueInt = Convert.ToInt32(item.Max);
											else exceed = false;
											if (exceed)
												formattedText = currentValueInt.ToString();
										}
										else
										{
											currentValueDecimal = Convert.ToDouble(formattedText);
											if (currentValueDecimal < item.Min) currentValueDecimal = item.Min;
											else if (currentValueDecimal > item.Max) currentValueDecimal = item.Max;
											else exceed = false;
											if (exceed)
												formattedText = currentValueDecimal.ToString("0.###");
										}
										isValueValid = true;
									}
									catch
									{
										isValueValid = false;
									}
									if (originalText != formattedText)
									{
										numberEntry.Text = formattedText;
										numberEntry.CursorPosition = formattedText.Length;
									}
									if (isValueValid)
										await commit(item.Type == TableItemType.Integer ? currentValueInt : currentValueDecimal);
								};
								numberUpButton.Clicked += (sender, e) =>
								{
									if (item.Type == TableItemType.Integer)
									{
										if (++currentValueInt > Convert.ToInt32(item.Max)) currentValueInt = Convert.ToInt32(item.Max);
										numberEntry.Text = currentValueInt.ToString();
									}
									else
									{
										if (++currentValueDecimal > item.Max) currentValueDecimal = item.Max;
										numberEntry.Text = currentValueDecimal.ToString("0.###");
									}
								};
								numberDownButton.Clicked += (sender, e) =>
								{
									if (item.Type == TableItemType.Integer)
									{
										if (--currentValueInt < Convert.ToInt32(item.Min)) currentValueInt = Convert.ToInt32(item.Min);
										numberEntry.Text = currentValueInt.ToString();
									}
									else
									{
										if (--currentValueDecimal < item.Min) currentValueDecimal = item.Min;
										numberEntry.Text = currentValueDecimal.ToString("0.###");
									}
								};
								numberGrid.Add(numberEntry);
								numberGrid.Add(numberUpButton);
								numberGrid.SetColumn(numberUpButton, 1);
								numberGrid.Add(numberDownButton);
								numberGrid.SetColumn(numberDownButton, 2);
								itemGrid.Add(numberGrid);
								itemGrid.SetColumn(numberGrid, 1);
								break;
							case TableItemType.Picker:
								UnaccentedButton pickerBackground = new UnaccentedButton();
								BorderlessPicker picker = new BorderlessPicker();
								picker.Opacity = 0;
								pickerBackground.BindingContext = picker;
								pickerBackground.SetBinding(Button.TextProperty, static (Picker p) => p.SelectedItem);
								picker.HorizontalTextAlignment = TextAlignment.Center;
								
								if (item.Options != null)
									foreach (var option in item.Options)
									{
										picker.Items.Add(option);
									}
								picker.SelectedIndex = Convert.ToInt32(item.NumberValue);
								itemGrid.Add(pickerBackground);
								itemGrid.Add(picker);
								itemGrid.SetColumn(picker, 1);
								itemGrid.SetColumn(pickerBackground, 1);
								picker.VerticalOptions = LayoutOptions.Center;
								picker.HorizontalOptions = LayoutOptions.Fill;
								picker.SelectedIndexChanged += async (sender, e) => await commit(picker.SelectedIndex);
								break;
							case TableItemType.String:
								Entry entry = new Entry();
								entry.Text = item.StringValue ?? "";
								itemGrid.Add(entry);
								itemGrid.SetColumn(entry, 1);
								entry.VerticalOptions = LayoutOptions.Center;
								entry.HorizontalOptions = LayoutOptions.Fill;
								entry.Margin = new Thickness(0, 0, 2, 0);
								entry.TextChanged += async (sender, e) =>
								{
									string formattedText = entry.Text;
									byte[] utf8_char = Encoding.UTF8.GetBytes(formattedText);
									int cp = entry.CursorPosition;
									while (item.StringLength >= 0 && utf8_char.Length > item.StringLength && utf8_char.Length > 0)
									{
										if (cp == 0) cp = 1;
										formattedText = formattedText.Substring(0, cp) + formattedText.Substring(cp + 1, formattedText.Length - cp - 1);
										if (cp > 0)
											entry.CursorPosition = cp - 1;
										utf8_char = Encoding.UTF8.GetBytes(formattedText);
									}
									entry.CursorPosition = cp;
									if (entry.Text != formattedText) entry.Text = formattedText;
									await commit(formattedText);
								};
								break;
						}
						groupLayout.Add(itemGrid);
					}
				groupBorder.Content = groupLayout;
				layout.Add(groupBorder);
			}
		}

		public async Task DisconnectToDeviceForce(WkcDeviceInfo? deviceInfo, bool connectionLost = false)
		{
			if (!await RequestQueue()) return;
			if (deviceInfo == null || !AllowDisconnect)
			{
				ClearRequest();
				return;
			}
			var adapter = Plugin.BLE.CrossBluetoothLE.Current.Adapter;
			var physicalDevice = GetPhysicalDevice(deviceInfo);
			if (physicalDevice == null)
			{
				ConnectedDevice = null;
				ClearRequest();
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
			ClearRequest();
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
