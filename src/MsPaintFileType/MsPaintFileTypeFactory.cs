using PaintDotNet;

namespace MsPaintFileType;

public sealed class MsPaintFileTypeFactory : IFileTypeFactory
{
    public FileType[] GetFileTypeInstances() => [new MsPaintFileTypePlugin()];
}
