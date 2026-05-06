using System.Reflection;
using PaintDotNet;

namespace MsPaintFileType;

public sealed class PluginSupportInfo : IPluginSupportInfo
{
    public string DisplayName => "MS Paint Project (.paint)";
    public string Author => "Brady Moritz";
    public string Copyright => "Copyright © 2026 Brady Moritz";
    public Version Version => typeof(PluginSupportInfo).Assembly.GetName().Version ?? new Version(0, 1);
    public Uri WebsiteUri => new("https://github.com/setiri/mspaintlib");
}
