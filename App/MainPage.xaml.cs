namespace App;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        WebViewConsoleBridge.Attach(blazorWebView);
    }
}