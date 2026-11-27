using FaceRecognition;
using OpenCvSharp;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    checks++;
}
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or InvalidDataException) { rejected = true; }
    Check(rejected, name);
}
string folder = Path.Combine(Path.GetTempPath(), "YMH219-FaceChecks-" + Guid.NewGuid());
string assets = Path.Combine(AppContext.BaseDirectory, "Assets");
string cascade = Path.Combine(assets, "haarcascade_frontalface_default.xml");
try
{
    Reject(() => { using var missing = new FaceEngine("missing.xml", folder); }, "Missing detector reports an error");
    using (var engine = new FaceEngine(cascade, folder))
    {
        using var image = Cv2.ImRead(Path.Combine(assets, "synthetic-demo.png"));
        var faces = engine.Detect(image);
        Check(faces.Length == 2, "Cascade detects two synthetic faces");
        Check(engine.Analyze(image).All(f => f.Distance == null && f.Label == "Eğitilmedi"), "Untrained state is explicit");
        using var blank = new Mat(400, 400, MatType.CV_8UC3, Scalar.White);
        Check(engine.Detect(blank).Length == 0, "Blank image has no faces");
        using var empty = new Mat();
        Reject(() => engine.Detect(empty), "Empty image is rejected");
        Reject(() => engine.Add(image, faces[0], " "), "Empty label rejected");
        engine.Add(image, faces[0], "Ornek-A");
        Reject(engine.Train, "One label cannot train demonstration");
        engine.Add(image, faces[1], "Ornek-B");
        Check(engine.LabelCount == 2 && engine.SampleCount == 2, "Two labels stored");
        engine.Train();
        var predictions = engine.Analyze(image);
        Check(predictions[0].Label == "Ornek-A" && predictions[1].Label == "Ornek-B", "Training samples receive corresponding labels");
        Check(predictions.All(p => p.Distance is >= 0 and < 1), "Identical sample LBPH distance is near zero");
        using var variation = new Mat();
        image.ConvertTo(variation, -1, 0.85, 15);
        var varied = engine.Analyze(variation);
        Check(varied.Length == 2 && varied[0].Label == "Ornek-A" && varied[1].Label == "Ornek-B", "Brightness variation retains demo labels");
        engine.Threshold = 0;
        Check(engine.Analyze(variation).Any(f => f.Label == "Bilinmiyor"), "Strict threshold rejects nonidentical sample");
        engine.Add(image, faces[0], "Ornek-A");
        Check(!engine.IsTrained, "Adding a sample invalidates old training");
    }
    using (var reloaded = new FaceEngine(cascade, folder))
    {
        Check(reloaded.SampleCount == 3 && reloaded.LabelCount == 2, "Local samples survive restart");
        reloaded.Train();
        Check(reloaded.IsTrained, "Reloaded samples can train");
    }
    Console.WriteLine($"All {checks} checks passed. Synthetic demo only; not an accuracy benchmark.");
}
finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
