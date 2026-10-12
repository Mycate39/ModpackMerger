using ModpackMerger.Core.Models;

namespace ModpackMerger.Core.Parsing;

public interface IModpackParser
{
    bool CanParse(IPackFileSystem pack);

    ModpackInfo Parse(IPackFileSystem pack, string sourcePath);
}
