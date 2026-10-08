using CommunityToolkit.Mvvm.ComponentModel;
using ModpackMerger.Core.Models;

namespace ModpackMerger.App.ViewModels;

/// <summary>Ligne de la liste des mods du pack public, avec sa case à cocher.</summary>
public partial class ModItemViewModel(ModEntry entry) : ObservableObject
{
    public ModEntry Entry { get; } = entry;

    [ObservableProperty]
    private bool _isSelected = entry.Required && entry.ClientSupported;

    public string DisplayName => Entry.DisplayName;
    public string FileName => Entry.FileName ?? "—";
    public string Version => Entry.VersionLabel ?? "";

    public string SourceLabel => Entry.Source switch
    {
        ModSource.CurseForge => "CurseForge",
        ModSource.Modrinth => "Modrinth",
        _ => "Inclus dans le pack",
    };

    public string Notes => string.Join(" · ", new[]
    {
        Entry.ClientSupported ? null : "Serveur uniquement",
        Entry.Required ? null : "Optionnel",
    }.Where(n => n is not null));

    /// <summary>À appeler après la résolution en ligne, qui modifie l'entrée sous-jacente.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(Version));
    }
}
