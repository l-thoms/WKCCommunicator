using CommunityToolkit.Maui.Core.Platform;
using CommunityToolkit.Maui.Extensions;
using System.Diagnostics;

namespace WkcCommunicator.Controls;

public partial class DeleteView : ContentView
{
	private DeleteView()
	{
		InitializeComponent();
	}

	public DeleteView(Page parent)
	{
		InitializeComponent();
		ParentPage = parent;
	}

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

	private async void CancelButton_Clicked(object sender, EventArgs e)
	{
		await Close();
	}

	private async void DeleteButton_Clicked(object sender, EventArgs e)
	{
		Confirmed = true;
		await Close();
	}
}