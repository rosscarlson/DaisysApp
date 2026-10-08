using DaisysApp.Settings;

namespace DaisysApp.Applets.Template;

/// <summary>Saved in %APPDATA%\DaisysApp\Template.json. Anything with a public getter and setter is saved.</summary>
public sealed class TemplateSettings
{
    private const string FileName = "Template";

    /// <summary>Who to say hello to.</summary>
    public string Name { get; set; } = "world";

    /// <summary>How many times the button has been clicked, ever.</summary>
    public int Clicks { get; set; }

    public static TemplateSettings Load() => JsonStore.Load<TemplateSettings>(FileName);
    public void Save() => JsonStore.Save(FileName, this);
}
