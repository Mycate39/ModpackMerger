# Modpack Merger

Application Windows (WPF, .NET 10) qui importe un modpack public (CurseForge `.zip` ou Modrinth `.mrpack`), liste ses mods, permet d'en exclure certains, puis les injecte dans le dossier `mods` d'un modpack personnel en gérant les doublons.

## Lancer

```powershell
dotnet run --project src/ModpackMerger.App            # application
dotnet run --project src/ModpackMerger.App -- pack.mrpack     # ouvre directement un pack
dotnet test                                            # tests du cœur métier
dotnet publish src/ModpackMerger.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Utilisation

1. **Importer** un `.zip` / `.mrpack` (bouton ou glisser-déposer). Les noms des mods sont résolus en ligne.
2. Choisir le **dossier cible** (le dossier de l'instance ou directement son sous-dossier `mods`).
3. Décocher les mods à exclure, puis **Analyser** : téléchargement dans un cache local + comparaison avec le dossier cible.
4. Dans l'onglet *Doublons*, choisir pour chaque conflit *Remplacer*, *Ignorer* ou *Garder les deux*, puis **Injecter**.

Les fichiers remplacés ne sont jamais supprimés : ils sont déplacés dans `…/mods-backup/<horodatage>/`, à côté du dossier `mods`.

## Architecture

```
src/
  ModpackMerger.Core/        Bibliothèque sans dépendance UI (testable)
    Models/                  ModpackInfo, ModEntry, JarMetadata
    Parsing/                 ModpackReader → CurseForgeParser | ModrinthParser
    Resolution/              CurseForgeResolver, ModrinthResolver (noms, URLs, hashes)
    Download/                ModDownloader (cache %LocalAppData%\ModpackMerger\cache, vérif. SHA-1/512)
    Metadata/                JarMetadataReader (fabric.mod.json, quilt.mod.json, mods.toml, neoforge.mods.toml, mcmod.info)
    Injection/               ConflictAnalyzer, VersionComparer, ModInjector
  ModpackMerger.App/         WPF + MVVM (CommunityToolkit.Mvvm), thème Fluent natif
    ViewModels/  Views/  Services/  Converters/
tests/
  ModpackMerger.Core.Tests/  xUnit, archives et jars synthétiques
```

Flux : `ModpackReader.Read` → `ModpackResolver.ResolveAsync` → sélection utilisateur → `ModDownloader.DownloadAsync` → `ConflictAnalyzer.Analyze` → arbitrage → `ModInjector.InjectAsync`.

### Détection des doublons

Le nom de fichier change souvent d'une version à l'autre (`JEI_Forge_v15.2.jar` → `jei-1.20.1-forge-15.20.0.105.jar`), il n'est donc pas le critère principal. Un mod entrant est rapproché des jars existants, dans cet ordre :

1. SHA-1 identique → déjà présent ;
2. identifiant commun lu dans le jar : `modId` principal, autres `[[mods]]` d'un mods.toml, `provides` Fabric/Quilt ;
3. même nom de fichier ;
4. même nom normalisé, sans versions, loader ni préfixes `[1.20.1]` (uniquement si un des jars n'a pas de métadonnées).

**Tous** les fichiers correspondants sont remplacés ensemble (ex. deux anciennes versions présentes par erreur). Un mod `.jar.disabled` reste désactivé après mise à jour. Le remplacement copie d'abord la nouvelle version, puis déplace les anciennes en sauvegarde, avec retour arrière en cas d'erreur.

Les versions sont comparées par `VersionComparer` (`1.2.10 > 1.2.9`, `1.0 > 1.0-beta`, métadonnées `+build`). Par défaut : plus récent → *Remplacer* ; plus ancien, identique ou incertain → *Ignorer*. Une correspondance via `provides` (mod alternatif) est toujours marquée « version différente », à arbitrer.

### CurseForge et clé API

Le `manifest.json` CurseForge ne contient que des identifiants (projectID/fileID).

- **Sans clé** : l'endpoint public `curseforge.com/api/v1/mods/{p}/files/{f}/download` redirige vers le CDN ; le nom du jar est lu dans la redirection, puis le vrai nom du mod dans le jar une fois téléchargé.
- **Avec clé** (gratuite sur <https://console.curseforge.com>, à saisir dans *Réglages*) : noms exacts dès l'import via l'API officielle.

Modrinth ne nécessite aucune clé.

### Réglages

`%AppData%\ModpackMerger\settings.json` : clé API CurseForge, dernier dossier cible.
