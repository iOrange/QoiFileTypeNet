using PaintDotNet;

namespace QoiFileTypeNet {
    public sealed class QoiFileTypeFactory : IFileTypeFactory {
        public FileType[] GetFileTypeInstances() {
            return new FileType[] { new QoiFileType() };
        }
    }
}
