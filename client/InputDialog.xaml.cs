using System.Windows;

namespace client
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = string.Empty;

        public InputDialog(string promptText, string defaultText = "")
        {
            InitializeComponent();
            PromptLabel.Text = promptText;
            InputTextBox.Text = defaultText;
            InputTextBox.Focus();
            if (!string.IsNullOrEmpty(defaultText))
            {
                InputTextBox.SelectAll();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
