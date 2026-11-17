using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Pásalo")]
[assembly: AssemblyProduct("Pásalo")]
[assembly: AssemblyDescription("Convierte grabaciones de DVR y videos a formatos que se abren en cualquier lado")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

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

// Paleta del kit de marca "Pásalo".
static class Pal
{
    public static readonly Color Header = Hex("161826"), DarkSurface = Hex("232532"), Line = Hex("9184D9");
    public static readonly Color Accent = Hex("5D5294"), AccentHover = Hex("796CBF"), AccentPressed = Hex("423A6A");
    public static readonly Color Select = Hex("E7E5FE"), SoftAccent = Hex("F5F4FF"), DropBorder = Hex("B5ABFC");
    public static readonly Color Text = Hex("292B31"), Text2 = Hex("595D6C"), Muted = Hex("75798C"), Faint = Hex("9397AB");
    public static readonly Color Border = Hex("CFD3E5"), Divider = Hex("E4E7F5"), Soft = Hex("F3F5FE"), InputBorder = Hex("B2B6CA");
    public static readonly Color Ok = Hex("2F7D4F"), Bad = Hex("B83A44"), HeaderText = Hex("E9E9ED"), HeaderSub = Hex("B2B6CA");
    public static Color Hex(string h) { return ColorTranslator.FromHtml("#" + h); }
}

// Icono: un bloque (el archivo) que pasa por una flecha.
public static class Logo
{
    public static Bitmap Render(int size, Color bg, bool outline)
    {
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (size <= 16) DrawPixel(g); else Draw(g, size, bg, outline);
        }
        return bmp;
    }

    public static Bitmap Render(int size) { return Render(size, Pal.Header, size >= 128); }

    static void Draw(Graphics g, int size, Color bg, bool outline)
    {
        g.ScaleTransform(size / 256f, size / 256f);
        using (var path = Rounded(8, 8, 240, 240, 52))
        {
            using (var b = new SolidBrush(bg)) g.FillPath(b, path);
        }
        if (outline)
            using (var path = Rounded(8.5f, 8.5f, 239, 239, 51.5f))
            using (var p = new Pen(Pal.Hex("423A6A"), 1)) g.DrawPath(p, path);
        bool small = size <= 32;
        using (var path = small ? Rounded(48, 96, 64, 64, 10) : Rounded(52, 100, 56, 56, 10))
        using (var b = new SolidBrush(Pal.HeaderText)) g.FillPath(b, path);
        using (var p = new Pen(Pal.Line, small ? 40 : 34) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(p, new[] { new PointF(132, 68), new PointF(192, 128), new PointF(132, 188) });
    }

    // Versión de 16 px dibujada aparte, alineada a píxel.
    static void DrawPixel(Graphics g)
    {
        using (var path = Rounded(0, 0, 16, 16, 3))
        using (var b = new SolidBrush(Pal.Header)) g.FillPath(b, path);
        g.SmoothingMode = SmoothingMode.None;
        using (var b = new SolidBrush(Pal.HeaderText)) g.FillRectangle(b, 3, 6, 4, 4);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var p = new Pen(Pal.Line, 2.2f) { StartCap = LineCap.Square, EndCap = LineCap.Square, LineJoin = LineJoin.Miter })
            g.DrawLines(p, new[] { new PointF(8.5f, 4), new PointF(12.5f, 8), new PointF(8.5f, 12) });
    }

    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

class MainForm : Form
{
    static readonly string[] Exts = { ".dav", ".avi", ".mkv", ".mov", ".mp4", ".wmv", ".flv", ".ts", ".mts", ".m2ts", ".h264", ".264", ".h265", ".265", ".mpg", ".mpeg", ".3gp", ".webm", ".asf", ".vob" };
    enum Kind { Secondary, Outline, Primary }

    DataGridView grid;
    Panel dropZone, progressTrack, progressFill;
    Label status, countLabel;
    ComboBox format;
    RadioButton sameFolder, otherFolder;
    TextBox outFolder;
    Button btnBrowse, btnAdd, btnRemove, btnClear, btnConvert, btnCancel, btnOpen;
    Font fontSemi, fontSmall, fontSmallSemi;
    double progressValue;

    string ffmpeg;
    volatile bool cancel;
    volatile bool busy;
    Process current;
    string lastOutput;

    public MainForm()
    {
        Text = "Pásalo";
        Font = new Font("Segoe UI", 9.75f);
        fontSemi = new Font("Segoe UI Semibold", 9.75f);
        fontSmall = new Font("Segoe UI", 9f);
        fontSmallSemi = new Font("Segoe UI Semibold", 9f);
        BackColor = Color.White;
        ForeColor = Pal.Text;
        ClientSize = new Size(880, 600);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // ---------- Cabecera: 64 px, fondo oscuro, línea inferior 2 px ----------
        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Pal.Header };
        var headerLogo = Logo.Render(32, Pal.DarkSurface, false);
        var titleFont = new Font("Segoe UI Semibold", 14f);
        header.Paint += (s, e) =>
        {
            e.Graphics.DrawImage(headerLogo, 24, 16, 32, 32);
            TextRenderer.DrawText(e.Graphics, "Pásalo", titleFont, new Point(66, 7), Pal.HeaderText);
            TextRenderer.DrawText(e.Graphics, "Convierte grabaciones de DVR y videos a formatos que se abren en cualquier lado", fontSmall, new Point(68, 35), Pal.HeaderSub);
            using (var b = new SolidBrush(Pal.Line)) e.Graphics.FillRectangle(b, 0, header.Height - 2, header.Width, 2);
        };

        // ---------- Barra de botones: 52 px ----------
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(0, 0, 24, 0) };
        btnAdd = MakeButton("+  Agregar archivos", Kind.Outline, 32);
        btnRemove = MakeButton("Quitar seleccionados", Kind.Secondary, 32);
        btnClear = MakeButton("Limpiar lista", Kind.Secondary, 32);
        int x = 24;
        foreach (var b in new[] { btnAdd, btnRemove, btnClear }) { b.Location = new Point(x, 10); x += b.Width + 8; }
        btnAdd.Click += delegate { AddDialog(); };
        btnRemove.Click += delegate { RemoveSelected(); };
        btnClear.Click += delegate { grid.Rows.Clear(); UpdateUi(); };
        countLabel = new Label { Dock = DockStyle.Right, Width = 220, TextAlign = ContentAlignment.MiddleRight, Font = fontSmall, ForeColor = Pal.Text2 };
        toolbar.Controls.AddRange(new Control[] { btnAdd, btnRemove, btnClear, countLabel });

        // ---------- Lista ----------
        grid = new DataGridView
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackgroundColor = Color.White,
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Pal.Divider,
            EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 28,
            AllowDrop = true, ShowCellToolTips = true, StandardTab = true
        };
        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Pal.Soft, ForeColor = Pal.Text2, SelectionBackColor = Pal.Soft, SelectionForeColor = Pal.Text2,
            Font = fontSmall, Padding = new Padding(6, 0, 6, 0)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White, ForeColor = Pal.Text, SelectionBackColor = Pal.Select, SelectionForeColor = Pal.Text,
            Font = Font, Padding = new Padding(6, 0, 6, 0), WrapMode = DataGridViewTriState.False
        };
        AddColumn("Archivo", 240, DataGridViewAutoSizeColumnMode.None, DataGridViewContentAlignment.MiddleLeft);
        AddColumn("Carpeta", 100, DataGridViewAutoSizeColumnMode.Fill, DataGridViewContentAlignment.MiddleLeft);
        AddColumn("Tamaño", 90, DataGridViewAutoSizeColumnMode.None, DataGridViewContentAlignment.MiddleRight);
        AddColumn("Estado", 150, DataGridViewAutoSizeColumnMode.None, DataGridViewContentAlignment.MiddleLeft);
        grid.Columns[1].DefaultCellStyle.ForeColor = Pal.Muted;
        grid.Columns[1].DefaultCellStyle.SelectionForeColor = Pal.Muted;
        grid.Columns[2].DefaultCellStyle.ForeColor = Pal.Text2;
        grid.Columns[2].DefaultCellStyle.SelectionForeColor = Pal.Text2;
        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(grid, true, null);
        grid.DragEnter += OnDragEnter;
        grid.DragDrop += OnDragDrop;
        grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var job = (Job)grid.Rows[e.RowIndex].Tag;
            if (job.Output != null) ShowInExplorer(job.Output);
        };
        grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; } };

        dropZone = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AllowDrop = true, Cursor = Cursors.Hand };
        typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dropZone, true, null);
        var bigFont = new Font("Segoe UI", 12f);
        var linkFont = new Font("Segoe UI", 9.75f, FontStyle.Underline);
        dropZone.Paint += (s, e) => PaintDropZone(e.Graphics, bigFont, linkFont);
        dropZone.Resize += delegate { dropZone.Invalidate(); };
        dropZone.Click += delegate { AddDialog(); };
        dropZone.DragEnter += OnDragEnter;
        dropZone.DragDrop += OnDragDrop;

        var listBorder = new Panel { Dock = DockStyle.Fill, BackColor = Pal.Border, Padding = new Padding(1) };
        listBorder.Controls.Add(dropZone);
        listBorder.Controls.Add(grid);
        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 0) };
        listHost.Controls.Add(listBorder);

        // ---------- Opciones: 76 px ----------
        var opts = new Panel { Dock = DockStyle.Bottom, Height = 76 };
        var lblFormat = new Label { Text = "Formato de salida", AutoSize = true, Location = new Point(22, 12), Font = fontSmall, ForeColor = Pal.Text2 };
        format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(24, 33), Width = 180, FlatStyle = FlatStyle.System };
        format.Items.AddRange(new object[] { "MP4 (recomendado)", "MKV", "AVI", "MOV" });
        format.SelectedIndex = 0;
        var lblSave = new Label { Text = "Guardar en", AutoSize = true, Location = new Point(234, 12), Font = fontSmall, ForeColor = Pal.Text2 };
        sameFolder = new RadioButton { Text = "La misma carpeta que el original", AutoSize = true, Location = new Point(236, 35), Checked = true };
        otherFolder = new RadioButton { Text = "Otra carpeta:", AutoSize = true, Location = new Point(480, 35) };
        outFolder = new TextBox { Location = new Point(600, 34), Width = 150, BorderStyle = BorderStyle.FixedSingle, Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) };
        btnBrowse = MakeButton("Examinar...", Kind.Secondary, 26);
        otherFolder.CheckedChanged += delegate { UpdateUi(); };
        btnBrowse.Click += delegate
        {
            using (var d = new FolderBrowserDialog { SelectedPath = outFolder.Text })
                if (d.ShowDialog(this) == DialogResult.OK) outFolder.Text = d.SelectedPath;
        };
        opts.Controls.AddRange(new Control[] { lblFormat, format, lblSave, sameFolder, otherFolder, outFolder, btnBrowse });
        opts.Resize += delegate
        {
            int w = opts.ClientSize.Width;
            otherFolder.Left = sameFolder.Left + sameFolder.PreferredSize.Width + 20;
            outFolder.Left = otherFolder.Left + otherFolder.PreferredSize.Width + 6;
            btnBrowse.Location = new Point(w - 24 - btnBrowse.Width, 33);
            outFolder.Width = Math.Max(80, btnBrowse.Left - 8 - outFolder.Left);
        };

        // ---------- Pie: 72 px ----------
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Pal.Soft };
        footer.Paint += (s, e) => { using (var p = new Pen(Pal.Divider)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
        progressTrack = new Panel { Location = new Point(24, 23), Height = 6, BackColor = Pal.Divider };
        progressFill = new Panel { Location = Point.Empty, Height = 6, Width = 0, BackColor = Pal.AccentHover };
        progressTrack.Controls.Add(progressFill);
        progressTrack.Resize += delegate { SetProgress(progressValue, progressFill.BackColor); };
        status = new Label { Location = new Point(22, 37), Height = 18, AutoSize = false, AutoEllipsis = true, Font = fontSmall, ForeColor = Pal.Text2 };
        btnConvert = MakeButton("Convertir", Kind.Primary, 40);
        btnConvert.Font = new Font("Segoe UI Semibold", 11f);
        btnConvert.Width = 150;
        btnConvert.Click += delegate { Start(); };
        btnCancel = MakeButton("Cancelar", Kind.Secondary, 40);
        btnCancel.Width += 12;
        btnCancel.Visible = false;
        btnCancel.Click += delegate { cancel = true; KillCurrent(); status.Text = "Cancelando..."; };
        btnOpen = MakeButton("Abrir carpeta", Kind.Outline, 40);
        btnOpen.Width += 12;
        btnOpen.Visible = false;
        btnOpen.Click += delegate { if (lastOutput != null) ShowInExplorer(lastOutput); };
        footer.Controls.AddRange(new Control[] { progressTrack, status, btnConvert, btnCancel, btnOpen });
        footer.Resize += delegate { LayoutFooter(footer); };
        btnCancel.VisibleChanged += delegate { LayoutFooter(footer); };
        btnOpen.VisibleChanged += delegate { LayoutFooter(footer); };

        Controls.Add(listHost);
        Controls.Add(opts);
        Controls.Add(footer);
        Controls.Add(toolbar);
        Controls.Add(header);

        FormClosing += (s, e) =>
        {
            if (!busy) return;
            if (MessageBox.Show(this, "Hay una conversión en curso. ¿Cancelarla y salir?", "Pásalo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No) { e.Cancel = true; return; }
            cancel = true;
            KillCurrent();
        };

        Shown += delegate { ActiveControl = btnAdd; };
        UpdateUi();
        PrepareFfmpeg();
    }

    void AddColumn(string name, int width, DataGridViewAutoSizeColumnMode mode, DataGridViewContentAlignment align)
    {
        var c = new DataGridViewTextBoxColumn { HeaderText = name, Width = width, AutoSizeMode = mode, SortMode = DataGridViewColumnSortMode.NotSortable, Resizable = DataGridViewTriState.False };
        c.DefaultCellStyle.Alignment = align;
        c.HeaderCell.Style.Alignment = align;
        grid.Columns.Add(c);
    }

    void LayoutFooter(Panel footer)
    {
        int w = footer.ClientSize.Width;
        btnConvert.Location = new Point(w - 24 - btnConvert.Width, 16);
        int right = btnConvert.Left;
        foreach (var b in new[] { btnCancel, btnOpen })
            if (b.Visible) { b.Location = new Point(right - 24 - b.Width, 16); right = b.Left; }
        progressTrack.Width = Math.Max(50, right - 24 - 24);
        status.Width = progressTrack.Width + 2;
    }

    void SetProgress(double value, Color color)
    {
        progressValue = Math.Max(0, Math.Min(1, value));
        progressFill.BackColor = color;
        progressFill.Width = (int)(progressTrack.Width * progressValue);
    }

    Button MakeButton(string text, Kind kind, int height)
    {
        var b = new Button { Text = text, Height = height, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false, TabStop = true };
        b.Font = kind == Kind.Outline ? fontSemi : Font;
        b.Width = TextRenderer.MeasureText(text, b.Font).Width + (height >= 40 ? 36 : 26);
        b.FlatAppearance.BorderSize = kind == Kind.Primary ? 0 : 1;
        Color disBg, disFg, disBorder;
        if (kind == Kind.Primary)
        {
            b.BackColor = Pal.Accent; b.ForeColor = Pal.Soft;
            b.FlatAppearance.MouseOverBackColor = Pal.AccentHover;
            b.FlatAppearance.MouseDownBackColor = Pal.AccentPressed;
            disBg = Pal.Divider; disFg = Pal.Faint; disBorder = Pal.Divider;
        }
        else if (kind == Kind.Outline)
        {
            b.BackColor = Color.White; b.ForeColor = Pal.Accent;
            b.FlatAppearance.BorderColor = Pal.Accent;
            b.FlatAppearance.MouseOverBackColor = Pal.SoftAccent;
            b.FlatAppearance.MouseDownBackColor = Pal.Select;
            disBg = Color.White; disFg = Pal.Hex("B3AECF"); disBorder = Pal.Hex("BEB9D9");
        }
        else
        {
            b.BackColor = Color.White; b.ForeColor = Pal.Text;
            b.FlatAppearance.BorderColor = Pal.Border;
            b.FlatAppearance.MouseOverBackColor = Pal.Soft;
            b.FlatAppearance.MouseDownBackColor = Pal.Divider;
            disBg = Color.White; disFg = Pal.Faint; disBorder = Pal.Divider;
        }
        // Estado desactivado con los colores del diseño (WinForms usaría el gris del sistema).
        b.Paint += (s, e) =>
        {
            if (b.Enabled) return;
            e.Graphics.Clear(disBg);
            using (var p = new Pen(disBorder)) e.Graphics.DrawRectangle(p, 0, 0, b.Width - 1, b.Height - 1);
            TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, disFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        };
        return b;
    }

    void PaintDropZone(Graphics g, Font bigFont, Font linkFont)
    {
        var r = new Rectangle(12, 12, dropZone.Width - 25, dropZone.Height - 25);
        if (r.Width < 10 || r.Height < 10) return;
        using (var b = new SolidBrush(Pal.SoftAccent)) g.FillRectangle(b, r);
        using (var p = new Pen(Pal.DropBorder) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, r);

        int total = 56 + 10 + 22 + 10 + 18 + 10 + 16;
        int top = r.Top + (r.Height - total) / 2;
        int cx = r.Left + r.Width / 2;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var ix = cx - 28;
        using (var p = new Pen(Pal.AccentHover, 2)) g.DrawRectangle(p, ix + 6, top + 20, 44, 30);
        using (var p = new Pen(Pal.AccentHover, 2.5f) { StartCap = LineCap.Square, EndCap = LineCap.Square })
        {
            g.DrawLine(p, ix + 28, top + 4, ix + 28, top + 32);
            g.DrawLines(p, new[] { new Point(ix + 18, top + 22), new Point(ix + 28, top + 32), new Point(ix + 38, top + 22) });
        }
        g.SmoothingMode = SmoothingMode.Default;
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine;
        int y = top + 56 + 10;
        TextRenderer.DrawText(g, "Arrastra aquí tus videos o carpetas", bigFont, new Rectangle(r.Left, y, r.Width, 24), Pal.Text, flags);
        y += 22 + 10;
        TextRenderer.DrawText(g, "o haz clic para elegir archivos", linkFont, new Rectangle(r.Left, y, r.Width, 20), Pal.Accent, flags);
        y += 18 + 10;
        TextRenderer.DrawText(g, ".dav · .mp4 · .mkv · .avi · .mov y más", fontSmall, new Rectangle(r.Left, y, r.Width, 18), Pal.Muted, flags);
    }

    // ---------- ffmpeg incluido ----------

    void PrepareFfmpeg()
    {
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
                    MessageBox.Show(this, "No se pudo preparar ffmpeg.\n" + err, "Pásalo", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pasalo");
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

    class Job { public string Source; public string Output; public long Size; }

    void OnDragEnter(object s, DragEventArgs e)
    {
        e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Copy : DragDropEffects.None;
    }

    void OnDragDrop(object s, DragEventArgs e)
    {
        if (!busy) AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));
    }

    void AddDialog()
    {
        if (busy) return;
        using (var d = new OpenFileDialog { Multiselect = true, Title = "Selecciona los videos" })
        {
            d.Filter = "Videos|*" + string.Join(";*", Exts) + "|Todos los archivos|*.*";
            if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames);
        }
    }

    void AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow r in grid.Rows) existing.Add(((Job)r.Tag).Source);
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                try
                {
                    foreach (var f in Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                        if (Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) >= 0) files.Add(f);
                }
                catch { }
            }
            else files.Add(p);
        }
        foreach (var p in files)
        {
            if (!File.Exists(p) || existing.Contains(p)) continue;
            existing.Add(p);
            var job = new Job { Source = p, Size = new FileInfo(p).Length };
            int i = grid.Rows.Add(Path.GetFileName(p), Path.GetDirectoryName(p), FormatSize(job.Size), "");
            grid.Rows[i].Tag = job;
            SetState(grid.Rows[i], "Pendiente", Pal.Muted, false);
        }
        grid.ClearSelection();
        UpdateUi();
    }

    void RemoveSelected()
    {
        if (busy) return;
        foreach (DataGridViewRow r in grid.SelectedRows) grid.Rows.Remove(r);
        grid.ClearSelection();
        UpdateUi();
    }

    void UpdateUi()
    {
        int n = grid.Rows.Count;
        dropZone.Visible = n == 0;
        grid.Visible = n > 0;

        long total = 0;
        foreach (DataGridViewRow r in grid.Rows) total += ((Job)r.Tag).Size;
        countLabel.Text = n == 0 ? "" : (n == 1 ? "1 archivo" : n + " archivos") + " · " + FormatSize(total);

        bool idle = !busy;
        btnConvert.Enabled = idle && ffmpeg != null && n > 0;
        btnAdd.Enabled = idle;
        btnRemove.Enabled = btnClear.Enabled = idle && n > 0;
        format.Enabled = sameFolder.Enabled = otherFolder.Enabled = idle;
        outFolder.Enabled = btnBrowse.Enabled = idle && otherFolder.Checked;
        if (idle && ffmpeg != null && (status.Text.StartsWith("Listo.") || status.Text.EndsWith("en la lista.")))
            status.Text = n == 0 ? "Listo. Agrega videos para empezar." : n + " archivo(s) en la lista.";
        foreach (var b in new[] { btnConvert, btnAdd, btnRemove, btnClear, btnBrowse }) b.Invalidate();
    }

    void SetState(DataGridViewRow row, string text, Color color, bool bold)
    {
        var cell = row.Cells[3];
        cell.Value = text;
        cell.Style.ForeColor = color;
        cell.Style.SelectionForeColor = color;
        cell.Style.Font = bold ? fontSemi : null;
    }

    // ---------- Conversión ----------

    void Start()
    {
        if (otherFolder.Checked && !Directory.Exists(outFolder.Text))
        {
            try { Directory.CreateDirectory(outFolder.Text); }
            catch { MessageBox.Show(this, "La carpeta de salida no es válida.", "Pásalo", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        var rows = new List<DataGridViewRow>();
        foreach (DataGridViewRow r in grid.Rows)
        {
            if (((Job)r.Tag).Output == null) { rows.Add(r); SetState(r, "En espera", Pal.Muted, false); }
            foreach (DataGridViewCell c in r.Cells) c.ToolTipText = "";
        }
        if (rows.Count == 0)
        {
            status.Text = "Todos los archivos ya están convertidos. Agrega más o limpia la lista.";
            return;
        }

        string ext = new[] { ".mp4", ".mkv", ".avi", ".mov" }[format.SelectedIndex];
        string dir = otherFolder.Checked ? outFolder.Text : null;
        cancel = false;
        busy = true;
        btnCancel.Visible = true;
        btnOpen.Visible = false;
        status.ForeColor = Pal.Text2;
        SetProgress(0, Pal.AccentHover);
        grid.ClearSelection();
        UpdateUi();

        var jobs = new List<Job>();
        foreach (var r in rows) jobs.Add((Job)r.Tag);
        new Thread(() => RunAll(rows, jobs, ext, dir)) { IsBackground = true }.Start();
    }

    void RunAll(List<DataGridViewRow> rows, List<Job> jobs, string ext, string dir)
    {
        int ok = 0, fail = 0;
        for (int i = 0; i < rows.Count && !cancel; i++)
        {
            var row = rows[i];
            var job = jobs[i];
            int idx = i;
            Ui(() =>
            {
                row.DefaultCellStyle.BackColor = Pal.SoftAccent;
                SetState(row, "Convirtiendo...", Pal.Accent, true);
                if (row.Index >= 0 && !row.Displayed) grid.FirstDisplayedScrollingRowIndex = row.Index;
                status.Text = string.Format("Convirtiendo {0} de {1}: {2}", idx + 1, rows.Count, Path.GetFileName(job.Source));
            });

            string error;
            int lastPct = -1;
            string output = Convert(job.Source, ext, dir, pct =>
            {
                int p = (int)(pct * 100);
                if (p == lastPct) return;
                lastPct = p;
                Ui(() =>
                {
                    SetState(row, "Convirtiendo " + p + "%", Pal.Accent, true);
                    SetProgress((idx + pct) / rows.Count, Pal.AccentHover);
                });
            }, out error);

            Ui(() => row.DefaultCellStyle.BackColor = Color.White);
            if (output != null)
            {
                ok++;
                job.Output = output;
                lastOutput = output;
                Ui(() => SetState(row, "✓ Completado", Pal.Ok, true));
            }
            else if (cancel)
            {
                Ui(() => SetState(row, "Cancelado", Pal.Muted, false));
            }
            else
            {
                fail++;
                string msg = error;
                Ui(() =>
                {
                    SetState(row, "✗ Error", Pal.Bad, true);
                    foreach (DataGridViewCell c in row.Cells) c.ToolTipText = msg;
                });
            }
        }

        Ui(() =>
        {
            foreach (var r in rows) if ((string)r.Cells[3].Value == "En espera") SetState(r, "Pendiente", Pal.Muted, false);
            busy = false;
            btnCancel.Visible = false;
            btnOpen.Visible = lastOutput != null;
            if (!cancel) SetProgress(1, Pal.Ok);
            string msg = cancel ? "Conversión cancelada." : "Conversión terminada.";
            msg += string.Format(" {0} correcto(s)", ok);
            if (fail > 0) msg += string.Format(", {0} con error (pasa el ratón sobre el archivo para ver el detalle)", fail);
            status.Text = msg + ". Doble clic en un archivo para verlo en su carpeta.";
            status.ForeColor = Pal.Text;
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
        return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + u[i];
    }
}
