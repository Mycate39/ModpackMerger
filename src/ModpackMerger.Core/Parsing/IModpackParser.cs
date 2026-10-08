using System.IO.Compression;
using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Parsing;

public interface IModpackParser
{
    bool CanParse(ZipArchive archive);

    ModpackInfo Parse(ZipArchive archive, string sourcePath);
}
