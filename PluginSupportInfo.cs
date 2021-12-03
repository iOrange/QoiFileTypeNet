using PaintDotNet;
using System;

namespace QoiFileTypeNet {
    public sealed class PluginSupportInfo : IPluginSupportInfo {
        public string DisplayName => "QOI FileType";

        public string Author => "iOrange";

        public string Copyright => "iOrange";

        public Version Version => typeof(PluginSupportInfo).Assembly.GetName().Version;

        public Uri WebsiteUri => new Uri(ConstantStrings.GitHubLinkValue);
    }
}
