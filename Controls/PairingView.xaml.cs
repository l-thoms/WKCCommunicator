using CommunityToolkit.Maui.Core.Platform;
using CommunityToolkit.Maui.Extensions;
using System.Diagnostics;

namespace WkcCommunicator.Controls;

public partial class PairingView : ContentView
{
	private PairingView()
	{
		InitializeComponent();
	}

	public PairingView(Page parent)
	{
		InitializeComponent();
		ParentPage = parent;
	}

	public int Key{ get => Convert.ToInt32(KeyEntry.Text); }
	public bool Confirmed { get; private set; } = false;
	private Page? ParentPage{ get; set; }

	private string? FormatNumber(string? number)
	{
		string result = "";
		if (number == null) return number;
		for (int i = 0; i < number.Length; i++)
			if (char.IsAsciiDigit(number[i]))
				result += number[i];
		return result;
	}

	private async Task Close()
	{
		if(ParentPage != null)
			await ParentPage.ClosePopupAsync();
	}

	private async void Entry_TextChanged(object sender, TextChangedEventArgs e)
	{
		var currentEntry = sender as Entry;
		if (currentEntry == null) return;
		string? formatted = FormatNumber(e.NewTextValue);
		if (e.NewTextValue != formatted) KeyEntry.Text = formatted;
		else if (e.NewTextValue.Length == 6)
		{
			Confirmed = true;
			currentEntry.IsEnabled = false;
			await Task.Delay(50);
			await Close();
		}
	}

	private async void UnaccentedButton_Clicked(object sender, EventArgs e)
	{
		await Close();
	}

	private async void ContentView_Loaded(object sender, EventArgs e)
	{
		KeyEntry.Focus();
		await KeyEntry.ShowKeyboardAsync();
	}
}