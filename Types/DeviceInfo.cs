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
		public string? Key { get; set; }
	}

	public struct WkcDeviceInfoRaw
	{
		public string? owner;
		public string? character;
		public string? manufacturer;
	}

	public struct WkcSettingsRaw
	{
		public int advertise_duration;
		public bool display_rotated;
		public int brightness;
		public int power_save;
	}
}
