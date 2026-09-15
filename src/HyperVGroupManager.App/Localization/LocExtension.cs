using System.Windows.Data;
using System.Windows.Markup;

namespace HyperVGroupManager.App.Localization;

[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        }.ProvideValue(serviceProvider);
}
