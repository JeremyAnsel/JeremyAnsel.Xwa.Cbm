
namespace JeremyAnsel.Xwa.Cbm
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;

    public class CbmImage
    {
        public int Width { get; internal set; }

        public int Height { get; internal set; }

        public bool IsCompressed { get; internal set; }

        public int AreaLeft { get; internal set; }

        public int AreaTop { get; internal set; }

        public int AreaRight { get; internal set; }

        public int AreaBottom { get; internal set; }

        public int OffsetX
        {
            get { return this.AreaLeft; }

            set
            {
                this.AreaLeft = value;
                this.AreaRight = value + this.Width;
            }
        }

        public int OffsetY
        {
            get { return this.AreaTop; }

            set
            {
                this.AreaTop = value;
                this.AreaBottom = value + this.Height;
            }
        }

        internal ushort[] palette16;

        internal uint[] palette32;

        internal byte[] rawData;

        public static CbmImage FromFile(string fileName)
        {
            CbmImage image = new CbmImage();

            image.ReplaceWithFile(fileName);

            return image;
        }

        public static CbmImage FromMemory(int width, int height, byte[] data)
        {
            CbmImage image = new CbmImage();

            image.ReplaceWithMemory(width, height, data);

            return image;
        }

        public ushort[] GetPalette16()
        {
            return this.palette16;
        }

        public uint[] GetPalette32()
        {
            return this.palette32;
        }

        public byte[] GetRawData()
        {
            return this.rawData;
        }

        public byte[] GetImageData()
        {
            if (this.rawData == null)
            {
                return null;
            }

            if (this.palette32 == null)
            {
                return null;
            }

            if (this.IsCompressed)
            {
                return this.DecompressData();
            }

            int length = this.Width * this.Height;
            byte[] data = new byte[length * 4];

            for (int i = 0; i < length; i++)
            {
                byte pal = this.rawData[i];

                data[i * 4 + 0] = (byte)((this.palette32[pal] >> 16) & 0xff);
                data[i * 4 + 1] = (byte)((this.palette32[pal] >> 8) & 0xff);
                data[i * 4 + 2] = (byte)(this.palette32[pal] & 0xff);
                data[i * 4 + 3] = 0xff;
            }

            return data;
        }

        private byte[] DecompressData()
        {
            byte[] data = new byte[this.Width * this.Height * 4];

            int index = 0;
            int dataIndex = 0;

            for (int y = 0; y < this.Height; y++)
            {
                int indexW = index + 4 + BitConverter.ToInt32(this.rawData, index);
                index += 4;

                while (true)
                {
                    byte op = this.rawData[index];
                    index++;

                    if (op == 0x80)
                    {
                        break;
                    }

                    if (index >= indexW)
                    {
                        throw new InvalidDataException();
                    }

                    if ((op & 0x80) != 0)
                    {
                        op &= 0x7f;

                        for (int i = 0; i < op; i++)
                        {
                            byte pal = this.rawData[index];
                            index++;

                            data[dataIndex + 0] = (byte)((this.palette32[pal] >> 16) & 0xff);
                            data[dataIndex + 1] = (byte)((this.palette32[pal] >> 8) & 0xff);
                            data[dataIndex + 2] = (byte)(this.palette32[pal] & 0xff);
                            data[dataIndex + 3] = 0xff;
                            dataIndex += 4;
                        }
                    }
                    else if ((op & 0x40) != 0)
                    {
                        op &= 0x3f;

                        for (int i = 0; i < op; i++)
                        {
                            data[dataIndex + 0] = 0;
                            data[dataIndex + 1] = 0;
                            data[dataIndex + 2] = 0;
                            data[dataIndex + 3] = 0;
                            dataIndex += 4;
                        }
                    }
                    else
                    {
                        byte pal = this.rawData[index];
                        index++;

                        byte b = (byte)((this.palette32[pal] >> 16) & 0xff);
                        byte g = (byte)((this.palette32[pal] >> 8) & 0xff);
                        byte r = (byte)(this.palette32[pal] & 0xff);

                        for (int i = 0; i < op; i++)
                        {
                            data[dataIndex + 0] = b;
                            data[dataIndex + 1] = g;
                            data[dataIndex + 2] = r;
                            data[dataIndex + 3] = 0xff;
                            dataIndex += 4;
                        }
                    }
                }
            }

            return data;
        }

        public void Decompress()
        {
            if (!this.IsCompressed)
            {
                return;
            }

            this.InitData(this.Width, this.Height, this.DecompressData());
        }

        public void Compress()
        {
            this.Compress(null);
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "Reviewed")]
        private void Compress(byte[] data32)
        {
            if (this.IsCompressed)
            {
                return;
            }

            if (this.rawData == null)
            {
                return;
            }

            if (this.palette32 == null)
            {
                return;
            }

            void addSegment(List<Tuple<byte, ArraySegment<byte>>> values, byte[] array, byte t, int c, int n, int max)
            {
                while (n > 0)
                {
                    if (n <= max)
                    {
                        values.Add(new Tuple<byte, ArraySegment<byte>>(
                            t,
                            new ArraySegment<byte>(array, c, n)));
                        c += n;
                        n = 0;
                    }
                    else
                    {
                        values.Add(new Tuple<byte, ArraySegment<byte>>(
                            t,
                            new ArraySegment<byte>(array, c, max)));
                        c += max;
                        n -= max;
                    }
                }
            }

            List<Tuple<byte, ArraySegment<byte>>> parseLine(ArraySegment<byte> t)
            {
                int tLength = t.Offset + t.Count;

                var values = new List<Tuple<byte, ArraySegment<byte>>>();

                for (int i = t.Offset; i < tLength;)
                {
                    int c;
                    int n;
                    byte v;

                    c = i;
                    n = 0;
                    v = 0;

                    for (; i < tLength; i++)
                    {
                        if ((n > 0 && t.Array[i] == v) || (data32 != null && data32[i * 4 + 3] == 0))
                        {
                            break;
                        }

                        v = t.Array[i];

                        n++;
                    }

                    if (n > 0 && i < tLength && t.Array[i] == v)
                    {
                        i--;
                        n--;
                    }

                    addSegment(values, t.Array, 0, c, n, 127);

                    if (data32 != null)
                    {
                        c = i;
                        n = 0;

                        for (; i < tLength; i++)
                        {
                            if (data32[i * 4 + 3] != 0)
                            {
                                break;
                            }

                            n++;
                        }

                        addSegment(values, t.Array, 1, c, n, 63);
                    }

                    c = i;
                    n = 0;
                    v = i < tLength ? t.Array[i] : (byte)0;

                    for (; i < tLength; i++)
                    {
                        if (t.Array[i] != v || (data32 != null && data32[i * 4 + 3] == 0))
                        {
                            break;
                        }

                        n++;
                    }

                    if (i >= tLength || n > 0)
                    {
                        addSegment(values, t.Array, 2, c, n, 63);
                    }
                }

                return values;
            }

            var lines = Enumerable.Range(0, this.Height)
                .Select(t => new ArraySegment<byte>(this.rawData, t * this.Width, this.Width))
                .Select(t => parseLine(t))
                .ToArray();

            List<byte> writeLine(List<Tuple<byte, ArraySegment<byte>>> t)
            {
                List<byte> data = new List<byte>();

                foreach (var block in t)
                {
                    if (block.Item1 == 0)
                    {
                        data.Add((byte)(block.Item2.Count | 0x80));

                        for (int i = block.Item2.Offset; i < block.Item2.Offset + block.Item2.Count; i++)
                        {
                            data.Add(block.Item2.Array[i]);
                        }
                    }
                    else if (block.Item1 == 1)
                    {
                        data.Add((byte)(block.Item2.Count | 0x40));
                    }
                    else
                    {
                        data.Add((byte)block.Item2.Count);
                        data.Add((byte)block.Item2.Array[block.Item2.Offset]);
                    }
                }

                data.Add(0x80);

                data.InsertRange(0, BitConverter.GetBytes(data.Count));

                return data;
            }

            var linesData = lines
                .SelectMany(t => writeLine(t))
                .ToArray();

            this.IsCompressed = true;
            this.rawData = linesData;
        }

        public void SetPalette(uint[] palette)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            if (palette.Length != 256)
            {
                throw new ArgumentOutOfRangeException(nameof(palette));
            }

            this.palette32 = palette;

            for (int i = 0; i < 256; i++)
            {
                this.palette32[i] &= 0xffffff;
            }

            this.palette16 = new ushort[256];

            for (int i = 0; i < 256; i++)
            {
                uint color = palette[i];

                uint b = (color >> 16) & 0xffU;
                uint g = (color >> 8) & 0xffU;
                uint r = color & 0xffU;

                b = (b * (0x1fU * 2) + 0xffU) / (0xffU * 2);
                g = (g * (0x3fU * 2) + 0xffU) / (0xffU * 2);
                r = (r * (0x1fU * 2) + 0xffU) / (0xffU * 2);

                this.palette16[i] = (ushort)((b << 11) | (g << 5) | r);
            }
        }

        public void SetRawData(int width, int height, byte[] data)
        {
            if (width < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length != width * height)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }

            this.Width = width;
            this.Height = height;
            this.IsCompressed = false;
            this.rawData = data;

            this.AreaLeft = 0;
            this.AreaTop = 0;
            this.AreaRight = width;
            this.AreaBottom = height;
        }

        public void Save(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToUpperInvariant();

            ImageFormat format;

            switch (ext)
            {
                case ".BMP":
                    format = ImageFormat.Bmp;
                    break;

                case ".PNG":
                    format = ImageFormat.Png;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(fileName));
            }

            var data = this.GetImageData();

            if (data == null)
            {
                return;
            }

            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);

            try
            {
                using (var bitmap = new Bitmap(this.Width, this.Height, this.Width * 4, PixelFormat.Format32bppArgb, handle.AddrOfPinnedObject()))
                {
                    bitmap.Save(fileName, format);
                }
            }
            finally
            {
                handle.Free();
            }
        }

        public void ReplaceWithFile(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToUpperInvariant();

            switch (ext)
            {
                case ".BMP":
                case ".PNG":
                case ".JPG":
                case ".GIF":
                    if (!File.Exists(fileName))
                    {
                        throw new FileNotFoundException(null, fileName);
                    }

                    int w;
                    int h;
                    byte[] bytes;

                    using (var file = new Bitmap(fileName))
                    {
                        var rect = new Rectangle(0, 0, file.Width, file.Height);
                        int length = file.Width * file.Height;

                        w = file.Width;
                        h = file.Height;
                        bytes = new byte[length * 4];

                        using (var bitmap = file.Clone(rect, PixelFormat.Format32bppArgb))
                        {
                            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

                            try
                            {
                                Marshal.Copy(data.Scan0, bytes, 0, length * 4);
                            }
                            finally
                            {
                                bitmap.UnlockBits(data);
                            }
                        }
                    }

                    this.InitData(w, h, bytes);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(fileName));
            }
        }

        public void ReplaceWithMemory(int width, int height, byte[] data)
        {
            if (width < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length != width * height * 4)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }

            this.InitData(width, height, data);
        }

        private void InitData(int w, int h, byte[] data)
        {
            int length = w * h;

            uint[] palette = new uint[256];
            byte[] colors;

            for (int i = 0; i < length; i++)
            {
                if (data[i * 4 + 3] >= 0x80)
                {
                    data[i * 4 + 3] = 0xff;
                }
                else
                {
                    data[i * 4 + 0] = 0;
                    data[i * 4 + 1] = 0;
                    data[i * 4 + 2] = 0;
                    data[i * 4 + 3] = 0;
                }
            }

            var dataColors = Enumerable.Range(0, length)
                .Select(t => (uint)((data[t * 4 + 0] << 16) | (data[t * 4 + 1] << 8) | data[t * 4 + 2]))
                .Distinct()
                .ToArray();

            if (dataColors.Length <= 256)
            {
                for (int i = 0; i < dataColors.Length; i++)
                {
                    palette[i] = dataColors[i];
                }

                for (int i = dataColors.Length; i < 256; i++)
                {
                    palette[i] = 0;
                }

                colors = Enumerable.Range(0, length)
                    .Select(t => (uint)((data[t * 4 + 0] << 16) | (data[t * 4 + 1] << 8) | data[t * 4 + 2]))
                    .Select(t =>
                    {
                        for (int i = 0; i < dataColors.Length; i++)
                        {
                            if (dataColors[i] == t)
                            {
                                return (byte)i;
                            }
                        }

                        return (byte)0;
                    })
                    .ToArray();
            }
            else
            {
                var image = new ColorQuant.WuColorQuantizer().Quantize(data);
                int paletteColorsCount = image.Palette.Length / 4;

                for (int i = 0; i < paletteColorsCount; i++)
                {
                    palette[i] = (uint)((image.Palette[i * 4 + 0] << 16) | (image.Palette[i * 4 + 1] << 8) | image.Palette[i * 4 + 2]);
                }

                for (int i = paletteColorsCount; i < 256; i++)
                {
                    palette[i] = 0;
                }

                colors = image.Bytes;
            }

            this.SetPalette(palette);
            this.SetRawData(w, h, colors);

            //this.Compress(data);
        }

        public void MakeColorTransparent(byte red, byte green, byte blue)
        {
            var data = this.GetImageData();

            if (data == null)
            {
                return;
            }

            int length = data.Length / 4;

            for (int i = 0; i < length; i++)
            {
                byte r = data[i * 4 + 2];
                byte g = data[i * 4 + 1];
                byte b = data[i * 4 + 0];

                if (r == red && g == green && b == blue)
                {
                    data[i * 4 + 3] = 0;
                }
            }

            this.InitData(this.Width, this.Height, data);
        }

        public void MakeColorTransparent(byte red0, byte green0, byte blue0, byte red1, byte green1, byte blue1)
        {
            var data = this.GetImageData();

            if (data == null)
            {
                return;
            }

            int length = data.Length / 4;

            for (int i = 0; i < length; i++)
            {
                byte r = data[i * 4 + 2];
                byte g = data[i * 4 + 1];
                byte b = data[i * 4 + 0];

                if (r >= red0 && r <= red1 && g >= green0 && g <= green1 && b >= blue0 && b <= blue1)
                {
                    data[i * 4 + 3] = 0;
                }
            }

            this.InitData(this.Width, this.Height, data);
        }
    }
}
