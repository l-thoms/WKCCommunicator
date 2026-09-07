using WkcCommunicator.Controls;

namespace WkcCommunicator
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
        }

		private async void ToolbarItem_Clicked(object sender, EventArgs e)
		{
            await AppInfoView.ShowAppInfo(this.CurrentPage);
        }
    }
}
