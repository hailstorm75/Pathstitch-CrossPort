using System;
using Domain.MVVM.Navigation;
using UI.Navigation;

namespace Pathstitch.App.Pages;

public partial class EditorPageView : BasePageView
{
    public EditorPageView(IServiceProvider serviceProvider) : base(serviceProvider, NavigationAddressBook.EditorPage)
    {
        InitializeComponent();
    }
}
