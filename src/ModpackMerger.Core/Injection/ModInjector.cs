namespace ModpackMerger.Core.Injection;

public sealed record InjectionProgress(int Completed, int Total, string Message);

public sealed record InjectionReport(int Installed, int Replaced, int Skipped, IReadOnlyList<string> Errors, string? BackupDirectory);

/// <summary>
/// Applique un plan : copie les nouveaux jars, remplace (avec sauvegarde) ou conserve les existants.
/// Les fichiers remplacés sont déplacés dans "{dossier parent}/mods-backup/{horodatage}" et jamais supprimés.
/// </summary>
public static class ModInjector
{
    private const string TempSuffix = ".modpackmerger-tmp";

    public static Task<InjectionReport> InjectAsync(
        IReadOnlyList<PlanItem> plan,
        string modsDirectory,
        IProgress<InjectionProgress>? progress,
        CancellationToken ct) => Task.Run(() => Inject(plan, modsDirectory, progress, ct), ct);

    private static InjectionReport Inject(IReadOnlyList<PlanItem> plan, string modsDirectory, IProgress<InjectionProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(modsDirectory);
        var backupDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modsDirectory).TrimEnd('\\', '/')) ?? modsDirectory,
            "mods-backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        int installed = 0, replaced = 0, skipped = 0, done = 0;
        var errors = new List<string>();

        foreach (var item in plan)
        {
            ct.ThrowIfCancellationRequested();
            var name = item.Incoming.Entry.DisplayName;
            string message;

            try
            {
                switch (item.Action)
                {
                    case ConflictAction.Replace when item.Existing.Count > 0:
                        message = Replace(item, modsDirectory, backupDir);
                        replaced++;
                        break;

                    case ConflictAction.Install:
                    case ConflictAction.KeepBoth:
                    case ConflictAction.Replace: // Replace sans fichier existant = simple ajout
                        var target = UniquePath(modsDirectory, item.Incoming.FileName);
                        File.Copy(item.Incoming.LocalPath, target, overwrite: false);
                        installed++;
                        message = item.Action == ConflictAction.KeepBoth
                            ? $"Ajouté (en plus de l'existant) : {Path.GetFileName(target)}"
                            : $"Ajouté : {Path.GetFileName(target)}";
                        break;

                    default:
                        skipped++;
                        message = $"Ignoré : {name}";
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{name} : {ex.Message}");
                message = $"ERREUR {name} : {ex.Message}";
            }

            progress?.Report(new InjectionProgress(++done, plan.Count, message));
        }

        return new InjectionReport(installed, replaced, skipped, errors, Directory.Exists(backupDir) ? backupDir : null);
    }

    /// <summary>
    /// Remplace TOUTES les versions existantes du mod par la nouvelle, même si les noms de fichiers diffèrent.
    /// Ordre sûr : copie de la nouvelle version sous un nom temporaire, déplacement des anciennes en sauvegarde,
    /// puis renommage final. En cas d'échec, les anciennes versions sont remises en place.
    /// </summary>
    private static string Replace(PlanItem item, string modsDirectory, string backupDir)
    {
        var existing = item.Existing.Where(e => File.Exists(e.Path)).ToList();
        // Un mod désactivé dans le launcher le reste après mise à jour.
        var keepDisabled = existing.Count > 0 && existing.All(e => e.IsDisabled);

        var temp = Path.Combine(modsDirectory, item.Incoming.FileName + TempSuffix);
        File.Copy(item.Incoming.LocalPath, temp, overwrite: true);

        var moved = new List<(string From, string To)>();
        try
        {
            Directory.CreateDirectory(backupDir);
            foreach (var old in existing)
            {
                var backup = UniquePath(backupDir, old.FileName);
                File.Move(old.Path, backup);
                moved.Add((old.Path, backup));
            }

            var finalName = item.Incoming.FileName + (keepDisabled ? ".disabled" : "");
            var target = UniquePath(modsDirectory, finalName);
            File.Move(temp, target);

            var oldNames = string.Join(", ", existing.Select(e => e.FileName));
            return $"Remplacé : {oldNames} → {Path.GetFileName(target)}" + (keepDisabled ? " (reste désactivé)" : "");
        }
        catch
        {
            foreach (var (from, to) in moved)
            {
                try { File.Move(to, from); } catch (IOException) { /* reste disponible dans la sauvegarde */ }
            }
            try { File.Delete(temp); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>"x.jar" puis "x (2).jar", "x (3).jar"... si le nom est déjà pris (actif ou désactivé).</summary>
    private static string UniquePath(string directory, string fileName)
    {
        var disabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var baseName = disabled ? fileName[..^".disabled".Length] : fileName;
        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName) + (disabled ? ".disabled" : "");

        bool Taken(string candidate)
        {
            var jar = disabled ? candidate[..^".disabled".Length] : candidate;
            return File.Exists(jar) || File.Exists(jar + ".disabled");
        }

        var path = Path.Combine(directory, fileName);
        for (var i = 2; Taken(path); i++)
            path = Path.Combine(directory, $"{stem} ({i}){ext}");
        return path;
    }
}
