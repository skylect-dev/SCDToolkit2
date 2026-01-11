using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCDToolkit.Desktop.Views
{
    public partial class PlaceholderWindow : Window
    {
        public PlaceholderWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
