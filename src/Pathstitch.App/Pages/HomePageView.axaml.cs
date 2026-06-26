using System;
using Domain.MVVM.Navigation;
using UI.Navigation;

namespace Pathstitch.App.Pages;

public partial class HomePageView : BasePageView
{
    public HomePageView(IServiceProvider serviceProvider) : base(serviceProvider, NavigationAddressBook.HomePage)
    {
        InitializeComponent();
    }
}