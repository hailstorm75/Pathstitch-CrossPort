namespace Domain.MVVM.Navigation;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NavigationPageAttribute : Attribute
{
	public string Identifier { get; set; }

	public NavigationPageAttribute(string identifier)
	{
		if (string.IsNullOrEmpty(identifier))
			throw new ArgumentNullException(nameof(identifier));

		Identifier = identifier;
	}
}