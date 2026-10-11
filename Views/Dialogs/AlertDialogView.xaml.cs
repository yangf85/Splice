using System.Windows.Controls;

namespace Splice.Views.Dialogs;

public partial class AlertDialogView : UserControl
{
    public string TitleText { get; }
    public string MessageText { get; }

    public AlertDialogView(string title, string message)
    {
        TitleText = title;
        MessageText = message;
        InitializeComponent();
        DataContext = this;
    }
}
