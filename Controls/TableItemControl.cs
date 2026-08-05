using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WkcCommunicator.Connections;
using WkcCommunicator.Types;

namespace WkcCommunicator.Controls
{
	internal class TableItemControl : ContentView
	{
		AdapterManager? Manager{ get; set; }
		TableItem? Item{ get; set; }
		public TableGroupType GroupType{ get; private set; }
		public string? Name { get => Item == null ? null : Item.Name; }
		IView? CurrentControl{ get; set; }
		bool FreezeUpdate{ get; set; }
		bool OnTransmit { get; set; }
		int IntValue { get; set; }
		double DoubleValue{ get; set; }
		ConcurrentQueue<JsonNode?> TransmissionQueue { get; set; } = new ConcurrentQueue<JsonNode?>();

		byte[]? GenerateCommit(JsonNode? data)
		{
			if (Item == null || Item.Name == null) return null;
			JsonObject commandObject = new JsonObject();
			commandObject.Add(Item.Name, data);
			List<byte> optionCommand = new List<byte>();
			optionCommand.Add(Convert.ToByte(GroupType == TableGroupType.Shortcut ? CommandType.WriteShortcut : CommandType.WriteSettings));
			optionCommand.AddRange(Encoding.UTF8.GetBytes(commandObject.ToJsonString()));
			return optionCommand.ToArray();
		}

		async Task Commit(JsonNode? data)
		{
			if (Item == null || Item.Name == null || Manager == null || FreezeUpdate) return;
			bool lastOnTransmit = OnTransmit;
			OnTransmit = true;
			if (lastOnTransmit)
			{
				TransmissionQueue.Enqueue(data);
				return;
			}
			await Manager.RequestQueue();
			byte[]? generated;
			generated = GenerateCommit(data);
			if (generated != null)
				await Manager.SendCustomCommandAsync(generated);
			while (TransmissionQueue.Count != 0)
			{
				int tCount = TransmissionQueue.Count;
				JsonNode? outNode = null;
				for (int i = 0; i < tCount; i++)
					TransmissionQueue.TryDequeue(out outNode);
				generated = GenerateCommit(outNode);
				if (generated != null)
					await Manager.SendCustomCommandAsync(generated);
			}
			Manager.ReleaseQueue();
			OnTransmit = false;
		}

		void UpdateActions()
		{
			var actionStackLayout = CurrentControl as HorizontalStackLayout;
			if (Item == null || Item.Options == null || actionStackLayout == null) return;
			foreach (var child in actionStackLayout.Children) child.DisconnectHandlers();
			actionStackLayout.Clear();
			for (int i = 0; i < Item.Options.Length; i++)
			{
				Controls.UnaccentedButton optionButton = new UnaccentedButton()
				{
					Text = Item.Options[i],
					MinimumWidthRequest = 60
				};
				int optionIndex = i;
				optionButton.Clicked += async (sender, e) => await Commit(optionIndex);
				actionStackLayout.Add(optionButton);
			}
		}

		public TableItemControl(AdapterManager? manager, TableItem? item, TableGroupType type)
		{
			if (item == null || item.Name == null || manager == null) return;
			this.Manager = manager;
			this.Item = item;
			this.GroupType = type;
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
					CurrentControl = actionStackLayout;
					if (item.Options != null)
						UpdateActions();
					break;
				case TableItemType.Switch:
					Microsoft.Maui.Controls.Switch commandSwitch = new Microsoft.Maui.Controls.Switch();
					commandSwitch.IsToggled = item.BoolValue;
					CurrentControl = commandSwitch;
					itemGrid.Add(commandSwitch);
					itemGrid.SetColumn(commandSwitch, 1);
					commandSwitch.HorizontalOptions = LayoutOptions.End;
					commandSwitch.Toggled += async (sender, e) => await Commit(commandSwitch.IsToggled);
					break;
				case TableItemType.Integer:
				case TableItemType.Decimal:
					Grid numberGrid = new Grid();
					numberGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
					numberGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
					numberGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
					numberGrid.ColumnSpacing = 6;
					Entry numberEntry = new Entry();
					UnaccentedButton numberUpButton = new UnaccentedButton() { Text = "+" };
					UnaccentedButton numberDownButton = new UnaccentedButton() { Text = "-" };
					numberEntry.Keyboard = Keyboard.Numeric;
					DoubleValue = item.NumberValue;
					IntValue = Convert.ToInt32(item.NumberValue);
					numberEntry.Text = item.Type == TableItemType.Decimal ? DoubleValue.ToString() : IntValue.ToString();
					numberUpButton.WidthRequest = 24;
					numberDownButton.WidthRequest = 24;
					numberEntry.VerticalOptions = LayoutOptions.Center;
					numberUpButton.VerticalOptions = LayoutOptions.Center;
					numberDownButton.VerticalOptions = LayoutOptions.Center;
					bool isValueValid = true;
					CurrentControl = numberEntry;
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
								IntValue = Convert.ToInt32(formattedText);
								if (IntValue < Convert.ToInt32(item.Min)) IntValue = Convert.ToInt32(item.Min);
								else if (IntValue > Convert.ToInt32(item.Max)) IntValue = Convert.ToInt32(item.Max);
								else exceed = false;
								if (exceed)
									formattedText = IntValue.ToString();
							}
							else
							{
								DoubleValue = Convert.ToDouble(formattedText);
								if (DoubleValue < item.Min) DoubleValue = item.Min;
								else if (DoubleValue > item.Max) DoubleValue = item.Max;
								else exceed = false;
								if (exceed)
									formattedText = DoubleValue.ToString("0.###");
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
							await Commit(item.Type == TableItemType.Integer ? IntValue : DoubleValue);
					};
					numberUpButton.Clicked += (sender, e) =>
					{
						if (item.Type == TableItemType.Integer)
						{
							if (++IntValue > Convert.ToInt32(item.Max)) IntValue = Convert.ToInt32(item.Max);
							numberEntry.Text = IntValue.ToString();
						}
						else
						{
							if (++DoubleValue > item.Max) DoubleValue = item.Max;
							numberEntry.Text = DoubleValue.ToString("0.###");
						}
					};
					numberDownButton.Clicked += (sender, e) =>
					{
						if (item.Type == TableItemType.Integer)
						{
							if (--IntValue < Convert.ToInt32(item.Min)) IntValue = Convert.ToInt32(item.Min);
							numberEntry.Text = IntValue.ToString();
						}
						else
						{
							if (--DoubleValue < item.Min) DoubleValue = item.Min;
							numberEntry.Text = DoubleValue.ToString("0.###");
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
					CurrentControl = picker;
					picker.SelectedIndexChanged += async (sender, e) => await Commit(picker.SelectedIndex);
					break;
				case TableItemType.String:
					Entry entry = new Entry();
					entry.Text = item.StringValue ?? "";
					itemGrid.Add(entry);
					itemGrid.SetColumn(entry, 1);
					entry.VerticalOptions = LayoutOptions.Center;
					entry.HorizontalOptions = LayoutOptions.Fill;
					entry.Margin = new Thickness(0, 0, 2, 0);
					CurrentControl = entry;
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
						await Commit(formattedText);
					};
					break;
			}
			this.Content = itemGrid;
		}

		void ParseItem(string fetch)
		{
			if (Item == null || Item.Name == null || CurrentControl == null) return;
			try
			{
				JsonNode? fetchNode = JsonNode.Parse(fetch);
				if (fetchNode == null) return;
				JsonNode? nameNode = fetchNode["name"];
				if (nameNode == null || nameNode.GetValueKind() != JsonValueKind.String || nameNode.GetValue<string>() != Item.Name) return;
				JsonNode? optionsNode = fetchNode["options"];
				JsonNode? valueNode = fetchNode["value"];
				switch (Item.Type)
				{
					case TableItemType.Action:
						var currentLayout = CurrentControl as StackLayout;
						if (currentLayout != null && optionsNode != null && optionsNode.GetValueKind() == JsonValueKind.Array)
						{
							List<string> optionsList = new List<string>();
							var optionsArray = optionsNode.AsArray();
							foreach (var arrayItem in optionsArray)
							{
								string arrayName;
								if (arrayItem == null || arrayItem.GetValueKind() != JsonValueKind.String)
									arrayName = AppResources.Table_UnnamedOption;
								else
									arrayName = arrayItem.GetValue<string>();
								optionsList.Add(arrayName);
							}
							Item.Options = optionsList.ToArray();
							UpdateActions();
						}
						break;
					case TableItemType.Switch:
						var currentSwitch = CurrentControl as Microsoft.Maui.Controls.Switch;
						if (currentSwitch != null && valueNode != null && (
							valueNode.GetValueKind() == JsonValueKind.True || valueNode.GetValueKind() == JsonValueKind.False))
						{
							Item.BoolValue = valueNode.GetValue<bool>();
							currentSwitch.Dispatcher.DispatchAsync(() => currentSwitch.IsToggled = Item.BoolValue).Wait();
						}
						break;
					case TableItemType.Integer:
					case TableItemType.Decimal:
						var currentNumberEntry = CurrentControl as Entry;
						if (currentNumberEntry != null && valueNode != null && valueNode.GetValueKind() == JsonValueKind.Number)
						{
							Item.NumberValue = valueNode.GetValue<double>();
							DoubleValue = Item.NumberValue;
							IntValue = Convert.ToInt32(Item.NumberValue);
							currentNumberEntry.Dispatcher.DispatchAsync(() =>
							{
								if (Item.Type == TableItemType.Integer)
									currentNumberEntry.Text = IntValue.ToString();
								else
									currentNumberEntry.Text = DoubleValue.ToString("0.###");
							}).Wait();
						}
						break;
					case TableItemType.Picker:
						var currentPicker = CurrentControl as Picker;
						if (currentPicker != null && valueNode != null && valueNode.GetValueKind() == JsonValueKind.Number)
						{
							Item.NumberValue = valueNode.GetValue<double>();
							currentPicker.Dispatcher.DispatchAsync(() => currentPicker.SelectedIndex = Convert.ToInt32(Item.NumberValue)).Wait();
						}
						break;
					case TableItemType.String:
						var currentStringEntry = CurrentControl as Entry;
						if (currentStringEntry != null && valueNode != null && valueNode.GetValueKind() == JsonValueKind.String)
						{
							Item.StringValue = valueNode.GetValue<string>();
							currentStringEntry.Dispatcher.DispatchAsync(() => currentStringEntry.Text = Item.StringValue).Wait();
						}
						break;
				}
			}
			catch (Exception ex){ Debug.WriteLine(ex.Message); }
		}

		public async Task UpdateAsync()
		{
			if (Item == null || Item.Name == null || Manager == null || Manager.ConnectedDevice == null) return;
			if (!await Manager.RequestQueue()) return;
			FreezeUpdate = true;
			List<byte> commandList = [(byte)(GroupType == TableGroupType.Shortcut ? CommandType.ReadShortcutItem : CommandType.ReadSettingsItem)];
			byte[] nameByte = Encoding.UTF8.GetBytes(Item.Name);
			commandList.AddRange(nameByte);
			byte[]? result = await Manager.SendCustomCommandAsync(commandList.ToArray());
			if (result != null && result.Length >= 1 && result[0] == 0)
			{
				byte[]? fetchResult = await Manager.GetCommandOutputAsync();
				
				Manager.ReleaseQueue();
				if (fetchResult != null)
				{
					string fetchString = Encoding.UTF8.GetString(fetchResult);
					ParseItem(fetchString);
				}
			}
			else
				Manager.ReleaseQueue();
			FreezeUpdate = false;
		}
	}
}