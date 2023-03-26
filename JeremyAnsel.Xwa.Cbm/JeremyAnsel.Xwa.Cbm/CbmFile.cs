
namespace JeremyAnsel.Xwa.Cbm
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Text;

    public class CbmFile
    {
        private int currentImageIndex;

        public string FileName { get; private set; }

        public int CurrentImageIndex
        {
            get { return this.currentImageIndex; }

            set
            {
                if (value >= this.Images.Count || value < 0)
                {
                    this.currentImageIndex = 0;
                }
                else
                {
                    this.currentImageIndex = value;
                }
            }
        }

        public CbmImage CurrentImage
        {
            get
            {
                if (this.Images.Count == 0)
                {
                    return null;
                }

                return this.Images[this.currentImageIndex];
            }
        }

        public int AreaLeft { get { return this.Images.Count == 0 ? 0 : this.Images.Min(t => t.AreaLeft); } }

        public int AreaTop { get { return this.Images.Count == 0 ? 0 : this.Images.Min(t => t.AreaTop); } }

        public int AreaRight { get { return this.Images.Count == 0 ? 0 : this.Images.Max(t => t.AreaRight); } }

        public int AreaBottom { get { return this.Images.Count == 0 ? 0 : this.Images.Max(t => t.AreaBottom); } }

        public int Width { get { return this.AreaRight - this.AreaLeft; } }

        public int Height { get { return this.AreaBottom - this.AreaTop; } }

        public int ImageId { get; set; }

        public int GroupId { get; set; }

        public IList<CbmImage> Images { get; } = new List<CbmImage>();

        public bool IsCompressed
        {
            get
            {
                return this.Images.Count != 0 && this.Images.Any(t => t.IsCompressed);
            }
        }

        public static CbmFile FromFile(string fileName)
        {
            using var filestream = new FileStream(fileName, FileMode.Open, FileAccess.Read);

            CbmFile cbm = FromStream(filestream);
            cbm.FileName = fileName;

            return cbm;
        }

        [SuppressMessage("Style", "IDE0017:Simplifier l'initialisation des objets", Justification = "Reviewed.")]
        public static CbmFile FromStream(Stream stream)
        {
            var cbm = new CbmFile();

            using var file = new BinaryReader(stream, Encoding.ASCII, true);

            int count = file.ReadInt32();

            cbm.currentImageIndex = file.ReadInt32();

            file.BaseStream.Position += 16;

            cbm.ImageId = file.ReadInt32();
            cbm.GroupId = file.ReadInt32();

            file.BaseStream.Position += 4;

            if (cbm.currentImageIndex < 0 || cbm.currentImageIndex >= count)
            {
                cbm.currentImageIndex = 0;
            }

            for (int i = 0; i < count; i++)
            {
                var image = new CbmImage();

                image.Width = file.ReadInt32();
                image.Height = file.ReadInt32();
                image.IsCompressed = file.ReadInt32() != 0;

                int dataLength = file.ReadInt32();

                image.AreaLeft = file.ReadInt32();
                image.AreaTop = file.ReadInt32();
                image.AreaRight = file.ReadInt32();
                image.AreaBottom = file.ReadInt32();

                file.BaseStream.Position += 4;

                image.palette16 = new ushort[256];

                for (int c = 0; c < 256; c++)
                {
                    image.palette16[c] = (ushort)file.ReadUInt32();
                }

                image.palette32 = new uint[256];

                for (int c = 0; c < 256; c++)
                {
                    image.palette32[c] = file.ReadUInt32();
                }

                image.rawData = file.ReadBytes(dataLength);

                cbm.Images.Add(image);
            }

            return cbm;
        }

        public void Save(string fileName)
        {
            using var filestream = new FileStream(fileName, FileMode.Create, FileAccess.Write);

            this.Save(filestream);
            this.FileName = fileName;
        }

        public void Save(Stream stream)
        {
            using var file = new BinaryWriter(stream, Encoding.ASCII, true);

            file.Write(this.Images.Count);
            file.Write(this.currentImageIndex);
            file.Write(this.AreaLeft);
            file.Write(this.AreaTop);
            file.Write(this.AreaRight);
            file.Write(this.AreaBottom);
            file.Write(this.ImageId);
            file.Write(this.GroupId);
            file.Write(0);

            foreach (var image in this.Images)
            {
                file.Write(image.Width);
                file.Write(image.Height);
                file.Write(image.IsCompressed ? 1 : 0);
                file.Write(image.rawData == null ? 0 : image.rawData.Length);
                file.Write(image.AreaLeft);
                file.Write(image.AreaTop);
                file.Write(image.AreaRight);
                file.Write(image.AreaBottom);
                file.Write(0);

                for (int c = 0; c < 256; c++)
                {
                    file.Write((uint)image.palette16[c]);
                }

                for (int c = 0; c < 256; c++)
                {
                    file.Write(image.palette32[c]);
                }

                if (image.rawData != null)
                {
                    file.Write(image.rawData);
                }
            }
        }

        public void Decompress()
        {
            this.Images
                .AsParallel()
                .ForAll(t => t.Decompress());
        }

        public void Compress()
        {
            this.Images
                .AsParallel()
                .ForAll(t => t.Compress());
        }

        public void MoveFirst()
        {
            this.currentImageIndex = 0;
        }

        public void MovePrevious()
        {
            if (this.Images.Count == 0)
            {
                this.currentImageIndex = 0;
                return;
            }

            this.currentImageIndex--;

            if (this.currentImageIndex < 0 || this.currentImageIndex >= this.Images.Count)
            {
                this.currentImageIndex = this.Images.Count - 1;
            }
        }

        public void MoveNext()
        {
            if (this.Images.Count == 0)
            {
                this.currentImageIndex = 0;
                return;
            }

            this.currentImageIndex++;

            if (this.currentImageIndex < 0 || this.currentImageIndex >= this.Images.Count)
            {
                this.currentImageIndex = 0;
            }
        }

        public void MoveLast()
        {
            if (this.Images.Count == 0)
            {
                this.currentImageIndex = 0;
                return;
            }

            this.currentImageIndex = this.Images.Count - 1;
        }
    }
}
