using System.Globalization;
using System.Windows.Data;
using ModpackMerger.Core.Injection;

namespace ModpackMerger.App.Converters;

/// <summary>Libellés français des énumérations du plan d'injection.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ConflictKind.New => "Nouveau",
        ConflictKind.Identical => "Déjà présent (identique)",
        ConflictKind.IncomingNewer => "Version plus récente",
        ConflictKind.IncomingOlder => "Version plus ancienne",
        ConflictKind.SameVersionDifferentFile => "Même version, fichier différent",
        ConflictKind.Different => "Version différente",
        ConflictAction.Install => "Installer",
        ConflictAction.Replace => "Remplacer",
        ConflictAction.Skip => "Ignorer",
        ConflictAction.KeepBoth => "Garder les deux",
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
