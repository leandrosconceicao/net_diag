using System.Windows;
using NetDiag.App.ViewModels;

namespace NetDiag.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
