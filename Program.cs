namespace MidianLauncher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Opzioni o = Opzioni.Leggi(args);
            string radice = o.Radice ?? AppContext.BaseDirectory.TrimEnd('\\', '/');

            // Un solo launcher per cartella.
            using Mutex unico = new Mutex(true, "MidianLauncher-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(radice.ToLowerInvariant())), 0, 8), out bool primo);
            if (!primo && !o.Riavviato)
            {
                return 0;
            }

            Finestra f = new Finestra(Grafica.Carica());
            f.Crea();
            Aggiornatore a = new Aggiornatore(f, o);
            Task lavoro = Task.Run(a.Esegui);
            Finestra.Ciclo();

            // Finestra chiusa: se l'aggiornamento e' a meta', Hina rimette i file com'erano prima di uscire.
            a.Annulla();
            try
            {
                lavoro.Wait(TimeSpan.FromSeconds(20));
            }
            catch
            {
            }

            return 0;
        }
    }
}
