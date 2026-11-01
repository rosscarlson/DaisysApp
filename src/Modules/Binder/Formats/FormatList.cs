using DaisysApp.Applets.Binder.Formats.Fso;

namespace DaisysApp.Applets.Binder.Formats;

/// <summary>The formats a title's JSON can name (its "format").</summary>
public static class FormatList
{
    public static readonly IBindFormat[] All = [new FsoFormat(), new EliteFormat(), new AcFormat(), new SheetFormat(), new TemplateFormat()];

    public static IBindFormat? For(string id) => All.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
