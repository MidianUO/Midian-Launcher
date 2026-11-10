namespace MidianLauncher
{
    // libsodium.dll serve a NSec (Hina) per controllare la firma dell'elenco. E' dentro l'exe: si scrive nella
    // cartella indicata se manca o e' diversa, prima di usare Hina.
    internal static class Libsodium
    {
        public static void Metti(string cartella)
        {
            string dest = Path.Combine(cartella, "libsodium.dll");
            using Stream? res = typeof(Libsodium).Assembly.GetManifestResourceStream("libsodium.dll");
            if (res == null)
            {
                return;
            }

            if (File.Exists(dest) && new FileInfo(dest).Length == res.Length)
            {
                return;
            }

            try
            {
                using FileStream f = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                res.CopyTo(f);
            }
            catch (IOException)
            {
                // gia' caricata da questo processo (bloccata): va bene quella che c'e'
            }
        }
    }
}
