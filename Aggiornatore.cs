using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hina.Core.Configuration;
using Hina.Core.Manifest;
using Hina.Core.Net;
using Hina.Core.Patching;
using Microsoft.Extensions.Logging;

namespace MidianLauncher
{
    // Il lavoro del launcher (era Launcher\aggiorna.ps1):
    //  1. se l'exe non sta in una cartella del gioco, installa il gioco in %USERPROFILE%\Midian e crea il collegamento;
    //  2. la fusione delle patch di verdata (una volta sola);
    //  3. aspetta che ClassicUO di questa cartella sia chiuso;
    //  4. scarica l'elenco Hina firmato e confronta i file (con una cache, per non rileggere 1 GB a ogni avvio);
    //  5. se qualcosa e' cambiato, aggiorna con Hina (solo i pezzi diversi; se va storto torna com'era);
    //  6. ip e porta del server da Launcher\server.txt; se l'exe stesso e' cambiato riparte col nuovo;
    //  7. avvia il gioco. Senza internet, o se qualcosa va storto, avvia comunque il gioco (se c'e').
    internal sealed class Aggiornatore
    {
        // Dove sono le patch (VPS) e la chiave pubblica che firma l'elenco: un elenco non firmato con la chiave
        // privata di Midian viene rifiutato, quindi anche un server o una rete manomessi non possono cambiare i file.
        public const string INDIRIZZO = "http://45.13.235.243/hina/";
        public const string CHIAVE_PUBBLICA = "1sjvnOf5TeUkLVIqDri+4fhW5qvDM8lwOLhOy+UK98s=";

        private readonly Finestra _f;
        private readonly Opzioni _o;
        private readonly CancellationTokenSource _annulla = new CancellationTokenSource();
        private string _radice;

        public Aggiornatore(Finestra f, Opzioni o)
        {
            _f = f;
            _o = o;
            _radice = o.Radice ?? AppContext.BaseDirectory.TrimEnd('\\', '/');
        }

        public void Annulla() => _annulla.Cancel();

        private bool Interrotto => _f.Chiusa || _f.Salta || _annulla.IsCancellationRequested;

        private string FileGioco => Path.Combine(_radice, "ClassicUO", "ClassicUO.exe");

        public async Task Esegui()
        {
            bool avvia = true;
            try
            {
                Diario.Scrivi(_radice, "avvio " + string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));
                PulisciVecchi();

                if (!File.Exists(FileGioco) && !_o.QuiComunque)
                {
                    if (!Installa())
                    {
                        avvia = false;
                        return;
                    }
                }

                Fusione();
                await AspettaIlGioco();
                if (_f.Chiusa)
                {
                    avvia = false;
                    return;
                }

                if (!_f.Salta)
                {
                    bool riavvia = await Aggiorna();
                    if (riavvia)
                    {
                        avvia = false;
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Esci o "Gioca senza aggiornare": Hina ha gia' rimesso i file com'erano.
            }
            catch (Exception e)
            {
                Diario.Scrivi(_radice, "errore: " + e);
                if (!_f.Chiusa)
                {
                    _f.Imposta(File.Exists(FileGioco) ? "Aggiornamento non riuscito: avvio il gioco." : "Installazione non riuscita.", Breve(e.Message), 0);
                    await Task.Delay(2500);
                }

                if (!File.Exists(FileGioco))
                {
                    Win32.MessageBox(0, "Non sono riuscito a scaricare il gioco:\n" + e.Message + "\n\nControlla la connessione e riprova.", "Midian", Win32.MB_OK | Win32.MB_ICONERROR);
                    avvia = false;
                }
            }
            finally
            {
                if (avvia && !_f.Chiusa)
                {
                    AvviaGioco();
                }

                _f.Chiudi();
            }
        }

        // --- 1. Installazione ---------------------------------------------------------------------------------

        // L'exe e' stato scaricato da solo (dal sito): il gioco va in %USERPROFILE%\Midian, l'exe si copia li'
        // e riparte da li'. Falso = questo processo ha finito (riparte quello installato).
        private bool Installa()
        {
            string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Midian");
            string exe = Environment.ProcessPath ?? throw new InvalidOperationException("percorso del launcher sconosciuto");
            string nome = Path.GetFileName(exe);

            if (string.Equals(Path.GetFullPath(_radice), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                return true;    // gia' nella cartella di installazione: si scarica tutto qui
            }

            int r = Win32.MessageBox(0, "Midian verra' installato in:\n" + dest + "\n\nVerranno scaricati circa 1 GB di file. Continuare?", "Installazione di Midian", 1 | 0x40);
            if (r != 1)
            {
                return false;
            }

            Directory.CreateDirectory(dest);
            string nuovo = Path.Combine(dest, "Avvia Midian.exe");
            if (!File.Exists(nuovo) || !string.Equals(Path.GetFullPath(nuovo), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(exe, nuovo, true);
            }

            Libsodium.Metti(dest);

            Collegamento(nuovo);
            Process.Start(new ProcessStartInfo(nuovo) { WorkingDirectory = dest, UseShellExecute = false, ArgumentList = { "--qui" } });
            Diario.Scrivi(_radice, "installazione avviata in " + dest + " (da " + nome + ")");
            return false;
        }

        // Collegamento "Midian" sul desktop (con WScript.Shell, via PowerShell: niente COM nell'exe).
        private static void Collegamento(string exe)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnk = Path.Combine(desktop, "Midian.lnk");
                string cmd = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + lnk.Replace("'", "''") + "');" +
                             "$s.TargetPath='" + exe.Replace("'", "''") + "';$s.WorkingDirectory='" + Path.GetDirectoryName(exe)!.Replace("'", "''") + "';$s.Save()";
                using Process? p = Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", cmd } });
                p?.WaitForExit(15000);
            }
            catch
            {
                // il collegamento non e' indispensabile
            }
        }

        // --- 2. Fusione ---------------------------------------------------------------------------------------

        // Come "Avvia Midian.bat": i file di gioco con le patch di verdata gia' fuse (ClassicUO legge male i colori
        // di verdata). Una volta sola, segnaposto UO\midian-fuso.txt.
        private void Fusione()
        {
            string uo = Path.Combine(_radice, "UO"), fuso = Path.Combine(_radice, "ClassicUO", "Data", "Midian-fuso");
            if (!File.Exists(Path.Combine(uo, "art.mul")) || File.Exists(Path.Combine(uo, "midian-fuso.txt")) || !Directory.Exists(fuso))
            {
                return;
            }

            _f.Imposta("Preparazione dei file di gioco...", "", -1);
            foreach (string f in Directory.GetFiles(fuso))
            {
                File.Copy(f, Path.Combine(uo, Path.GetFileName(f)), true);
            }

            string verdata = Path.Combine(uo, "verdata.mul");
            if (File.Exists(verdata))
            {
                File.Move(verdata, verdata + ".originale", true);
            }

            File.WriteAllText(Path.Combine(uo, "midian-fuso.txt"), "File fusi copiati da ClassicUO\\Data\\Midian-fuso. Cancella questo file per ricopiarli.", Encoding.ASCII);
        }

        // --- 3. Gioco aperto ----------------------------------------------------------------------------------

        // Un client aperto tiene bloccati i file: si aspetta che si chiuda (Midian-ClassicUO #10).
        private async Task AspettaIlGioco()
        {
            while (!_f.Chiusa && !_f.Salta && GiocoAperto())
            {
                _f.Imposta("Il gioco e' ancora aperto: chiudilo per aggiornare.", "Oppure premi 'Gioca senza aggiornare' per aprire un altro client.", 0);
                await Task.Delay(500);
            }
        }

        private bool GiocoAperto()
        {
            foreach (Process p in Process.GetProcessesByName("ClassicUO"))
            {
                try
                {
                    string? percorso = p.MainModule?.FileName;
                    if (percorso != null && percorso.StartsWith(_radice + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // processo di un altro utente o gia' chiuso
                }
                finally
                {
                    p.Dispose();
                }
            }

            return false;
        }

        // --- 4-6. Aggiornamento -------------------------------------------------------------------------------

        // Vero = l'exe e' cambiato ed e' ripartito il nuovo.
        private async Task<bool> Aggiorna()
        {
            PatcherConfig cfg = new PatcherConfig
            {
                BaseUrl = new Uri(_o.Indirizzo ?? INDIRIZZO),
                TrustedPublicKey = CHIAVE_PUBBLICA,
                Concurrency = 6,
                MaxRetries = 4,
                RetryBaseDelayMs = 700,
                MaxRetryDelayMs = 8000
            };

            _f.Imposta("Controllo degli aggiornamenti...", "", -1);
            Libsodium.Metti(AppContext.BaseDirectory);
            Manifest elenco;
            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MidianLauncher/1");
                using HttpChunkClient hc = new HttpChunkClient(http, new RetryPolicy(3, 700, 5000, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
                elenco = await hc.GetManifestAsync(cfg.BaseUrl, cfg.Channel, _annulla.Token);
            }

            if (!ManifestSigner.Verify(elenco, CHIAVE_PUBBLICA))
            {
                throw new InvalidDataException("l'elenco dei file non ha la firma di Midian");
            }

            // Quali file sono diversi: dimensione e impronta, con la cache delle impronte (dimensione + data).
            Cache cache = Cache.Carica(_radice);
            List<ManifestFile> daFare = new List<ManifestFile>();
            int i = 0;
            foreach (ManifestFile mf in elenco.Files)
            {
                if (Interrotto)
                {
                    throw new OperationCanceledException();
                }

                i++;
                if (i % 25 == 0)
                {
                    _f.Imposta("Controllo dei file...", $"{i} di {elenco.Files.Count}", (double)i / elenco.Files.Count);
                }

                string loc = Percorso(mf.Path);
                FileInfo fi = new FileInfo(loc);
                bool uguale = false;
                if (fi.Exists && fi.Length == mf.Size)
                {
                    string h = cache.Impronta(mf.Path, fi) ?? cache.Ricorda(mf.Path, fi, Impronta(loc));
                    uguale = string.Equals(h, mf.FileHash, StringComparison.OrdinalIgnoreCase);
                }

                if (!uguale)
                {
                    daFare.Add(mf);
                }
            }

            bool exeCambiato = false;
            if (daFare.Count > 0)
            {
                long totale = daFare.Sum(x => x.Size);
                Diario.Scrivi(_radice, $"{daFare.Count} file da aggiornare ({totale / 1048576.0:0.0} MB)");
                string titolo = File.Exists(FileGioco) ? "Aggiornamento in corso..." : "Installazione in corso...";
                _f.Imposta(titolo, $"{daFare.Count} file, {totale / 1048576.0:0.#} MB", 0);

                // I file aperti da questo processo (l'exe, libsodium.dll) non si possono sovrascrivere ma si possono
                // rinominare: si rinominano in .vecchio e se ne mette una copia, che Hina aggiorna.
                foreach (ManifestFile mf in daFare)
                {
                    string loc = Percorso(mf.Path);
                    if (File.Exists(loc) && Bloccato(loc))
                    {
                        string vecchio = loc + ".vecchio";
                        File.Move(loc, vecchio, true);
                        File.Copy(vecchio, loc, true);
                        if (string.Equals(Path.GetFullPath(loc), Path.GetFullPath(Environment.ProcessPath ?? ""), StringComparison.OrdinalIgnoreCase))
                        {
                            exeCambiato = true;
                        }
                    }
                }

                Avanzamento av = new Avanzamento(_f, titolo, elenco, daFare, totale);
                using (PatchClient pc = new PatchClient(cfg, av))
                {
                    using CancellationTokenSource legato = CancellationTokenSource.CreateLinkedTokenSource(_annulla.Token);
                    Task controllo = Task.Run(async () =>
                    {
                        while (!legato.IsCancellationRequested)
                        {
                            if (_f.Chiusa || _f.Salta)
                            {
                                legato.Cancel();
                            }
                            await Task.Delay(200);
                        }
                    });
                    PatchResult r;
                    try
                    {
                        r = await pc.PatchAsync(_radice, legato.Token);
                    }
                    finally
                    {
                        legato.Cancel();
                        await controllo;
                    }

                    if (!r.Success)
                    {
                        throw new IOException("aggiornamento non riuscito: " + r.Message);
                    }

                    Diario.Scrivi(_radice, $"aggiornati {r.AppliedFiles.Count} file");
                }

                foreach (ManifestFile mf in daFare)
                {
                    string loc = Percorso(mf.Path);
                    if (File.Exists(loc))
                    {
                        cache.Ricorda(mf.Path, new FileInfo(loc), mf.FileHash);
                    }
                }
            }

            cache.Salva(_radice, elenco);
            ImpostaServer();

            if (exeCambiato && !_o.Riavviato && Environment.ProcessPath != null)
            {
                _f.Imposta("Launcher aggiornato, riavvio...", "", 1);
                ProcessStartInfo psi = new ProcessStartInfo(Environment.ProcessPath) { WorkingDirectory = _radice, UseShellExecute = false };
                psi.ArgumentList.Add("--riavviato");
                if (_o.NonAvviare) { psi.ArgumentList.Add("--non-avviare"); }
                if (_o.Indirizzo != null) { psi.ArgumentList.Add("--indirizzo"); psi.ArgumentList.Add(_o.Indirizzo); }
                if (_o.Radice != null) { psi.ArgumentList.Add("--radice"); psi.ArgumentList.Add(_o.Radice); }
                Process.Start(psi);
                return true;
            }

            _f.Imposta(daFare.Count > 0 ? $"Aggiornato ({daFare.Count} file). Buon gioco!" : "Il gioco e' aggiornato. Buon gioco!", "", 1, false);
            await Task.Delay(700);
            return false;
        }

        // Launcher\server.txt ("ip porta", nell'elenco firmato): se cambia, il launcher aggiorna solo ip e porta
        // nelle impostazioni del giocatore (ClassicUO\settings.json), senza toccare il resto.
        private void ImpostaServer()
        {
            try
            {
                string srv = Path.Combine(_radice, "Launcher", "server.txt"), set = Path.Combine(_radice, "ClassicUO", "settings.json");
                string predefinite = Path.Combine(_radice, "Launcher", "settings-predefinite.json");

                // Installazione nuova: le impostazioni di partenza (senza account), poi ip e porta come sotto.
                if (!File.Exists(set) && File.Exists(predefinite) && Directory.Exists(Path.GetDirectoryName(set)))
                {
                    File.Copy(predefinite, set);
                    Diario.Scrivi(_radice, "impostazioni di partenza copiate");
                }

                if (!File.Exists(srv) || !File.Exists(set))
                {
                    return;
                }

                string[] p = File.ReadAllText(srv).Split(new[] { ' ', '\t', '\r', '\n', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2 || !int.TryParse(p[1], out int porta) || !Regex.IsMatch(p[0], @"^[A-Za-z0-9.\-]+$"))
                {
                    return;
                }

                string testo = File.ReadAllText(set);
                string nuovo = Regex.Replace(testo, "(\"ip\"\\s*:\\s*\")[^\"]*(\")", m => m.Groups[1].Value + p[0] + m.Groups[2].Value);
                nuovo = Regex.Replace(nuovo, "(\"port\"\\s*:\\s*)\\d+", m => m.Groups[1].Value + porta);
                if (nuovo != testo)
                {
                    File.WriteAllText(set, nuovo, new UTF8Encoding(false));
                    Diario.Scrivi(_radice, "server impostato: " + p[0] + ":" + porta);
                }
            }
            catch (Exception e)
            {
                Diario.Scrivi(_radice, "server non impostato: " + e.Message);
            }
        }

        // --- 7. Avvio -----------------------------------------------------------------------------------------

        private void AvviaGioco()
        {
            if (_o.NonAvviare || !File.Exists(FileGioco))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(FileGioco) { WorkingDirectory = Path.GetDirectoryName(FileGioco)!, UseShellExecute = false })?.Dispose();
            }
            catch (Exception e)
            {
                Win32.MessageBox(0, "Non riesco ad avviare il gioco:\n" + e.Message, "Midian", Win32.MB_OK | Win32.MB_ICONERROR);
            }
        }

        // --- Utilita' -----------------------------------------------------------------------------------------

        // Le copie .vecchio lasciate da un aggiornamento dell'exe (ora non piu' in uso).
        private void PulisciVecchi()
        {
            try
            {
                foreach (string f in Directory.GetFiles(_radice, "*.vecchio"))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch
            {
            }
        }

        private string Percorso(string rel)
        {
            string p = Path.GetFullPath(Path.Combine(_radice, rel.Replace('/', '\\')));
            if (!p.StartsWith(_radice + "\\", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("percorso fuori dalla cartella del gioco: " + rel);
            }

            return p;
        }

        private static bool Bloccato(string f)
        {
            try
            {
                using FileStream s = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        public static string Impronta(string f)
        {
            using FileStream s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(s));
        }

        private static string Breve(string s) => s.Length > 90 ? s.Substring(0, 87) + "..." : s;
    }

    // Le opzioni dalla riga di comando (per le prove e per il riavvio dopo un aggiornamento).
    internal sealed class Opzioni
    {
        public string? Indirizzo;     // --indirizzo <url>: un'altra cartella di patch (prove)
        public string? Radice;        // --radice <cartella>: la cartella del gioco (prove)
        public bool NonAvviare;       // --non-avviare: aggiorna ma non apre il gioco
        public bool Riavviato;        // --riavviato: gia' ripartito dopo l'aggiornamento dell'exe
        public bool QuiComunque;      // --qui: installa/aggiorna nella cartella dell'exe anche se vuota

        public static Opzioni Leggi(string[] a)
        {
            Opzioni o = new Opzioni();
            for (int i = 0; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "--indirizzo" when i + 1 < a.Length: o.Indirizzo = a[++i].EndsWith('/') ? a[i] : a[i] + "/"; break;
                    case "--radice" when i + 1 < a.Length: o.Radice = Path.GetFullPath(a[++i]).TrimEnd('\\'); break;
                    case "--non-avviare": o.NonAvviare = true; break;
                    case "--riavviato": o.Riavviato = true; break;
                    case "--qui": o.QuiComunque = true; break;
                }
            }

            return o;
        }
    }

    // Cache delle impronte: percorso -> dimensione, data di modifica, impronta (Launcher\stato-hina.txt).
    internal sealed class Cache
    {
        private readonly Dictionary<string, (long s, long t, string h)> _v = new Dictionary<string, (long, long, string)>(StringComparer.OrdinalIgnoreCase);

        private static string File_(string radice) => Path.Combine(radice, "Launcher", "stato-hina.txt");

        public static Cache Carica(string radice)
        {
            Cache c = new Cache();
            try
            {
                if (File.Exists(File_(radice)))
                {
                    foreach (string riga in File.ReadAllLines(File_(radice)))
                    {
                        string[] p = riga.Split('\t');
                        if (p.Length == 4 && long.TryParse(p[1], out long s) && long.TryParse(p[2], out long t))
                        {
                            c._v[p[0]] = (s, t, p[3]);
                        }
                    }
                }
            }
            catch
            {
            }

            return c;
        }

        public string? Impronta(string rel, FileInfo fi) =>
            _v.TryGetValue(rel, out var e) && e.s == fi.Length && e.t == fi.LastWriteTimeUtc.Ticks ? e.h : null;

        public string Ricorda(string rel, FileInfo fi, string h)
        {
            fi.Refresh();
            _v[rel] = (fi.Length, fi.LastWriteTimeUtc.Ticks, h);
            return h;
        }

        public void Salva(string radice, Manifest elenco)
        {
            try
            {
                HashSet<string> validi = new HashSet<string>(elenco.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
                Directory.CreateDirectory(Path.GetDirectoryName(File_(radice))!);
                File.WriteAllLines(File_(radice), _v.Where(kv => validi.Contains(kv.Key)).Select(kv => $"{kv.Key}\t{kv.Value.s}\t{kv.Value.t}\t{kv.Value.h}"));
            }
            catch
            {
            }
        }
    }

    // Hina non ha un evento di avanzamento: si leggono i suoi messaggi di diario ("Patching file", "Downloading chunk")
    // per muovere la barra in base ai byte dei file gia' fatti piu' i pezzi del file in corso.
    internal sealed class Avanzamento : ILogger<PatchClient>
    {
        private readonly Finestra _f;
        private readonly string _titolo;
        private readonly Dictionary<string, ManifestFile> _daFare;
        private readonly long _totale;
        private long _fatti;
        private ManifestFile? _corrente;
        private int _ultimoTick;

        public Avanzamento(Finestra f, string titolo, Manifest elenco, List<ManifestFile> daFare, long totale)
        {
            _f = f;
            _titolo = titolo;
            _daFare = daFare.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            _totale = Math.Max(1, totale);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel livello, EventId id, TState stato, Exception? ex, Func<TState, Exception?, string> formato)
        {
            string? file = null;
            int? pezzo = null;
            string? modello = null;
            if (stato is IReadOnlyList<KeyValuePair<string, object?>> campi)
            {
                foreach (KeyValuePair<string, object?> kv in campi)
                {
                    if (kv.Key == "FilePath") { file = kv.Value as string; }
                    else if (kv.Key == "ChunkIndex" && kv.Value is int n) { pezzo = n; }
                    else if (kv.Key == "{OriginalFormat}") { modello = kv.Value as string; }
                }
            }

            if (livello >= LogLevel.Warning)
            {
                Diario.Scrivi(null, "hina: " + formato(stato, ex));
            }

            lock (this)
            {
                if (modello != null && modello.StartsWith("Patching file", StringComparison.Ordinal) && file != null && _daFare.TryGetValue(file, out ManifestFile? mf))
                {
                    if (_corrente != null) { _fatti += _corrente.Size; }
                    _corrente = mf;
                }

                long inCorso = 0;
                if (_corrente != null && pezzo.HasValue && _corrente.ChunkSize > 0)
                {
                    inCorso = Math.Min(_corrente.Size, (long)pezzo.Value * _corrente.ChunkSize);
                }

                int ora = Environment.TickCount;
                if (ora - _ultimoTick < 100 && pezzo.HasValue)
                {
                    return;
                }

                _ultimoTick = ora;
                long fatto = Math.Min(_totale, _fatti + inCorso);
                string nome = _corrente != null ? Path.GetFileName(_corrente.Path) : "";
                _f.Imposta(_titolo, $"{nome}   {fatto / 1048576.0:0.0} di {_totale / 1048576.0:0.0} MB", (double)fatto / _totale);
            }
        }
    }

    // Launcher\launcher.log: le ultime righe di cosa e' successo (per capire i problemi dei giocatori).
    internal static class Diario
    {
        private static string? _radice;

        public static void Scrivi(string? radice, string riga)
        {
            try
            {
                _radice = radice ?? _radice;
                if (_radice == null)
                {
                    return;
                }

                string dir = Path.Combine(_radice, "Launcher");
                if (!Directory.Exists(dir))
                {
                    return;
                }

                string f = Path.Combine(dir, "launcher.log");
                if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024)
                {
                    File.Move(f, f + ".1", true);
                }

                File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + riga + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
