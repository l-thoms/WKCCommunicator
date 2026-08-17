using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plugin.BLE;

namespace WkcCommunicator.Types
{
	public enum CommandType
	{
		None,
		KeyCode,
		TimeSync,
		ReadShortcutTable,
		ReadShortcutItem,
		WriteShortcut,
		ReadSettingsTable,
		ReadSettingsItem,
		WriteSettings
	}

	public enum TableItemType
	{
		Action,
		Switch,
		Integer,
		Decimal,
		Picker,
		String,
		Unknown
	}

	public class WkcDeviceInfo
	{
		public string? Name { get; set; }
		public byte[]? Address { get; set; }
		public string? Owner { get; set; }
		public string? Character { get; set; }
		public string? Manufacturer { get; set; }
		public byte[]? ProtocolVersion { get; set; }
		public string? Model { get; set; }
		public int Signal { get; set; } = 0;
		public byte[]? Key { get; set; }
	}

	public class TableItem
	{
		public TableItemType Type { get; set; }
		public string? Name { get; set; }
		public string? DisplayName { get; set; }
		public double Min { get; set; }
		public double Max { get; set; }
		public double NumberValue { get; set; }
		public string[]? Options { get; set; }
		public int StringLength { get; set; }
		public string? StringValue { get; set; }
		public bool BoolValue { get; set; }
	}

	public class TableGroup
	{
		public string? Name { get; set; }
		public TableItem[]? Items { get; set; }
	}

	public enum TableGroupType
	{
		Shortcut,
		Settings
	}
}
