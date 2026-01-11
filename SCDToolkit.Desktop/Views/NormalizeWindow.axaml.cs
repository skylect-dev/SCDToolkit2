using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SCDToolkit.Desktop.Views
{
    public partial class NormalizeWindow : Window
    {
        public NormalizeWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
