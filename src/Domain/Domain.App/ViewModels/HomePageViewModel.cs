using System;
using System.Collections.Generic;
using System.Text;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

[NavigationPage(NavigationAddressBook.HomePage)]
public sealed partial class HomePageViewModel(ILogger<HomePageViewModel> logger) : BasePageViewModel(logger)
{
	public override bool IsLoading
	{
		get;
		set => SetProperty(ref field, value);
	}
}
