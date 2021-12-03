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

        public override int GetHashCode() => (r, g, b, a).GetHashCode();

        public static bool operator ==(QoiRgba a, QoiRgba b) => a.Equals(b);
        public static bool operator !=(QoiRgba a, QoiRgba b) => !a.Equals(b);
    }

    internal struct QoiDesc {
        public const int QOI_SRGB               = 0x00;
        public const int QOI_SRGB_LINEAR_ALPHA  = 0x01;
        public const int QOI_LINEAR             = 0x0F;

        public int width;
        public int height;
        public int channels;
        public int colorspace;
    }

    internal static class QoiFile {
        private const int QOI_INDEX = 0x00; // 00xxxxxx
        private const int QOI_RUN_8 = 0x40; // 010xxxxx
        private const int QOI_RUN_16 = 0x60; // 011xxxxx
        private const int QOI_DIFF_8 = 0x80; // 10xxxxxx
        private const int QOI_DIFF_16 = 0xC0; // 110xxxxx
        private const int QOI_DIFF_24 = 0xE0; // 1110xxxx
        private const int QOI_COLOR = 0xF0; // 1111xxxx

        private const int QOI_MASK_2 = 0xC0; // 11000000
        private const int QOI_MASK_3 = 0xE0; // 11100000
        private const int QOI_MASK_4 = 0xF0; // 11110000

        private const int QOI_MAGIC = (((int)'q') << 24 | ((int)'o') << 16 | ((int)'i') << 8 | ((int)'f'));

        private const int QOI_HEADER_SIZE = 14;
        private const int QOI_PADDING = 4;

        private static int ColorHash(ref QoiRgba c) {
            return c.r ^ c.g ^ c.b ^ c.a;
        }

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

                        if ((b1 & QOI_MASK_2) == QOI_INDEX) {
                            px = index[b1 ^ QOI_INDEX];
                        } else if ((b1 & QOI_MASK_3) == QOI_RUN_8) {
                            run = b1 & 0x1F;
                        } else if ((b1 & QOI_MASK_3) == QOI_RUN_16) {
                            byte b2 = reader.ReadByte();
                            run = (((b1 & 0x1f) << 8) | (b2)) + 32;
                        } else if ((b1 & QOI_MASK_2) == QOI_DIFF_8) {
                            px.r += (byte)(((b1 >> 4) & 0x03) - 2);
                            px.g += (byte)(((b1 >> 2) & 0x03) - 2);
                            px.b += (byte)((b1 & 0x03) - 2);
                        } else if ((b1 & QOI_MASK_3) == QOI_DIFF_16) {
                            byte b2 = reader.ReadByte();
                            px.r += (byte)((b1 & 0x1f) - 16);
                            px.g += (byte)((b2 >> 4) - 8);
                            px.b += (byte)((b2 & 0x0f) - 8);
                        } else if ((b1 & QOI_MASK_4) == QOI_DIFF_24) {
                            byte b2 = reader.ReadByte();
                            byte b3 = reader.ReadByte();
                            px.r += (byte)((((b1 & 0x0f) << 1) | (b2 >> 7)) - 16);
                            px.g += (byte)(((b2 & 0x7c) >> 2) - 16);
                            px.b += (byte)((((b2 & 0x03) << 3) | ((b3 & 0xe0) >> 5)) - 16);
                            px.a += (byte)((b3 & 0x1f) - 16);
                        } else if ((b1 & QOI_MASK_4) == QOI_COLOR) {
                            if ((b1 & 8) == 8) { px.r = reader.ReadByte(); }
                            if ((b1 & 4) == 4) { px.g = reader.ReadByte(); }
                            if ((b1 & 2) == 2) { px.b = reader.ReadByte(); }
                            if ((b1 & 1) == 1) { px.a = reader.ReadByte(); }
                        }

                        index[ColorHash(ref px) % 64] = px;
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
                    }

                    if (run > 0 && (run == 0x2020 || px != pxPrev || i == (numPixels - 1))) {
                        if (run < 33) {
                            run -= 1;
                            writer.Write((byte)(QOI_RUN_8 | run));
                        } else {
                            run -= 33;
                            writer.Write((byte)(QOI_RUN_16 | run >> 8));
                            writer.Write((byte)run);
                        }
                        run = 0;
                    }

                    if (px != pxPrev) {
                        int indexPos = ColorHash(ref px) % 64;

                        if (index[indexPos] == px) {
                            writer.Write((byte)(QOI_INDEX | indexPos));
                        } else {
                            index[indexPos] = px;

                            int vr = px.r - pxPrev.r;
                            int vg = px.g - pxPrev.g;
                            int vb = px.b - pxPrev.b;
                            int va = px.a - pxPrev.a;

                            if (vr > -17 && vr < 16 &&
                                vg > -17 && vg < 16 &&
                                vb > -17 && vb < 16 &&
                                va > -17 && va < 16) {
                                if (va == 0 &&
                                    vr > -3 && vr < 2 &&
                                    vg > -3 && vg < 2 &&
                                    vb > -3 && vb < 2) {
                                    writer.Write((byte)(QOI_DIFF_8 | ((vr + 2) << 4) | (vg + 2) << 2 | (vb + 2)));
                                } else if (va == 0 &&
                                           vr > -17 && vr < 16 &&
                                           vg >  -9 && vg <  8 &&
                                           vb >  -9 && vb <  8 ) {
                                    writer.Write((byte)(QOI_DIFF_16 | (vr + 16)));
                                    writer.Write((byte)((vg + 8) << 4 | (vb + 8)));
                                } else {
                                    writer.Write((byte)(QOI_DIFF_24 | (vr + 16) >> 1));
                                    writer.Write((byte)((vr + 16) << 7 | (vg + 16) << 2 | (vb + 16) >> 3));
                                    writer.Write((byte)((vb + 16) << 5 | (va + 16)));
                                }
                            } else {
                                writer.Write((byte)(QOI_COLOR | ((vr != 0) ? 8 : 0) | ((vg != 0) ? 4 : 0) | ((vb != 0) ? 2 : 0) | ((va != 0) ? 1 : 0)));
                                if (vr != 0) { writer.Write(px.r); }
                                if (vg != 0) { writer.Write(px.g); }
                                if (vb != 0) { writer.Write(px.b); }
                                if (va != 0) { writer.Write(px.a); }
                            }
                        }
                    }

                    pxPrev = px;
                }

                for (int i = 0; i < QOI_PADDING; ++i) {
                    writer.Write((byte)0);
                }
        }
    }
}
