using PaintDotNet;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using System;
using System.Collections.Generic;
using System.IO;

namespace QoiFileTypeNet {
    public enum BitDepth {
        AutoDetect,
        Depth32Bit,
        Depth24Bit
    }

    [PluginSupportInfo(typeof(PluginSupportInfo))]
    public sealed class QoiFileType : PropertyBasedFileType {
        public QoiFileType() :
            base("Quite OK Image (QOI)", new FileTypeOptions() {
                LoadExtensions = new string[] { ".qoi" },
                SaveExtensions = new string[] { ".qoi" },
                SupportsCancellation = true,
                SupportsLayers = false
            })
        {
        }

        public override PropertyCollection OnCreateSavePropertyCollection() {
            List<Property> props = new List<Property>
            {
                StaticListChoiceProperty.CreateForEnum(ConstantStrings.BitDepthString, BitDepth.AutoDetect),
                new UriProperty(ConstantStrings.GitHubLinkString, new Uri(ConstantStrings.GitHubLinkValue))
            };

            return new PropertyCollection(props);
        }

        public override ControlInfo OnCreateSaveConfigUI(PropertyCollection props) {
            ControlInfo info = CreateDefaultSaveConfigUI(props);

            PropertyControlInfo bitDepthPCI = info.FindControlForPropertyName(ConstantStrings.BitDepthString);
            bitDepthPCI.SetValueDisplayName(BitDepth.AutoDetect, "Auto Detect");
            bitDepthPCI.SetValueDisplayName(BitDepth.Depth32Bit, "32 Bits");
            bitDepthPCI.SetValueDisplayName(BitDepth.Depth24Bit, "24 Bits");

            PropertyControlInfo githubLinkPCI = info.FindControlForPropertyName(ConstantStrings.GitHubLinkString);
            githubLinkPCI.ControlProperties[ControlInfoPropertyNames.DisplayName].Value = "Author info";
            githubLinkPCI.ControlProperties[ControlInfoPropertyNames.Description].Value = "GitHub";

            return info;
        }

        protected override Document OnLoad(Stream input) {
            Document document = null;

            QoiDesc desc = new QoiDesc();
            QoiRgba[] pixels = QoiFile.Load(input, ref desc);
            if (pixels.Length > 0) {
                document = new Document(desc.width, desc.height);

                BitmapLayer layer = Layer.CreateBackgroundLayer(desc.width, desc.height);

                Surface surface = layer.Surface;

                unsafe {
                    int offset = 0;
                    for (int y = 0; y < surface.Height; ++y) {
                        ColorBgra* dst = surface.GetRowPointerUnchecked(y);

                        for (int x = 0; x < surface.Width; ++x, ++offset) {
                            dst->R = pixels[offset].r;
                            dst->G = pixels[offset].g;
                            dst->B = pixels[offset].b;
                            dst->A = pixels[offset].a;

                            ++dst;
                        }
                    }
                }

                document.Layers.Add(layer);
            }

            return document;
        }

        protected override void OnSaveT(Document input, Stream output, PropertyBasedSaveConfigToken token, Surface scratchSurface, ProgressEventHandler progressCallback) {
            scratchSurface.Clear();
            input.CreateRenderer().Render(scratchSurface);

            int srcWidth = scratchSurface.Width;
            int srcHeight = scratchSurface.Height;
            int numPixels = srcWidth * srcHeight;

            BitDepth bitDepth = (BitDepth)token.GetProperty(ConstantStrings.BitDepthString).Value;
            bool writeAlpha = (bitDepth == BitDepth.AutoDetect) ?
                              this.DetectIfSurfaceHasAlpha(scratchSurface) :
                              (bitDepth == BitDepth.Depth32Bit);

            QoiRgba[] pixels = new QoiRgba[numPixels];

            unsafe {
                int offset = 0;
                for (int y = 0; y < srcHeight; ++y) {
                    ColorBgra* src = scratchSurface.GetRowPointerUnchecked(y);

                    for (int x = 0; x < srcWidth; ++x, ++offset) {
                        pixels[offset].r = src->R;
                        pixels[offset].g = src->G;
                        pixels[offset].b = src->B;
                        pixels[offset].a = writeAlpha ? src->A : (byte)255;

                        ++src;
                    }
                }
            }

            QoiDesc desc = new QoiDesc {
                width = srcWidth,
                height = srcHeight,
                channels = writeAlpha ? 4 : 3,
                colorspace = QoiDesc.QOI_SRGB
            };
            QoiFile.Save(output, ref desc, pixels);
        }

        private bool DetectIfSurfaceHasAlpha(Surface scratchSurface) {
            bool hasAlpha = false;
            unsafe {
                for (int y = 0; y < scratchSurface.Height; ++y) {
                    ColorBgra* src = scratchSurface.GetRowPointerUnchecked(y);
                    for (int x = 0; x < scratchSurface.Width; ++x) {
                        hasAlpha = hasAlpha || (src->A != 255);
                        ++src;
                    }
                }
            }

            return hasAlpha;
        }
    }
}
