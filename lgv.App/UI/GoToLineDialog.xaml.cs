using System.Windows;
using System.Windows.Input;

// Avoid WinForms ambiguity
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;

namespace lgv.UI;

public partial class GoToLineDialog : Window
{
    private readonly int _maxLine;
    public int LineNumber { get; private set; } = 1;

    public GoToLineDialog(int maxLine)
    {
        _maxLine = maxLine;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LineNumberBox.Focus();
            LineNumberBox.SelectAll();
        };
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(LineNumberBox.Text, out int n) && n >= 1 && n <= _maxLine)
        {
            LineNumber = n;
            DialogResult = true;
        }
        else
        {
            WpfMessageBox.Show($"Enter a number between 1 and {_maxLine}.",
                "Invalid Line Number", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LineNumberBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OkBtn_Click(sender, new RoutedEventArgs());
    }
}
