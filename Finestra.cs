using System.Runtime.InteropServices;
using static MidianLauncher.Win32;

namespace MidianLauncher
{
    // La finestra del launcher: 640x480 senza bordi, come la schermata di accesso di Ultima Online.
    // Si disegna tutto in un'immagine in memoria (le gump del gioco), poi i testi con GDI, poi sullo schermo.
    // Il lavoro di aggiornamento gira in un altro thread e cambia solo lo Stato; la finestra si ridisegna da sola.
    internal sealed unsafe class Finestra
    {
        public const int W = 640, H = 480;
        private const uint WM_RIDISEGNA = WM_APP + 1, WM_CHIUDI = WM_APP + 2;

        // Lo stato mostrato: lo cambia il thread dell'aggiornamento.
        private readonly object _blocco = new object();
        private string _stato = "Controllo degli aggiornamenti...";
        private string _dettaglio = "";
        private double _progresso;                 // 0..1; < 0 = in corso senza percentuale
        private bool _mostraSalta = true;

        public volatile bool Salta;                // "Gioca senza aggiornare"
        public volatile bool Chiusa;               // pulsante Esci o finestra chiusa

        private readonly Grafica _gr;
        private nint _hwnd, _memDC, _dib, _fontTitolo, _fontStato, _fontPiccolo;
        private uint* _bits;
        private int _sopra = -1, _premuto = -1;     // pulsante sotto il mouse / premuto: 0 = Esci, 1 = Gioca senza aggiornare
        private int _fase;                          // animazione della barra senza percentuale
        private static Finestra? _istanza;
        private static readonly WndProc _proc = Procedura;  // tenuto vivo: Windows lo chiama

        // Dove stanno i pulsanti (in pixel della finestra).
        private static readonly RECT PULSANTE_ESCI = new RECT { left = 594, top = 10, right = 630, bottom = 46 };
        private static readonly RECT PULSANTE_SALTA = new RECT { left = 22, top = 444, right = 210, bottom = 468 };

        public Finestra(Grafica grafica)
        {
            _gr = grafica;
        }

        public void Imposta(string? stato = null, string? dettaglio = null, double? progresso = null, bool? mostraSalta = null)
        {
            lock (_blocco)
            {
                if (stato != null) { _stato = stato; }
                if (dettaglio != null) { _dettaglio = dettaglio; }
                if (progresso.HasValue) { _progresso = progresso.Value; }
                if (mostraSalta.HasValue) { _mostraSalta = mostraSalta.Value; }
            }

            if (_hwnd != 0)
            {
                PostMessage(_hwnd, WM_RIDISEGNA, 0, 0);
            }
        }

        public void Chiudi()
        {
            if (_hwnd != 0)
            {
                PostMessage(_hwnd, WM_CHIUDI, 0, 0);
            }
        }

        public void Crea()
        {
            _istanza = this;
            nint inst = GetModuleHandle(0);
            fixed (char* nome = "MidianLauncher")
            {
                WNDCLASSEXW wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                    hInstance = inst,
                    hCursor = LoadCursor(0, IDC_ARROW),
                    hIcon = ExtractIcon(inst, Environment.ProcessPath ?? "", 0),
                    lpszClassName = nome
                };
                RegisterClassEx(&wc);
            }

            int x = (GetSystemMetrics(0) - W) / 2, y = (GetSystemMetrics(1) - H) / 2;
            _hwnd = CreateWindowEx(WS_EX_APPWINDOW, "MidianLauncher", "Midian", WS_POPUP | WS_SYSMENU | WS_MINIMIZEBOX, x, y, W, H, 0, 0, inst, 0);

            // L'immagine in memoria (dall'alto in basso, 32 bit) e i caratteri.
            nint dc = GetDC(_hwnd);
            _memDC = CreateCompatibleDC(dc);
            ReleaseDC(_hwnd, dc);
            BITMAPINFOHEADER bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32 };
            void* bits;
            _dib = CreateDIBSection(_memDC, &bi, 0, &bits, 0, 0);
            _bits = (uint*)bits;
            SelectObject(_memDC, _dib);
            _fontTitolo = CreateFont(-46, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Georgia");
            _fontStato = CreateFont(-17, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Georgia");
            _fontPiccolo = CreateFont(-13, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");

            SetTimer(_hwnd, 1, 60, 0);
            ShowWindow(_hwnd, SW_SHOW);
            UpdateWindow(_hwnd);
        }

        public static void Ciclo()
        {
            MSG m;
            while (GetMessage(&m, 0, 0, 0) > 0)
            {
                TranslateMessage(&m);
                DispatchMessage(&m);
            }
        }

        private static nint Procedura(nint hwnd, uint msg, nint w, nint l)
        {
            Finestra? f = _istanza;
            if (f == null)
            {
                return DefWindowProc(hwnd, msg, w, l);
            }

            switch (msg)
            {
                case WM_ERASEBKGND:
                    return 1;
                case WM_PAINT:
                    f.Disegna();
                    return 0;
                case WM_RIDISEGNA:
                    InvalidateRect(hwnd, 0, false);
                    return 0;
                case WM_TIMER:
                    bool animata;
                    lock (f._blocco) { animata = f._progresso < 0; }
                    if (animata)
                    {
                        f._fase++;
                        InvalidateRect(hwnd, 0, false);
                    }
                    return 0;
                case WM_NCHITTEST:
                {
                    // Si trascina la finestra da qualunque punto che non sia un pulsante.
                    POINT p = new POINT { x = LoWord(l), y = HiWord(l) };
                    ScreenToClient(hwnd, &p);
                    return f.PulsanteIn(p.x, p.y) >= 0 ? HTCLIENT : HTCAPTION;
                }
                case WM_SETCURSOR:
                    if (f._sopra >= 0)
                    {
                        SetCursor(LoadCursor(0, IDC_HAND));
                        return 1;
                    }
                    break;
                case WM_MOUSEMOVE:
                {
                    int s = f.PulsanteIn(LoWord(l), HiWord(l));
                    if (s != f._sopra)
                    {
                        f._sopra = s;
                        InvalidateRect(hwnd, 0, false);
                    }
                    TRACKMOUSEEVENT t = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TME_LEAVE, hwndTrack = hwnd };
                    TrackMouseEvent(&t);
                    return 0;
                }
                case WM_MOUSELEAVE:
                    if (f._premuto < 0)
                    {
                        f._sopra = -1;
                        InvalidateRect(hwnd, 0, false);
                    }
                    return 0;
                case WM_LBUTTONDOWN:
                    f._premuto = f.PulsanteIn(LoWord(l), HiWord(l));
                    if (f._premuto >= 0)
                    {
                        SetCapture(hwnd);
                    }
                    InvalidateRect(hwnd, 0, false);
                    return 0;
                case WM_LBUTTONUP:
                {
                    int p = f._premuto;
                    f._premuto = -1;
                    ReleaseCapture();
                    if (p >= 0 && p == f.PulsanteIn(LoWord(l), HiWord(l)))
                    {
                        if (p == 0)
                        {
                            f.Chiusa = true;
                            DestroyWindow(hwnd);
                            return 0;
                        }
                        f.Salta = true;
                        f.Imposta(stato: "Avvio del gioco...", dettaglio: "", mostraSalta: false);
                    }
                    InvalidateRect(hwnd, 0, false);
                    return 0;
                }
                case WM_CLOSE:
                    f.Chiusa = true;
                    DestroyWindow(hwnd);
                    return 0;
                case WM_CHIUDI:
                    DestroyWindow(hwnd);
                    return 0;
                case WM_DESTROY:
                    f._hwnd = 0;
                    PostQuitMessage(0);
                    return 0;
            }

            return DefWindowProc(hwnd, msg, w, l);
        }

        private int PulsanteIn(int x, int y)
        {
            if (Dentro(PULSANTE_ESCI, x, y))
            {
                return 0;
            }

            bool salta;
            lock (_blocco) { salta = _mostraSalta; }
            return salta && Dentro(PULSANTE_SALTA, x, y) ? 1 : -1;
        }

        private static bool Dentro(RECT r, int x, int y) => x >= r.left && x < r.right && y >= r.top && y < r.bottom;

        // --- Disegno ---------------------------------------------------------------------------------------

        private void Disegna()
        {
            string stato, dettaglio;
            double progresso;
            bool salta;
            lock (_blocco)
            {
                stato = _stato;
                dettaglio = _dettaglio;
                progresso = _progresso;
                salta = _mostraSalta;
            }

            // 1) Le immagini.
            Gump? sfondo = _gr[Grafica.SFONDO];
            if (sfondo != null)
            {
                Metti(sfondo, 0, 0);
            }
            else
            {
                for (int i = 0; i < W * H; i++)
                {
                    _bits[i] = 0xFF1E140C;
                }
            }

            // Pergamena per il messaggio: inizio, centro ripetuto, fine.
            Gump? psx = _gr[Grafica.PERGAMENA_SX], pmz = _gr[Grafica.PERGAMENA_MEZZO], pdx = _gr[Grafica.PERGAMENA_DX];
            const int PX = 70, PY = 388, PW = 500;
            if (psx != null && pmz != null && pdx != null)
            {
                Metti(psx, PX, PY);
                int x = PX + psx.Larghezza, fine = PX + PW - pdx.Larghezza;
                while (x < fine)
                {
                    Metti(pmz, x, PY, Math.Min(pmz.Larghezza, fine - x));
                    x += pmz.Larghezza;
                }
                Metti(pdx, fine, PY);
            }

            // Barra: il fondo e il pieno delle barre di stato di UO, allargati.
            const int BX = 156, BY = 434, BW = 328, BH = 11;
            Gump? bf = _gr[Grafica.BARRA_FONDO], bp = _gr[Grafica.BARRA_PIENA];
            if (bf != null && bp != null)
            {
                Allarga(bf, BX, BY, BW, BH, BW);
                if (progresso >= 0)
                {
                    Allarga(bp, BX, BY, BW, BH, (int)(BW * Math.Clamp(progresso, 0, 1)));
                }
                else
                {
                    // In corso: un tratto che scorre.
                    int tratto = BW / 4, pos = (_fase * 8) % (BW + tratto) - tratto;
                    AllargaParte(bp, BX, BY, BW, BH, Math.Max(0, pos), Math.Min(BW, pos + tratto));
                }
            }

            // Pulsanti.
            Gump? esci = _gr[_premuto == 0 ? Grafica.ESCI_PREMUTO : _sopra == 0 ? Grafica.ESCI_SOPRA : Grafica.ESCI];
            if (esci != null)
            {
                Metti(esci, PULSANTE_ESCI.left, PULSANTE_ESCI.top);
            }

            if (salta)
            {
                Gump? freccia = _gr[_premuto == 1 ? Grafica.FRECCIA_PREMUTA : _sopra == 1 ? Grafica.FRECCIA_SOPRA : Grafica.FRECCIA];
                if (freccia != null)
                {
                    Metti(freccia, PULSANTE_SALTA.left, PULSANTE_SALTA.top + 1);
                }
            }

            // 2) I testi.
            GdiFlush();
            SetBkMode(_memDC, TRANSPARENT);
            Testo(_fontTitolo, "MIDIAN", new RECT { left = 0, top = 22, right = W, bottom = 82 }, DT_CENTER, Rgb(232, 196, 92), true);
            Testo(_fontPiccolo, "Shard di Ultima Online", new RECT { left = 0, top = 78, right = W, bottom = 98 }, DT_CENTER, Rgb(200, 180, 140), true);
            Testo(_fontStato, stato, new RECT { left = PX + 40, top = PY, right = PX + PW - 45, bottom = PY + 38 }, DT_CENTER, Rgb(62, 38, 16), false);
            Testo(_fontPiccolo, dettaglio, new RECT { left = 130, top = 448, right = 580, bottom = 470 }, DT_CENTER, Rgb(225, 205, 160), true);
            if (salta)
            {
                uint col = _sopra == 1 ? Rgb(255, 225, 130) : Rgb(215, 190, 130);
                Testo(_fontPiccolo, "Gioca senza aggiornare", new RECT { left = PULSANTE_SALTA.left + 24, top = PULSANTE_SALTA.top, right = PULSANTE_SALTA.right + 40, bottom = PULSANTE_SALTA.bottom }, DT_LEFT, col, true);
            }

            // 3) Sullo schermo.
            PAINTSTRUCT ps;
            nint dc = BeginPaint(_hwnd, &ps);
            BitBlt(dc, 0, 0, W, H, _memDC, 0, 0, SRCCOPY);
            EndPaint(_hwnd, &ps);
        }

        private void Testo(nint font, string t, RECT r, uint allinea, uint colore, bool ombra)
        {
            if (string.IsNullOrEmpty(t))
            {
                return;
            }

            SelectObject(_memDC, font);
            uint f = allinea | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS | DT_NOPREFIX;
            if (ombra)
            {
                RECT o = new RECT { left = r.left + 2, top = r.top + 2, right = r.right + 2, bottom = r.bottom + 2 };
                SetTextColor(_memDC, Rgb(0, 0, 0));
                DrawText(_memDC, t, t.Length, &o, f);
            }

            SetTextColor(_memDC, colore);
            DrawText(_memDC, t, t.Length, &r, f);
        }

        // Copia una gump (i pixel trasparenti restano quelli sotto).
        private void Metti(Gump g, int x0, int y0, int larghezza = -1)
        {
            int lw = larghezza < 0 ? g.Larghezza : larghezza;
            for (int y = 0; y < g.Altezza; y++)
            {
                int ty = y0 + y;
                if (ty < 0 || ty >= H)
                {
                    continue;
                }

                for (int x = 0; x < lw; x++)
                {
                    int tx = x0 + x;
                    uint p = g.Pixel[y * g.Larghezza + x];
                    if (tx >= 0 && tx < W && (p & 0xFF000000) != 0)
                    {
                        _bits[ty * W + tx] = p;
                    }
                }
            }
        }

        // Disegna la gump allargata a w x h, solo le prime "visibili" colonne.
        private void Allarga(Gump g, int x0, int y0, int w, int h, int visibili) => AllargaParte(g, x0, y0, w, h, 0, visibili);

        private void AllargaParte(Gump g, int x0, int y0, int w, int h, int da, int a)
        {
            for (int y = 0; y < h; y++)
            {
                int sy = y * g.Altezza / h;
                for (int x = da; x < a; x++)
                {
                    int sx = x * g.Larghezza / w;
                    uint p = g.Pixel[sy * g.Larghezza + sx];
                    int tx = x0 + x, ty = y0 + y;
                    if ((p & 0xFF000000) != 0 && tx >= 0 && tx < W && ty >= 0 && ty < H)
                    {
                        _bits[ty * W + tx] = p;
                    }
                }
            }
        }
    }
}
