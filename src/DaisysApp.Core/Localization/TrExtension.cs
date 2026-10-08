using System.Windows.Markup;

namespace DaisysApp;

/// <summary>
/// Translated text in XAML: <c>Text="{l:Tr 'Run now'}"</c> (with <c>xmlns:l="clr-namespace:DaisysApp"</c>). The English
/// is the key, as with <see cref="Loc.T"/>; spaces around it are kept, for text next to a <c>&lt;Run&gt;</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Any(Text);
}
