using CommunityToolkit.Mvvm.ComponentModel;
using ModpackMerger.Core.Injection;

namespace ModpackMerger.App.ViewModels;

/// <summary>Ligne du plan d'injection : mod entrant, éventuel doublon existant, action choisie.</summary>
public partial class PlanItemViewModel(PlanItem item) : ObservableObject
{
    public PlanItem Item { get; } = item;

    public string Name => Item.Incoming.Entry.DisplayName;
    public string IncomingFile => Item.Incoming.FileName;
    public string IncomingVersion => Item.Incoming.Metadata.Version ?? "?";
    /// <summary>Tous les fichiers existants reconnus comme ce mod (ils seront tous remplacés).</summary>
    public string ExistingFile => string.Join("\n", Item.Existing.Select(e => e.IsDisabled ? $"{e.FileName} (désactivé)" : e.FileName));
    public string ExistingVersion => Item.PrimaryExisting is null ? "" : Item.PrimaryExisting.Metadata.Version ?? "?";
    public ConflictKind Kind => Item.Kind;
    public bool IsConflict => Item.IsConflict;
    public bool CanChangeAction => Item.Kind != ConflictKind.Identical;
    public IReadOnlyList<ConflictAction> AvailableActions => Item.AvailableActions;

    public ConflictAction Action
    {
        get => Item.Action;
        set
        {
            if (Item.Action == value) return;
            Item.Action = value;
            OnPropertyChanged();
        }
    }
}
