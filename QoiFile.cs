using System;
using System.IO;
using System.Runtime.InteropServices;


// my bad C# translation of this - https://github.com/phoboslab/qoi/blob/master/qoi.h
namespace QoiFileTypeNet {
    [StructLayout(LayoutKind.Sequential)]
    internal struct QoiRgba : IEquatable<QoiRgba> {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public override bool Equals(object obj) => obj is QoiRgba other && this.Equals(other);

        public bool Equals(QoiRgba other) {
            return this.r == other.r &&
                   this.g == other.g &&
                   this.b == other.b &&
                   this.a == other.a;
        }

        public override int GetHashCode() => r * 3 + g * 5 + b * 7 + a * 11;

        public static bool operator ==(QoiRgba a, QoiRgba b) => a.Equals(b);
        public static bool operator !=(QoiRgba a, QoiRgba b) => !a.Equals(b);
    }

    internal struct QoiDesc {
        public const int QOI_SRGB       = 0x00;
        public const int QOI_LINEAR     = 0x01;

        public int width;
        public int height;
        public int channels;
        public int colorspace;
    }

    internal static class QoiFile {
        private const int QOI_OP_INDEX  = 0x00; // 00xxxxxx
        private const int QOI_OP_DIFF   = 0x40; // 01xxxxxx
        private const int QOI_OP_LUMA   = 0x80; // 10xxxxxx
        private const int QOI_OP_RUN    = 0xC0; // 11xxxxxx
        private const int QOI_OP_RGB    = 0xFE; // 11111110
        private const int QOI_OP_RGBA   = 0xFF; // 11111111

        private const int QOI_MASK_2    = 0xC0; // 11000000

        private const int QOI_MAGIC     = (((int)'q') << 24 | ((int)'o') << 16 | ((int)'i') << 8 | ((int)'f'));

        private const int QOI_HEADER_SIZE = 14;
        private const int QOI_PADDING     = 8;

        private static readonly byte[] QOI_PADDING_BYTES = { 0, 0, 0, 0, 0, 0, 0, 1 };

        private const int QOI_PIXELS_MAX  = 400000000;

        private static uint SwapBytes(uint x) {
            return ((x & 0x000000ff) << 24) |
                   ((x & 0x0000ff00) <<  8) |
                   ((x & 0x00ff0000) >>  8) |
                   ((x & 0xff000000) >> 24);
        }

        private static int SwapBytes(int x) {
            return (int)SwapBytes((uint)x);
        }

        public static QoiRgba[] Load(Stream input, ref QoiDesc desc) {
            QoiRgba[] result = null;
            desc.width = 0;
            desc.height = 0;

            int fileSize = (int)input.Length;
            if (fileSize < (QOI_HEADER_SIZE + QOI_PADDING)) {
                return result;
            }

            using (BinaryReader reader = new BinaryReader(input)) {
                uint headerMagic = SwapBytes(reader.ReadUInt32());
                if (headerMagic != QOI_MAGIC) {
                    return result;
                }

                desc.width = SwapBytes(reader.ReadInt32());
                desc.height = SwapBytes(reader.ReadInt32());
                desc.channels = reader.ReadByte();
                desc.colorspace = reader.ReadByte();

                if (desc.width == 0 || desc.height == 0 || desc.channels < 3 || desc.channels > 4) {
                    return result;
                }

                if (desc.height >= QOI_PIXELS_MAX / desc.width) {
                    return null;
                }

                int numPixels = (int)(desc.width * desc.height);
                result = new QoiRgba[numPixels];

                QoiRgba px = new QoiRgba { r = 0, g = 0, b = 0, a = 255 };
                QoiRgba[] index = new QoiRgba[64];

                int run = 0;
                int chunksLen = fileSize - QOI_PADDING;
                for (int i = 0; i < numPixels; ++i) {
                    if (run > 0) {
                        run--;
                    } else if (reader.BaseStream.Position < chunksLen) {
                        byte b1 = reader.ReadByte();

                        if (b1 == QOI_OP_RGB) {
                            px.r = reader.ReadByte();
                            px.g = reader.ReadByte();
                            px.b = reader.ReadByte();
                        } else if (b1 == QOI_OP_RGBA) {
                            px.r = reader.ReadByte();
                            px.g = reader.ReadByte();
                            px.b = reader.ReadByte();
                            px.a = reader.ReadByte();
                        } else if ((b1 & QOI_MASK_2) == QOI_OP_INDEX) {
                            px = index[b1];
                        } else if ((b1 & QOI_MASK_2) == QOI_OP_DIFF) {
                            px.r += (byte)(((b1 >> 4) & 0x03) - 2);
                            px.g += (byte)(((b1 >> 2) & 0x03) - 2);
                            px.b += (byte)(( b1       & 0x03) - 2);
                        } else if ((b1 & QOI_MASK_2) == QOI_OP_LUMA) {
                            int b2 = reader.ReadByte();
                            int vg = (b1 & 0x3F) - 32;
                            px.r += (byte)(vg - 8 + ((b2 >> 4) & 0x0F));
                            px.g += (byte) vg;
                            px.b += (byte)(vg - 8 +  (b2       & 0x0F));
                        } else if ((b1 & QOI_MASK_2) == QOI_OP_RUN) {
                            run = (b1 & 0x3F);
                        }

                        index[px.GetHashCode() % 64] = px;
                    }

                    result[i] = px;
                }
            }

            return result;
        }

        public static void Save(Stream output, ref QoiDesc desc, QoiRgba[] pixels) {
            BinaryWriter writer = new BinaryWriter(output);

            writer.Write(SwapBytes(QOI_MAGIC));
            writer.Write(SwapBytes(desc.width));
            writer.Write(SwapBytes(desc.height));
            writer.Write((byte)desc.channels);
            writer.Write((byte)desc.colorspace);

            QoiRgba px = new QoiRgba { r = 0, g = 0, b = 0, a = 255 };
            QoiRgba pxPrev = px;
            QoiRgba[] index = new QoiRgba[64];

            int run = 0;
            int numPixels = desc.width * desc.height;
            for (int i = 0; i < numPixels; ++i) {
                px = pixels[i];

                if (px == pxPrev) {
                    run++;
                    if (run == 62 || i == (numPixels - 1)) {
                        writer.Write((byte)(QOI_OP_RUN | (run - 1)));
                        run = 0;
                    }
                } else {
                    if (run > 0) {
                        writer.Write((byte)(QOI_OP_RUN | (run - 1)));
                        run = 0;
                    }

                    int indexPos = px.GetHashCode() % 64;

                    if (index[indexPos] == px) {
                        writer.Write((byte)(QOI_OP_INDEX | indexPos));
                    } else {
                        index[indexPos] = px;

                        if (px.a == pxPrev.a) {
                            int vr = px.r - pxPrev.r;
                            int vg = px.g - pxPrev.g;
                            int vb = px.b - pxPrev.b;

                            int vg_r = vr - vg;
                            int vg_b = vb - vg;

                            if (vr > -3 && vr < 2 &&
                                vg > -3 && vg < 2 &&
                                vb > -3 && vb < 2) {
                                writer.Write((byte)(QOI_OP_DIFF | (vr + 2) << 4 | (vg + 2) << 2 | (vb + 2)));
                            } else if (vg_r >  -9 && vg_r <  8 &&
                                       vg   > -33 && vg   < 32 &&
                                       vg_b >  -9 && vg_b <  8) {
                                writer.Write((byte)(QOI_OP_LUMA | (vg + 32)));
                                writer.Write((byte)((vg_r + 8) << 4 | (vg_b + 8)));
                            } else {
                                writer.Write((byte)QOI_OP_RGB);
                                writer.Write(px.r);
                                writer.Write(px.g);
                                writer.Write(px.b);
                            }
                        } else {
                            writer.Write((byte)QOI_OP_RGBA);
                            writer.Write(px.r);
                            writer.Write(px.g);
                            writer.Write(px.b);
                            writer.Write(px.a);
                        }
                    }
                }

                pxPrev = px;
            }

            for (int i = 0; i < QOI_PADDING; ++i) {
                writer.Write(QOI_PADDING_BYTES[i]);
            }
        }
    }
}
