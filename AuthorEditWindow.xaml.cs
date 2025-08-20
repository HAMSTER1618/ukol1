using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ukol1
{
    public partial class AuthorEditWindow : Window
    {
        public AuthorEditWindow(string? lastName = null, string? firstName = null)
        {
            InitializeComponent();
            LastNameBox.Text = lastName ?? "";
            FirstNameBox.Text = firstName ?? "";
            Loaded += (_, __) => { LastNameBox.Focus(); LastNameBox.CaretIndex = LastNameBox.Text.Length; };
        }

        public string FirstName => FirstNameBox.Text.Trim();
        public string LastName => LastNameBox.Text.Trim();

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
            if (string.IsNullOrWhiteSpace(LastName))
            {
                MessageBox.Show("Pole „Příjmení“ je povinné.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                LastNameBox.Focus();
                return;
            }
            DialogResult = true;
        }
    }
}