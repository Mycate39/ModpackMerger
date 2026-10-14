using System.Globalization;
using Avalonia.Data.Converters;
using ModpackMerger.Core.Injection;

namespace ModpackMerger.Mac.Converters;

/// <summary>Libellés français des énumérations du plan d'injection.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public static readonly EnumLabelConverter Instance = new();

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

/// <summary>Vrai si la valeur (page, gravité de notification…) vaut le paramètre, comparés en texte.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
