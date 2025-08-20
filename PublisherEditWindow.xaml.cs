using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ukol1
{
    public partial class PublisherEditWindow : Window
    {
        public PublisherEditWindow(string? name = null, string? city = null)
        {
            InitializeComponent();
            NameBox.Text = name ?? "";
            CityBox.Text = city ?? "";
            Loaded += (_, __) => { NameBox.Focus(); NameBox.CaretIndex = NameBox.Text.Length; };
        }

        public string City => CityBox.Text.Trim();
        public string PublisherName => NameBox.Text.Trim();

        private void MoveCaretToEnd(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.CaretIndex = tb.Text?.Length ?? 0;
                tb.SelectionLength = 0;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PublisherName))
            {
                MessageBox.Show("Pole „Název“ je povinné.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus();
                return;
            }
            DialogResult = true;
        }
    }
}