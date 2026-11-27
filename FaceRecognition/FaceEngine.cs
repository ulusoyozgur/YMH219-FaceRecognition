using OpenCvSharp;
using OpenCvSharp.Face;
using System.Text.Json;

namespace FaceRecognition;

public sealed record FaceResult(Rect Bounds, string Label, double? Distance);
public sealed record Sample(string Label, string File);

public sealed class FaceEngine : IDisposable
{
    private readonly CascadeClassifier detector;
    private LBPHFaceRecognizer? recognizer;
    private string[] labels = [];
    private readonly List<Sample> samples = [];
    private readonly string folder;
    public bool IsTrained => recognizer != null;
    public int SampleCount => samples.Count;
    public int LabelCount => samples.Select(s => s.Label).Distinct().Count();
    public double Threshold { get; set; } = 65;
    public string Summary => string.Join(" | ", samples.GroupBy(s => s.Label).Select(g => $"{g.Key}: {g.Count()}"));

    public FaceEngine(string cascadePath, string dataFolder)
    {
        if (!File.Exists(cascadePath)) throw new FileNotFoundException("Yüz algılama modeli bulunamadı.", cascadePath);
        detector = new CascadeClassifier(cascadePath);
        if (detector.Empty()) throw new InvalidDataException("Yüz algılama modeli okunamadı.");
        folder = dataFolder;
        Directory.CreateDirectory(folder);
        string index = Path.Combine(folder, "samples.json");
        if (File.Exists(index))
        {
            var loaded = JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(index)) ?? [];
            foreach (var sample in loaded)
            {
                if (Path.GetFileName(sample.File) != sample.File || !File.Exists(Path.Combine(folder, sample.File)))
                    throw new InvalidDataException("Örnek listesinde eksik veya geçersiz dosya var.");
                samples.Add(sample);
            }
        }
    }

    public Rect[] Detect(Mat image)
    {
        if (image.Empty()) throw new InvalidDataException("Görüntü okunamadı.");
        using var gray = Gray(image);
        return detector.DetectMultiScale(gray, 1.1, 5, HaarDetectionTypes.ScaleImage, new OpenCvSharp.Size(50, 50))
            .OrderBy(r => r.X).ToArray();
    }

    private static Mat Gray(Mat image)
    {
        var gray = new Mat();
        if (image.Channels() == 1) image.CopyTo(gray);
        else Cv2.CvtColor(image, gray, image.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);
        return gray;
    }

    public static Mat Normalize(Mat image, Rect face)
    {
        using var crop = new Mat(image, face);
        using var gray = Gray(crop);
        var resized = new Mat();
        Cv2.Resize(gray, resized, new OpenCvSharp.Size(160, 160));
        return resized;
    }

    public void Add(Mat image, Rect face, string label)
    {
        label = label.Trim();
        if (label.Length is < 1 or > 40 || label.Any(char.IsControl))
            throw new ArgumentException("Etiket 1–40 karakter olmalı ve kontrol karakteri içermemeli.");
        if (samples.Count >= 200) throw new InvalidOperationException("En fazla 200 örnek saklanabilir.");
        using var normalized = Normalize(image, face);
        string file = Guid.NewGuid().ToString("N") + ".png";
        if (!Cv2.ImWrite(Path.Combine(folder, file), normalized)) throw new IOException("Örnek kaydedilemedi.");
        samples.Add(new Sample(label, file));
        try
        {
            string temporary = Path.Combine(folder, "samples.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(samples));
            File.Move(temporary, Path.Combine(folder, "samples.json"), true);
        }
        catch { samples.RemoveAt(samples.Count - 1); File.Delete(Path.Combine(folder, file)); throw; }
        recognizer?.Dispose();
        recognizer = null;
    }

    public void Train()
    {
        if (LabelCount < 2) throw new InvalidOperationException("Eğitim için en az iki farklı etiket ekleyin.");
        var names = samples.Select(s => s.Label).Distinct().ToArray();
        var images = new List<Mat>();
        LBPHFaceRecognizer? candidate = null;
        try
        {
            foreach (var sample in samples)
            {
                var image = Cv2.ImRead(Path.Combine(folder, sample.File), ImreadModes.Grayscale);
                images.Add(image);
                if (image.Empty()) throw new InvalidDataException("Bir eğitim örneği okunamadı.");
            }
            candidate = LBPHFaceRecognizer.Create();
            candidate.Train(images, samples.Select(s => Array.IndexOf(names, s.Label)));
            recognizer?.Dispose();
            recognizer = candidate;
            candidate = null;
            labels = names;
        }
        finally { candidate?.Dispose(); foreach (var image in images) image.Dispose(); }
    }

    public FaceResult[] Analyze(Mat image)
    {
        return Detect(image).Select(rect =>
        {
            if (recognizer == null) return new FaceResult(rect, "Eğitilmedi", null);
            using var face = Normalize(image, rect);
            recognizer.Predict(face, out int id, out double distance);
            return new FaceResult(rect, id >= 0 && id < labels.Length && distance <= Threshold ? labels[id] : "Bilinmiyor", distance);
        }).ToArray();
    }

    public void Dispose() { recognizer?.Dispose(); detector.Dispose(); }
}
