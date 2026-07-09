namespace WkcCommunicator.Controls;

public partial class DeviceSeparator : ContentView
{
	public static readonly BindableProperty DeviceTextProperty =
		BindableProperty.Create(nameof(Text), typeof(string), typeof(DeviceSeparator), null);
	public string Text
	{
		get => (string)GetValue(DeviceTextProperty);
		set => SetValue(DeviceTextProperty, value);
	}
	public DeviceSeparator()
	{
		InitializeComponent();
	}
}