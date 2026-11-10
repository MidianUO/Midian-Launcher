namespace MidianLauncher
{
    // Un'immagine "gump" di Ultima Online letta da Gumpart.mul/Gumpidx.mul (la cartella UO del client).
    // Pixel in 0xAARRGGBB; il colore 0 della gump e' trasparente.
    internal sealed class Gump
    {
        public int Larghezza { get; }
        public int Altezza { get; }
        public uint[] Pixel { get; }

        public Gump(int w, int h, uint[] px)
        {
            Larghezza = w;
            Altezza = h;
            Pixel = px;
        }

        // Formato: indice di 12 byte per gump (posizione, lunghezza, larghezza<<16|altezza); nei dati, per ogni riga
        // la sua posizione (in parole da 4 byte) e poi coppie (colore a 16 bit 1-5-5-5, ripetizioni).
        public static Gump? Leggi(FileStream idx, FileStream mul, int id)
        {
            try
            {
                byte[] voce = new byte[12];
                idx.Position = (long)id * 12;
                if (idx.Read(voce, 0, 12) != 12)
                {
                    return null;
                }

                int pos = BitConverter.ToInt32(voce, 0), lung = BitConverter.ToInt32(voce, 4), extra = BitConverter.ToInt32(voce, 8);
                int w = (extra >> 16) & 0xFFFF, h = extra & 0xFFFF;
                if (pos < 0 || lung <= 0 || w <= 0 || h <= 0 || w > 4096 || h > 4096)
                {
                    return null;
                }

                byte[] d = new byte[lung];
                mul.Position = pos;
                if (mul.Read(d, 0, lung) != lung)
                {
                    return null;
                }

                uint[] px = new uint[w * h];
                for (int y = 0; y < h; y++)
                {
                    int inizio = BitConverter.ToInt32(d, y * 4) * 4;
                    int fine = y < h - 1 ? BitConverter.ToInt32(d, (y + 1) * 4) * 4 : lung;
                    int x = 0;
                    for (int p = inizio; p + 3 < fine && p + 3 < lung && x < w; p += 4)
                    {
                        int c = BitConverter.ToUInt16(d, p), n = BitConverter.ToUInt16(d, p + 2);
                        uint argb = 0;
                        if (c != 0)
                        {
                            uint r = (uint)(((c >> 10) & 31) * 255 / 31), g = (uint)(((c >> 5) & 31) * 255 / 31), b = (uint)((c & 31) * 255 / 31);
                            argb = 0xFF000000 | (r << 16) | (g << 8) | b;
                        }
                        for (int k = 0; k < n && x < w; k++, x++)
                        {
                            px[y * w + x] = argb;
                        }
                    }
                }

                return new Gump(w, h, px);
            }
            catch
            {
                return null;
            }
        }
    }

    // Le gump usate dal launcher, lette una volta dai file del gioco. Se mancano, la finestra si disegna senza.
    internal sealed class Grafica
    {
        public const int SFONDO = 0x0500;            // la schermata di accesso di UO: il baule con l'ankh
        public const int PERGAMENA_SX = 0x09D8, PERGAMENA_MEZZO = 0x09D9, PERGAMENA_DX = 0x09DA;
        public const int BARRA_FONDO = 0x0805, BARRA_PIENA = 0x0806;
        public const int FRECCIA = 0x15A4, FRECCIA_SOPRA = 0x15A5, FRECCIA_PREMUTA = 0x15A6;
        public const int ESCI = 0x1589, ESCI_SOPRA = 0x158A, ESCI_PREMUTO = 0x158B;

        private readonly Dictionary<int, Gump> _gump = new Dictionary<int, Gump>();

        public Gump? this[int id] => _gump.TryGetValue(id, out Gump? g) ? g : null;

        // Le gump sono dentro l'exe (grafica.bin, fatto da strumenti/estrai-gump.js dai file di UO): si vedono anche
        // alla prima installazione, quando la cartella UO non c'e' ancora. Formato: per ogni gump id, larghezza,
        // altezza (int32) e i pixel a 16 bit 1-5-5-5 come nel gioco (0 = trasparente); tutto compresso deflate.
        public static Grafica Carica()
        {
            Grafica gr = new Grafica();
            try
            {
                using Stream? res = typeof(Grafica).Assembly.GetManifestResourceStream("grafica.bin");
                if (res == null)
                {
                    return gr;
                }

                using System.IO.Compression.DeflateStream z = new System.IO.Compression.DeflateStream(res, System.IO.Compression.CompressionMode.Decompress);
                using BinaryReader r = new BinaryReader(z);
                while (true)
                {
                    int id, w, h;
                    try { id = r.ReadInt32(); } catch (EndOfStreamException) { break; }
                    w = r.ReadInt32();
                    h = r.ReadInt32();
                    uint[] px = new uint[w * h];
                    for (int i = 0; i < px.Length; i++)
                    {
                        int c = r.ReadUInt16();
                        if (c != 0)
                        {
                            uint cr = (uint)(((c >> 10) & 31) * 255 / 31), cg = (uint)(((c >> 5) & 31) * 255 / 31), cb = (uint)((c & 31) * 255 / 31);
                            px[i] = 0xFF000000 | (cr << 16) | (cg << 8) | cb;
                        }
                    }
                    gr._gump[id] = new Gump(w, h, px);
                }
            }
            catch
            {
                // senza grafica: la finestra usa i colori di riserva
            }

            return gr;
        }
    }
}
