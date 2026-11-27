using OpenCvSharp;

namespace FaceRecognition;

public sealed class MainForm : Form
{
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(24, 33, 45) };
    private readonly ListBox results = new() { Dock = DockStyle.Fill, AccessibleName = "Yüz sonuçları" };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Hazırlanıyor…" };
    private readonly Label dataset = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly TextBox label = new() { Width = 150, MaxLength = 40, PlaceholderText = "Örn. Ornek-1", AccessibleName = "Örnek etiketi" };
    private readonly NumericUpDown cameraIndex = new() { Width = 45, Maximum = 9, AccessibleName = "Kamera numarası" };
    private readonly NumericUpDown threshold = new() { Width = 60, Minimum = 1, Maximum = 200, Value = 65, AccessibleName = "Tanıma eşiği" };
    private readonly Button start = Button("Kamera Başlat");
    private readonly Button stop = Button("Durdur");
    private readonly Button open = Button("Görüntü Seç");
    private readonly Button add = Button("Örnek Ekle");
    private readonly Button train = Button("Modeli Eğit");
    private readonly Button demo = Button("Sentetik Demo");
    private readonly Button local = Button("Yerel Örnekler");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 150 };
    private FaceEngine? engine;
    private VideoCapture? camera;
    private Mat? current;
    private FaceResult[] faces = [];
    private bool demoMode;
    private int failedFrames;
    private readonly string assets = Path.Combine(AppContext.BaseDirectory, "Assets");
    private readonly string dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YMH219-FaceRecognition");

    public MainForm()
    {
        Text = "YMH219 — Yüz Algılama ve Tanıma";
        ClientSize = new System.Drawing.Size(1120, 780);
        MinimumSize = new System.Drawing.Size(1000, 740);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
        foreach (int height in new[] { 50, 46, 46, 42, -1, 110, 46 })
            layout.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute, height < 0 ? 100 : height));
        layout.Controls.Add(new Label { Text = "Yüz Algılama ve Tanıma", AutoSize = true, Font = new Font("Segoe UI", 23, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(Bar(new Label { Text = "Kamera", AutoSize = true }, cameraIndex, start, stop, open, demo, local), 0, 1);
        layout.Controls.Add(Bar(label, add, train, new Label { Text = "LBPH eşiği", AutoSize = true }, threshold), 0, 2);
        layout.Controls.Add(dataset, 0, 3);
        layout.Controls.Add(preview, 0, 4);
        layout.Controls.Add(results, 0, 5);
        layout.Controls.Add(status, 0, 6);
        Controls.Add(layout);
        Shown += (_, _) => Run(() => SwitchEngine(false));
        start.Click += (_, _) => Run(StartCamera);
        stop.Click += (_, _) => { StopCamera(); status.Text = "Kamera kapalı."; };
        open.Click += (_, _) => Run(OpenImage);
        demo.Click += (_, _) => Run(LoadDemo);
        local.Click += (_, _) => Run(() => SwitchEngine(false));
        add.Click += (_, _) => Run(AddSample);
        train.Click += (_, _) => Run(() => { engine!.Train(); Render(); status.Text = "Model eğitildi. Daha önce eklenmemiş bir görüntüyle deneyebilirsiniz."; });
        threshold.ValueChanged += (_, _) => Run(() => { if (engine != null) { engine.Threshold = (double)threshold.Value; Render(); } });
        timer.Tick += (_, _) => Run(ReadFrame);
        RefreshControls();
    }

    private static Button Button(string text) => new() { Text = text, AutoSize = true, MinimumSize = new System.Drawing.Size(95, 32) };
    private static FlowLayoutPanel Bar(params Control[] controls)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        bar.Controls.AddRange(controls);
        return bar;
    }

    private void SwitchEngine(bool isDemo)
    {
        StopCamera();
        var next = new FaceEngine(Path.Combine(assets, "haarcascade_frontalface_default.xml"), Path.Combine(dataRoot, isDemo ? "demo" : "samples"));
        engine?.Dispose();
        engine = next;
        engine.Threshold = (double)threshold.Value;
        demoMode = isDemo;
        Render();
        status.Text = isDemo ? "Sentetik örnek kümesi açık." : "Yerel örnekler açık. Kamera kapalı; yalnızca kendi veya izinli örneklerinizi ekleyin.";
    }

    private void LoadDemo()
    {
        SwitchEngine(true);
        using var image = ReadImage(Path.Combine(assets, "synthetic-demo.png"));
        var detected = engine!.Detect(image);
        if (detected.Length != 2) throw new InvalidDataException("Demo görüntüsünde iki yüz algılanamadı.");
        if (engine.SampleCount == 0)
        {
            engine.Add(image, detected[0], "Sentetik-A");
            engine.Add(image, detected[1], "Sentetik-B");
        }
        SetFrame(image.Clone());
        status.Text = "Sentetik demo: iki örnek hazır. Modeli Eğit'e basın. Bu bir doğruluk ölçümü değildir.";
    }

    private static Mat ReadImage(string path)
    {
        if (new FileInfo(path).Length > 30 * 1024 * 1024) throw new InvalidDataException("Görüntü 30 MB sınırını aşıyor.");
        var image = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Color);
        if (image.Empty()) { image.Dispose(); throw new InvalidDataException("Görüntü dosyası okunamadı."); }
        return image;
    }

    private void OpenImage()
    {
        using var dialog = new OpenFileDialog { Filter = "Görüntüler|*.png;*.jpg;*.jpeg;*.bmp", Title = "Yerel görüntü seçin" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        StopCamera();
        SetFrame(ReadImage(dialog.FileName));
        status.Text = $"Görüntü açıldı. {faces.Length} yüz algılandı. Kamera kapalı.";
    }

    private void StartCamera()
    {
        StopCamera();
        var next = new VideoCapture();
        try
        {
            if (!next.Open((int)cameraIndex.Value, VideoCaptureAPIs.DSHOW))
                throw new InvalidOperationException("Kamera açılamadı. Bağlantıyı, kamera numarasını ve Windows kamera izinlerini kontrol edin.");
            next.Set(VideoCaptureProperties.FrameWidth, 640);
            next.Set(VideoCaptureProperties.FrameHeight, 480);
            camera = next;
            failedFrames = 0;
            timer.Start();
            status.Text = "Kamera açık. Görüntüler otomatik olarak kaydedilmez.";
        }
        catch { next.Dispose(); throw; }
    }

    private void ReadFrame()
    {
        if (camera == null) return;
        var frame = new Mat();
        if (!camera.Read(frame) || frame.Empty())
        {
            frame.Dispose();
            if (++failedFrames >= 10) throw new IOException("Kameradan görüntü alınamıyor. Kamera durduruldu.");
            return;
        }
        failedFrames = 0;
        SetFrame(frame);
    }

    private void StopCamera()
    {
        timer.Stop();
        camera?.Dispose();
        camera = null;
        RefreshControls();
    }

    private void SetFrame(Mat frame)
    {
        current?.Dispose();
        current = frame;
        Render();
    }

    private void Render()
    {
        if (current == null || engine == null) return;
        faces = engine.Analyze(current);
        using var annotated = current.Clone();
        int selection = results.SelectedIndex;
        results.Items.Clear();
        for (int i = 0; i < faces.Length; i++)
        {
            var face = faces[i];
            Cv2.Rectangle(annotated, face.Bounds, Scalar.LimeGreen, 3);
            Cv2.PutText(annotated, $"#{i + 1}", new OpenCvSharp.Point(face.Bounds.X, Math.Max(25, face.Bounds.Y - 8)), HersheyFonts.HersheySimplex, 0.8, Scalar.LimeGreen, 2);
            results.Items.Add($"Yüz {i + 1} — {face.Label}" + (face.Distance is double d ? $" | LBPH uzaklığı: {d:F2} (düşük değer daha yakın)" : " | Model eğitilmedi"));
        }
        using var stream = new MemoryStream(annotated.ToBytes(".bmp"));
        using var bitmap = new Bitmap(stream);
        var old = preview.Image;
        preview.Image = new Bitmap(bitmap);
        old?.Dispose();
        if (faces.Length > 0) results.SelectedIndex = Math.Clamp(selection, 0, faces.Length - 1);
        RefreshControls();
    }

    private void AddSample()
    {
        int selected = results.SelectedIndex;
        if (current == null || selected < 0 || selected >= faces.Length) throw new InvalidOperationException("Listeden bir yüz seçin.");
        StopCamera();
        engine!.Add(current, faces[selected].Bounds, label.Text);
        Render();
        status.Text = "Seçilen yüz yerel örneklere kaydedildi. Modeli yeniden eğitin.";
    }

    private void RefreshControls()
    {
        bool ready = engine != null;
        start.Enabled = ready && camera == null;
        stop.Enabled = camera != null;
        cameraIndex.Enabled = camera == null;
        open.Enabled = demo.Enabled = ready;
        local.Enabled = true;
        add.Enabled = ready && faces.Length > 0 && !demoMode;
        train.Enabled = ready && engine!.LabelCount >= 2 && camera == null;
        dataset.Text = ready ? $"{(demoMode ? "SENTETİK DEMO" : "YEREL ÖRNEKLER")} | {engine!.LabelCount} etiket, {engine.SampleCount} örnek | {(engine.IsTrained ? "Model hazır" : "Eğitim bekleniyor")}\n{engine.Summary}" : "Model yüklenemedi. Yerel Örnekler ile tekrar deneyin.";
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is OpenCVException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or TypeInitializationException or DllNotFoundException)
        {
            StopCamera();
            status.Text = "İşlem yapılamadı: " + ex.Message;
            MessageBox.Show(this, ex.Message, "İşlem yapılamadı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { RefreshControls(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop(); timer.Dispose(); camera?.Dispose(); current?.Dispose(); engine?.Dispose(); preview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
