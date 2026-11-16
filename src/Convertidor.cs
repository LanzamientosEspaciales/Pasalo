using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Convertidor de Video")]
[assembly: AssemblyProduct("Convertidor de Video")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    static readonly Color Accent = Color.FromArgb(37, 99, 235);
    static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
    static readonly Color HeaderBg = Color.FromArgb(17, 24, 39);
    static readonly Color Muted = Color.FromArgb(107, 114, 128);
    static readonly Color Ok = Color.FromArgb(22, 163, 74);
    static readonly Color Bad = Color.FromArgb(220, 38, 38);
    static readonly string[] Exts = { ".dav", ".avi", ".mkv", ".mov", ".mp4", ".wmv", ".flv", ".ts", ".mts", ".m2ts", ".h264", ".264", ".h265", ".265", ".mpg", ".mpeg", ".3gp", ".webm", ".asf", ".vob" };

    ListView list;
    Label dropHint, status;
    ComboBox format;
    RadioButton sameFolder, otherFolder;
    TextBox outFolder;
    Button btnBrowse, btnAdd, btnRemove, btnClear, btnConvert, btnCancel, btnOpen;
    ProgressBar progress;

    string ffmpeg;
    volatile bool cancel;
    Process current;
    string lastOutput;

    public MainForm()
    {
        Text = "Convertidor de Video";
        Font = new Font("Segoe UI", 9.75f);
        BackColor = Color.White;
        ClientSize = new Size(880, 600);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // Encabezado
        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = HeaderBg };
        header.Controls.Add(new Label { Text = "Convertidor de Video", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 17f), AutoSize = true, Location = new Point(22, 12) });
        header.Controls.Add(new Label { Text = "Convierte grabaciones de DVR (.dav) y otros videos a MP4, MKV, AVI o MOV", ForeColor = Color.FromArgb(156, 163, 175), AutoSize = true, Location = new Point(25, 48) });

        // Barra de botones
        btnAdd = MakeButton("+  Agregar archivos", true);
        btnRemove = MakeButton("Quitar seleccionados", false);
        btnClear = MakeButton("Limpiar lista", false);
        btnAdd.Click += delegate { AddDialog(); };
        btnRemove.Click += delegate { foreach (ListViewItem it in list.SelectedItems) list.Items.Remove(it); UpdateUi(); };
        btnClear.Click += delegate { list.Items.Clear(); UpdateUi(); };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(18, 12, 18, 0) };
        toolbar.Controls.AddRange(new Control[] { btnAdd, btnRemove, btnClear });

        // Lista
        list = new ListView { View = View.Details, FullRowSelect = true, AllowDrop = true, ShowItemToolTips = true, BorderStyle = BorderStyle.FixedSingle, HeaderStyle = ColumnHeaderStyle.Nonclickable, Dock = DockStyle.Fill };
        list.Columns.Add("Archivo", 360);
        list.Columns.Add("Carpeta", 230);
        list.Columns.Add("Tamaño", 90, HorizontalAlignment.Right);
        list.Columns.Add("Estado", 140);
        list.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && btnConvert.Enabled ? DragDropEffects.Copy : DragDropEffects.None; };
        list.DragDrop += (s, e) => AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));
        list.DoubleClick += delegate
        {
            if (list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is Job && ((Job)list.SelectedItems[0].Tag).Output != null)
                ShowInExplorer(((Job)list.SelectedItems[0].Tag).Output);
        };
        list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && btnConvert.Enabled) btnRemove.PerformClick(); };
        list.Resize += delegate { list.Columns[0].Width = Math.Max(150, list.ClientSize.Width - 230 - 90 - 140 - 4); };
        dropHint = new Label { Text = "Arrastra aquí tus videos\no usa \"Agregar archivos\"", ForeColor = Muted, Font = new Font("Segoe UI", 12f), TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, BackColor = Color.White, AllowDrop = true };
        dropHint.DragEnter += (s, e) => { e.Effect = DragDropEffects.Copy; };
        dropHint.DragDrop += (s, e) => AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));
        dropHint.Click += delegate { AddDialog(); };
        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 6, 20, 6) };
        var listInner = new Panel { Dock = DockStyle.Fill };
        listInner.Controls.Add(dropHint);
        listInner.Controls.Add(list);
        listHost.Controls.Add(listInner);

        // Opciones
        var opts = new Panel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(20, 6, 20, 0) };
        opts.Controls.Add(new Label { Text = "Formato de salida", AutoSize = true, Location = new Point(20, 10), ForeColor = Muted });
        format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(20, 32), Width = 160 };
        format.Items.AddRange(new object[] { "MP4 (recomendado)", "MKV", "AVI", "MOV" });
        format.SelectedIndex = 0;
        opts.Controls.Add(format);
        opts.Controls.Add(new Label { Text = "Guardar en", AutoSize = true, Location = new Point(210, 10), ForeColor = Muted });
        sameFolder = new RadioButton { Text = "La misma carpeta que el original", AutoSize = true, Location = new Point(210, 32), Checked = true };
        otherFolder = new RadioButton { Text = "Otra carpeta:", AutoSize = true, Location = new Point(210, 58) };
        outFolder = new TextBox { Location = new Point(320, 57), Width = 360, Enabled = false, Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) };
        btnBrowse = new Button { Text = "Examinar...", Location = new Point(690, 55), Width = 100, Height = 27, Enabled = false };
        opts.Resize += delegate
        {
            int w = opts.ClientSize.Width;
            btnBrowse.Location = new Point(w - 120, 55);
            outFolder.Width = Math.Max(100, w - 120 - 10 - 320);
        };
        otherFolder.CheckedChanged += delegate { outFolder.Enabled = btnBrowse.Enabled = otherFolder.Checked; };
        btnBrowse.Click += delegate
        {
            using (var d = new FolderBrowserDialog { SelectedPath = outFolder.Text })
                if (d.ShowDialog(this) == DialogResult.OK) outFolder.Text = d.SelectedPath;
        };
        opts.Controls.AddRange(new Control[] { sameFolder, otherFolder, outFolder, btnBrowse });

        // Pie
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 96, BackColor = Color.FromArgb(249, 250, 251) };
        footer.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Color.FromArgb(229, 231, 235)), 0, 0, footer.Width, 0);
        progress = new ProgressBar { Location = new Point(20, 18), Height = 10, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, Maximum = 1000 };
        status = new Label { Location = new Point(20, 40), AutoSize = false, Height = 40, ForeColor = Muted, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right, AutoEllipsis = true };
        btnConvert = MakeButton("Convertir", true);
        btnConvert.Font = new Font("Segoe UI Semibold", 10.5f);
        btnConvert.Size = new Size(150, 40);
        btnConvert.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnConvert.Click += delegate { Start(); };
        btnCancel = MakeButton("Cancelar", false);
        btnCancel.Size = new Size(110, 40);
        btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnCancel.Visible = false;
        btnCancel.Click += delegate { cancel = true; KillCurrent(); status.Text = "Cancelando..."; };
        btnOpen = MakeButton("Abrir carpeta", false);
        btnOpen.Size = new Size(130, 40);
        btnOpen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnOpen.Visible = false;
        btnOpen.Click += delegate { if (lastOutput != null) ShowInExplorer(lastOutput); };
        footer.Controls.AddRange(new Control[] { progress, status, btnConvert, btnCancel, btnOpen });
        footer.Resize += delegate
        {
            int w = footer.ClientSize.Width;
            btnConvert.Location = new Point(w - 170, 28);
            btnCancel.Location = new Point(w - 290, 28);
            btnOpen.Location = new Point(w - 310, 28);
            progress.Width = w - 350;
            status.Width = w - 350;
        };

        Controls.Add(listHost);
        Controls.Add(opts);
        Controls.Add(footer);
        Controls.Add(toolbar);
        Controls.Add(header);

        FormClosing += (s, e) =>
        {
            if (!btnConvert.Enabled && btnCancel.Visible)
            {
                if (MessageBox.Show(this, "Hay una conversión en curso. ¿Cancelarla y salir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) { e.Cancel = true; return; }
                cancel = true;
                KillCurrent();
            }
        };

        UpdateUi();
        PrepareFfmpeg();
    }

    Button MakeButton(string text, bool primary)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 32, Padding = new Padding(10, 0, 10, 0), Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        if (primary)
        {
            b.BackColor = Accent; b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = AccentDark;
        }
        else
        {
            b.BackColor = Color.White; b.ForeColor = Color.FromArgb(31, 41, 55);
            b.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(243, 244, 246);
        }
        return b;
    }

    // ---------- ffmpeg incluido ----------

    void PrepareFfmpeg()
    {
        btnConvert.Enabled = false;
        status.Text = "Preparando el motor de conversión (solo la primera vez)...";
        new Thread(() =>
        {
            string err = null;
            try { ffmpeg = FindOrExtractFfmpeg(); }
            catch (Exception ex) { err = ex.Message; }
            BeginInvoke((Action)(() =>
            {
                if (ffmpeg == null)
                {
                    status.Text = "No se encontró el motor de conversión (ffmpeg).";
                    MessageBox.Show(this, "No se pudo preparar ffmpeg.\n" + err, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                status.Text = "Listo. Agrega videos para empezar.";
                UpdateUi();
            }));
        }) { IsBackground = true }.Start();
    }

    static string FindOrExtractFfmpeg()
    {
        string local = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ffmpeg.exe");
        if (File.Exists(local)) return local;

        var asm = Assembly.GetExecutingAssembly();
        using (var res = asm.GetManifestResourceStream("ffmpeg.exe.gz"))
        {
            if (res != null)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConvertidorVideo");
                // El nombre incluye el tamaño del recurso: si se actualiza el .exe, se vuelve a extraer.
                string target = Path.Combine(dir, "ffmpeg-" + res.Length + ".exe");
                if (File.Exists(target)) return target;
                Directory.CreateDirectory(dir);
                string tmp = target + ".tmp";
                using (var gz = new GZipStream(res, CompressionMode.Decompress))
                using (var f = File.Create(tmp)) gz.CopyTo(f);
                File.Move(tmp, target);
                foreach (var old in Directory.GetFiles(dir, "ffmpeg-*.exe"))
                    if (old != target) try { File.Delete(old); } catch { }
                return target;
            }
        }

        foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { var c = Path.Combine(p.Trim(), "ffmpeg.exe"); if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    // ---------- Lista ----------

    class Job { public string Source; public string Output; }

    void AddDialog()
    {
        if (!btnAdd.Enabled) return;
        using (var d = new OpenFileDialog { Multiselect = true, Title = "Selecciona los videos" })
        {
            d.Filter = "Videos|*" + string.Join(";*", Exts) + "|Todos los archivos|*.*";
            if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames);
        }
    }

    void AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ListViewItem it in list.Items) existing.Add(((Job)it.Tag).Source);
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var inner = new List<string>();
                foreach (var f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                    if (Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) >= 0) inner.Add(f);
                AddPaths(inner);
                foreach (ListViewItem it in list.Items) existing.Add(((Job)it.Tag).Source);
                continue;
            }
            if (!File.Exists(p) || existing.Contains(p)) continue;
            existing.Add(p);
            var item = new ListViewItem(new[] { Path.GetFileName(p), Path.GetDirectoryName(p), FormatSize(new FileInfo(p).Length), "Pendiente" });
            item.Tag = new Job { Source = p };
            item.UseItemStyleForSubItems = false;
            item.SubItems[1].ForeColor = Muted;
            item.SubItems[3].ForeColor = Muted;
            list.Items.Add(item);
        }
        UpdateUi();
    }

    void UpdateUi()
    {
        dropHint.Visible = list.Items.Count == 0;
        if (dropHint.Visible) dropHint.BringToFront(); else list.BringToFront();
        bool idle = !btnCancel.Visible;
        btnConvert.Enabled = idle && ffmpeg != null && list.Items.Count > 0;
        btnAdd.Enabled = btnRemove.Enabled = btnClear.Enabled = format.Enabled = idle;
        sameFolder.Enabled = otherFolder.Enabled = idle;
        outFolder.Enabled = btnBrowse.Enabled = idle && otherFolder.Checked;
        btnConvert.BackColor = btnConvert.Enabled ? Accent : Color.FromArgb(147, 197, 253);
        if (idle && ffmpeg != null && list.Items.Count > 0 && status.Text.StartsWith("Listo. Agrega"))
            status.Text = list.Items.Count + " archivo(s) en la lista.";
    }

    void SetState(ListViewItem item, string text, Color color)
    {
        item.SubItems[3].Text = text;
        item.SubItems[3].ForeColor = color;
    }

    // ---------- Conversión ----------

    void Start()
    {
        if (otherFolder.Checked && !Directory.Exists(outFolder.Text))
        {
            try { Directory.CreateDirectory(outFolder.Text); }
            catch { MessageBox.Show(this, "La carpeta de salida no es válida.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        var items = new List<ListViewItem>();
        foreach (ListViewItem it in list.Items)
        {
            var j = (Job)it.Tag;
            if (j.Output == null) { items.Add(it); SetState(it, "En espera", Muted); }
        }
        if (items.Count == 0)
        {
            status.Text = "Todos los archivos ya están convertidos. Agrega más o limpia la lista.";
            return;
        }

        string ext = new[] { ".mp4", ".mkv", ".avi", ".mov" }[format.SelectedIndex];
        string dir = otherFolder.Checked ? outFolder.Text : null;
        cancel = false;
        btnCancel.Visible = true;
        btnOpen.Visible = false;
        progress.Value = 0;
        UpdateUi();

        new Thread(() => RunAll(items, ext, dir)) { IsBackground = true }.Start();
    }

    void RunAll(List<ListViewItem> items, string ext, string dir)
    {
        int ok = 0, fail = 0;
        for (int i = 0; i < items.Count && !cancel; i++)
        {
            var item = items[i];
            var job = (Job)Invoke((Func<object>)(() => item.Tag));
            int idx = i;
            Ui(() =>
            {
                SetState(item, "Convirtiendo...", Accent);
                item.EnsureVisible();
                status.Text = string.Format("Convirtiendo {0} de {1}: {2}", idx + 1, items.Count, Path.GetFileName(job.Source));
            });

            string error;
            string output = Convert(job.Source, ext, dir, pct =>
            {
                Ui(() =>
                {
                    SetState(item, string.Format("Convirtiendo {0}%", (int)(pct * 100)), Accent);
                    progress.Value = Math.Min(1000, (int)((idx + pct) / items.Count * 1000));
                });
            }, out error);

            if (output != null)
            {
                ok++;
                job.Output = output;
                lastOutput = output;
                Ui(() => SetState(item, "✓ Completado", Ok));
            }
            else if (cancel)
            {
                Ui(() => SetState(item, "Cancelado", Muted));
            }
            else
            {
                fail++;
                Ui(() => { SetState(item, "✗ Error", Bad); item.ToolTipText = error; });
            }
        }

        Ui(() =>
        {
            foreach (var it in items) if (it.SubItems[3].Text == "En espera") SetState(it, "Pendiente", Muted);
            btnCancel.Visible = false;
            btnOpen.Visible = lastOutput != null;
            progress.Value = cancel ? progress.Value : 1000;
            string msg = cancel ? "Conversión cancelada." : "Conversión terminada.";
            msg += string.Format(" {0} correcto(s)", ok);
            if (fail > 0) msg += string.Format(", {0} con error (pasa el ratón sobre el archivo para ver el detalle)", fail);
            status.Text = msg + ". Doble clic en un archivo para verlo en su carpeta.";
            UpdateUi();
            if (!cancel) System.Media.SystemSounds.Asterisk.Play();
        });
    }

    string Convert(string src, string ext, string dir, Action<double> onProgress, out string error)
    {
        error = null;
        string folder = dir ?? Path.GetDirectoryName(src);
        string name = Path.GetFileNameWithoutExtension(src);
        string dst = Path.Combine(folder, name + ext);
        for (int n = 1; File.Exists(dst) || string.Equals(dst, src, StringComparison.OrdinalIgnoreCase); n++)
            dst = Path.Combine(folder, name + " (" + n + ")" + ext);

        // Analizar el archivo: duración y códec de video.
        string info = RunCapture("-hide_banner -i " + Q(src));
        double duration = 0;
        var m = Regex.Match(info, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
        if (m.Success)
            duration = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        var vc = Regex.Match(info, @"Video:\s*(\w+)");
        string vcodec = vc.Success ? vc.Groups[1].Value : "";
        if (vcodec == "")
        {
            error = "El archivo no contiene video reconocible o está dañado.";
            return null;
        }

        string acodec = ext == ".avi" ? "libmp3lame" : "aac";
        string tag = (vcodec == "hevc" && (ext == ".mp4" || ext == ".mov")) ? " -tag:v hvc1" : "";
        var attempts = new[]
        {
            // 1) Rápido y sin pérdida: copia el video tal cual.
            "-map 0:v:0 -map 0:a? -c:v copy" + tag + " -c:a " + acodec,
            // 2) Si el video no se puede copiar, se re-codifica a H.264.
            "-map 0:v:0 -map 0:a? -c:v libx264 -preset fast -crf 22 -pix_fmt yuv420p -c:a " + acodec
        };

        foreach (var a in attempts)
        {
            if (cancel) break;
            // AVI no admite bien H.265: se re-codifica directamente.
            if (ext == ".avi" && vcodec == "hevc" && a.Contains("-c:v copy")) continue;
            string extra = ext == ".mp4" || ext == ".mov" ? " -movflags +faststart" : "";
            string args = "-y -hide_banner -nostats -loglevel error -fflags +genpts -i " + Q(src) + " " + a + extra + " -progress pipe:1 " + Q(dst);
            if (RunWithProgress(args, duration, onProgress, out error) && File.Exists(dst) && new FileInfo(dst).Length > 0)
                return dst;
            TryDelete(dst);
        }
        TryDelete(dst);
        if (string.IsNullOrEmpty(error)) error = "No se pudo convertir este archivo.";
        return null;
    }

    bool RunWithProgress(string args, double duration, Action<double> onProgress, out string error)
    {
        var psi = new ProcessStartInfo(ffmpeg, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        var errText = new System.Text.StringBuilder();
        using (var p = new Process { StartInfo = psi })
        {
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errText) errText.AppendLine(e.Data); };
            p.Start();
            current = p;
            p.BeginErrorReadLine();
            string line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                if (duration > 0 && line.StartsWith("out_time_us="))
                {
                    long us;
                    if (long.TryParse(line.Substring(12), out us) && us > 0)
                        onProgress(Math.Min(1.0, us / 1e6 / duration));
                }
            }
            p.WaitForExit();
            current = null;
            lock (errText) error = errText.ToString().Trim();
            if (error.Length > 400) error = error.Substring(error.Length - 400);
            return p.ExitCode == 0 && !cancel;
        }
    }

    string RunCapture(string args)
    {
        var psi = new ProcessStartInfo(ffmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = true };
        using (var p = Process.Start(psi))
        {
            string s = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return s;
        }
    }

    void KillCurrent()
    {
        var p = current;
        try { if (p != null && !p.HasExited) p.Kill(); } catch { }
    }

    // ---------- Utilidades ----------

    void Ui(Action a)
    {
        if (IsDisposed) return;
        try { Invoke(a); } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }
    }

    static string Q(string s) { return "\"" + s + "\""; }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    static void ShowInExplorer(string file)
    {
        try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { }
    }

    static string FormatSize(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return v.ToString(i == 0 ? "0" : "0.0") + " " + u[i];
    }
}
