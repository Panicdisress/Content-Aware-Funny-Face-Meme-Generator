using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using OpenCvSharp;
using System.Collections.Generic;

public partial class MainWindow : System.Windows.Window
{
    private bool isProcessing = false;
    private Random randomSource = new Random();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            txtLog.AppendText($"[{time}] {message}\n");
            txtLog.ScrollToEnd();
        });
    }

    private void BtnBrowseImage_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog { Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png" };
        if (ofd.ShowDialog() == true) { txtImagePath.Text = ofd.FileName; Log($"Selected image: {ofd.FileName}"); }
    }

    private void BtnBrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog { Filter = "Video files (*.mp4;*.avi;*.mov)|*.mp4;*.avi;*.mov" };
        if (ofd.ShowDialog() == true) { txtVideoPath.Text = ofd.FileName; Log($"Selected video: {ofd.FileName}"); }
    }

    private void BtnBrowseStaticVideo_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog { Filter = "Video files (*.mp4;*.avi;*.mov)|*.mp4;*.avi;*.mov" };
        if (ofd.ShowDialog() == true) { txtStaticVideoPath.Text = ofd.FileName; Log($"Selected static video: {ofd.FileName}"); }
    }

    private void BtnOpenFrames_Click(object sender, RoutedEventArgs e)
    {
        string target = "output_frames";
        if (!Directory.Exists(target)) Directory.CreateDirectory(target);
        Process.Start("explorer.exe", target);
    }

    private void BtnOpenVideos_Click(object sender, RoutedEventArgs e)
    {
        string target = "output_videos";
        if (!Directory.Exists(target)) Directory.CreateDirectory(target);
        Process.Start("explorer.exe", target);
    }

    // ====================================================================================
    // PROGRESSIVE PREVIEW LOGIC
    // ====================================================================================
    private void BtnPreviewProgHalf_Click(object sender, RoutedEventArgs e) => ProcessProgressivePreview(true);
    private void BtnPreviewProgFull_Click(object sender, RoutedEventArgs e) => ProcessProgressivePreview(false);

    private async void ProcessProgressivePreview(bool isHalfway)
    {
        if (isProcessing) return;
        string targetPath = txtVideoPath.Text;

        if (!File.Exists(targetPath))
        {
            MessageBox.Show("Please select a valid progressive video first.", "Error");
            return;
        }

        isProcessing = true;
        btnPreviewProgHalf.IsEnabled = false;
        btnPreviewProgFull.IsEnabled = false;
        lblStatus.Text = isHalfway ? "Seeking to Middle Frame..." : "Seeking to End Frame...";
        lblStatus.Foreground = System.Windows.Media.Brushes.Orange;

        bool scaleEnabled = chkEnableScaling.IsChecked == true;
        int maxDim = rbSize400.IsChecked == true ? 400 : rbSize600.IsChecked == true ? 600 : 1200;
        double targetIntensity = isHalfway ? (sliderVidIntensity.Value / 2.0) : sliderVidIntensity.Value;
        double jitter = sliderVidJitter.Value;
        double centerBias = sliderVidBias.Value;
        int.TryParse(txtVidSeed.Text, out int seed);

        await Task.Run(() =>
        {
            try
            {
                using VideoCapture cap = new VideoCapture(targetPath);
                if (!cap.IsOpened()) throw new Exception("Failed to open video file.");

                int totalFrames = (int)cap.FrameCount;
                int targetFrameIndex = isHalfway ? (totalFrames / 2) : (totalFrames - 2);
                if (targetFrameIndex < 0) targetFrameIndex = 0;

                cap.Set(VideoCaptureProperties.PosFrames, targetFrameIndex);
                using Mat targetFrame = new Mat();
                if (!cap.Read(targetFrame) || targetFrame.Empty()) throw new Exception("Failed to read frame.");

                using Context context = Context.Create(builder => builder.Cuda().OpenCL());
                var cudaDevice = context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda);
                if (cudaDevice == null) throw new Exception("No active NVIDIA CUDA device discovered!");
                using Accelerator gpu = cudaDevice.CreateAccelerator(context);
                using var seamCarver = new SeamCarver(gpu);

                string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
                using CascadeClassifier faceDetector = File.Exists(cascadePath) ? new CascadeClassifier(cascadePath) : null;

                using Mat scaledFrame = ScaleSafely(targetFrame, scaleEnabled, maxDim);
                using Mat finalImage = CarveSingleFrame(scaledFrame, targetIntensity, jitter, seed, centerBias, gpu, seamCarver, faceDetector);

                string previewFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_progressive.jpg");
                Cv2.ImWrite(previewFile, finalImage);

                Log($"Preview frame {targetFrameIndex}/{totalFrames} generated.");
                Process.Start(new ProcessStartInfo(previewFile) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"PREVIEW ERROR: {ex.Message}");
            }
        });

        isProcessing = false;
        btnPreviewProgHalf.IsEnabled = true;
        btnPreviewProgFull.IsEnabled = true;
        lblStatus.Text = "Ready";
        lblStatus.Foreground = System.Windows.Media.Brushes.Green;
    }

    // ====================================================================================
    // STATIC PREVIEW LOGIC
    // ====================================================================================
    private async void BtnPreviewStaticFrame_Click(object sender, RoutedEventArgs e)
    {
        if (isProcessing) return;
        string targetPath = txtStaticVideoPath.Text;

        if (!File.Exists(targetPath)) { MessageBox.Show("Please select a valid video first.", "Error"); return; }

        isProcessing = true;
        btnPreviewStaticFrame.IsEnabled = false;
        lblStatus.Text = "Rendering Preview...";
        lblStatus.Foreground = System.Windows.Media.Brushes.Blue;

        bool scaleEnabled = chkEnableScaling.IsChecked == true;
        int maxDim = rbSize400.IsChecked == true ? 400 : rbSize600.IsChecked == true ? 600 : 1200;
        double intensity = sliderStaticVidIntensity.Value;
        double jitter = sliderStaticVidJitter.Value;
        double centerBias = sliderStaticBias.Value;
        int.TryParse(txtStaticSeed.Text, out int seed);

        await Task.Run(() =>
        {
            try
            {
                using VideoCapture cap = new VideoCapture(targetPath);
                if (!cap.IsOpened()) throw new Exception("Failed to open video file.");

                using Mat firstFrame = new Mat();
                if (!cap.Read(firstFrame) || firstFrame.Empty()) throw new Exception("Video is empty.");

                using Context context = Context.Create(builder => builder.Cuda().OpenCL());
                var cudaDevice = context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda);
                if (cudaDevice == null) throw new Exception("No active NVIDIA CUDA device discovered!");
                using Accelerator gpu = cudaDevice.CreateAccelerator(context);
                using var seamCarver = new SeamCarver(gpu);

                string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
                using CascadeClassifier faceDetector = File.Exists(cascadePath) ? new CascadeClassifier(cascadePath) : null;

                using Mat scaledFrame = ScaleSafely(firstFrame, scaleEnabled, maxDim);
                using Mat finalImage = CarveSingleFrame(scaledFrame, intensity, jitter, seed, centerBias, gpu, seamCarver, faceDetector);

                string previewFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_distort.jpg");
                Cv2.ImWrite(previewFile, finalImage);

                Log($"Preview frame generated and opened.");
                Process.Start(new ProcessStartInfo(previewFile) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"PREVIEW ERROR: {ex.Message}");
            }
        });

        isProcessing = false;
        btnPreviewStaticFrame.IsEnabled = true;
        lblStatus.Text = "Ready";
        lblStatus.Foreground = System.Windows.Media.Brushes.Green;
    }

    private async void BtnPreviewEnergyMap_Click(object sender, RoutedEventArgs e)
    {
        if (isProcessing) return;
        string targetPath = txtStaticVideoPath.Text;

        if (!File.Exists(targetPath)) { MessageBox.Show("Please select a valid video first.", "Error"); return; }

        isProcessing = true;
        btnPreviewEnergyMap.IsEnabled = false;
        lblStatus.Text = "Rendering Thermal Energy Map...";
        lblStatus.Foreground = System.Windows.Media.Brushes.Purple;

        bool scaleEnabled = chkEnableScaling.IsChecked == true;
        int maxDim = rbSize400.IsChecked == true ? 400 : rbSize600.IsChecked == true ? 600 : 1200;
        double centerBias = sliderStaticBias.Value;
        int.TryParse(txtStaticSeed.Text, out int seed);
        double jitter = sliderStaticVidJitter.Value;

        await Task.Run(() =>
        {
            try
            {
                using VideoCapture cap = new VideoCapture(targetPath);
                if (!cap.IsOpened()) throw new Exception("Failed to open video file.");

                using Mat firstFrame = new Mat();
                if (!cap.Read(firstFrame) || firstFrame.Empty()) throw new Exception("Video is empty.");

                using Context context = Context.Create(builder => builder.Cuda().OpenCL());
                var cudaDevice = context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda);
                if (cudaDevice == null) throw new Exception("No active NVIDIA CUDA device discovered!");
                using Accelerator gpu = cudaDevice.CreateAccelerator(context);
                using var seamCarver = new SeamCarver(gpu);

                string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
                using CascadeClassifier faceDetector = File.Exists(cascadePath) ? new CascadeClassifier(cascadePath) : null;

                using Mat scaledFrame = ScaleSafely(firstFrame, scaleEnabled, maxDim);
                using Mat heatMap = GenerateEnergyHeatmap(scaledFrame, jitter, seed, centerBias, gpu, seamCarver, faceDetector);

                string previewFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview_energy_heatmap.jpg");
                Cv2.ImWrite(previewFile, heatMap);

                Log($"Energy Heatmap generated.");
                Process.Start(new ProcessStartInfo(previewFile) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"HEATMAP ERROR: {ex.Message}");
            }
        });

        isProcessing = false;
        btnPreviewEnergyMap.IsEnabled = true;
        lblStatus.Text = "Ready";
        lblStatus.Foreground = System.Windows.Media.Brushes.Green;
    }

    private Mat GenerateEnergyHeatmap(Mat scaledFrame, double jitterStrength, int seed, double centerBias, Accelerator gpu, SeamCarver seamCarver, CascadeClassifier? faceDetector)
    {
        Random seededRandom = new Random(seed);
        using Mat bgraFrame = new Mat();
        Cv2.CvtColor(scaledFrame, bgraFrame, ColorConversionCodes.BGR2BGRA);

        int currentWidth = bgraFrame.Width;
        int currentHeight = bgraFrame.Height;
        float targetX = currentWidth / 2.0f;
        float targetY = currentHeight / 2.0f;

        if (faceDetector != null && centerBias != 0)
        {
            using Mat grayFrame = new Mat();
            Cv2.CvtColor(scaledFrame, grayFrame, ColorConversionCodes.BGR2GRAY);
            lock (faceDetector) // Thread-safe lock!
            {
                OpenCvSharp.Rect[] faces = faceDetector.DetectMultiScale(grayFrame, 1.1, 4);
                if (faces.Length > 0)
                {
                    targetX = faces[0].X + (faces[0].Width / 2.0f);
                    targetY = faces[0].Y + (faces[0].Height / 2.0f);
                }
            }
        }

        var d_colorImage = gpu.Allocate2DDenseY<int>(new Index2D(currentWidth, currentHeight));
        int[,] cpuColorImage = new int[currentWidth, currentHeight];
        var indexer = bgraFrame.GetUnsafeGenericIndexer<Vec4b>();

        for (int y = 0; y < currentHeight; y++)
        {
            for (int x = 0; x < currentWidth; x++)
            {
                var vec = indexer[y, x];
                cpuColorImage[x, y] = (vec.Item3 << 24) | (vec.Item2 << 16) | (vec.Item1 << 8) | vec.Item0;
            }
        }
        d_colorImage.CopyFromCPU(cpuColorImage);

        using var d_energyMap = gpu.Allocate2DDenseY<float>(new Index2D(currentWidth, currentHeight));
        seamCarver.CalculateEnergy(d_colorImage, d_energyMap);
        gpu.Synchronize();

        float[,] localEnergy = d_energyMap.GetAsArray2D();
        d_colorImage.Dispose();

        float minEnergy = float.MaxValue;
        float maxEnergy = float.MinValue;
        float noiseBound = (float)(jitterStrength * 2.5);

        for (int y = 0; y < currentHeight; y++)
        {
            for (int x = 0; x < currentWidth; x++)
            {
                if (jitterStrength > 0)
                {
                    localEnergy[x, y] += (float)(seededRandom.NextDouble() * noiseBound);
                }

                if (centerBias != 0)
                {
                    float distX = (x - targetX) / (currentWidth / 2.0f);
                    float distY = (y - targetY) / (currentHeight / 2.0f);
                    float distance = (float)Math.Sqrt((distX * distX) + (distY * distY));
                    float faceWeight = Math.Max(0, 1.0f - distance);

                    float multiplier = 1.0f;
                    if (centerBias > 0)
                    {
                        multiplier = 1.0f + (faceWeight * (float)(centerBias / 100.0f));
                    }
                    else
                    {
                        float reduction = faceWeight * (float)(-centerBias / 550.0f);
                        multiplier = Math.Max(0.1f, 1.0f - reduction);
                    }
                    localEnergy[x, y] *= multiplier;
                }

                if (x > 2 && x < currentWidth - 2 && y > 2 && y < currentHeight - 2)
                {
                    if (localEnergy[x, y] < minEnergy) minEnergy = localEnergy[x, y];
                    if (localEnergy[x, y] > maxEnergy) maxEnergy = localEnergy[x, y];
                }
            }
        }

        Mat grayEnergy = new Mat(currentHeight, currentWidth, MatType.CV_8UC1);
        var outIndexer = grayEnergy.GetUnsafeGenericIndexer<byte>();
        float range = maxEnergy - minEnergy;
        if (range <= 0) range = 1;

        for (int y = 0; y < currentHeight; y++)
        {
            for (int x = 0; x < currentWidth; x++)
            {
                float normalized = ((localEnergy[x, y] - minEnergy) / range) * 255f;
                if (normalized > 255f) normalized = 255f;
                if (normalized < 0f) normalized = 0f;
                outIndexer[y, x] = (byte)normalized;
            }
        }

        Mat heatMap = new Mat();
        Cv2.ApplyColorMap(grayEnergy, heatMap, ColormapTypes.Jet);

        if (faceDetector != null && centerBias != 0)
        {
            Cv2.DrawMarker(heatMap, new OpenCvSharp.Point((int)targetX, (int)targetY), Scalar.LimeGreen, MarkerTypes.Cross, 25, 3);
        }

        grayEnergy.Dispose();
        return heatMap;
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (isProcessing) return;
        int selectedTab = tabMain.SelectedIndex;

        // FIX 1: Swapped Index 1 and 2 to match your new UI tab order
        string targetPath = selectedTab == 0 ? txtImagePath.Text : (selectedTab == 1 ? txtStaticVideoPath.Text : txtVideoPath.Text);

        if (!File.Exists(targetPath))
        {
            MessageBox.Show("Please select a valid file first.", "Error");
            return;
        }

        isProcessing = true;
        btnStart.IsEnabled = false;
        lblStatus.Text = "Spinning up GPU Engine (Multi-Threaded)...";
        lblStatus.Foreground = System.Windows.Media.Brushes.Blue;
        txtLog.Clear();
        progressBar.Value = 0;

        bool scaleEnabled = chkEnableScaling.IsChecked == true;
        int maxDim = rbSize400.IsChecked == true ? 400 : rbSize600.IsChecked == true ? 600 : 1200;
        bool limitFps = chkLimitFps.IsChecked == true;
        int maxAllowedFps = rbFpsLimit15.IsChecked == true ? 15 : rbFpsLimit30.IsChecked == true ? 30 : 999;
        int batchSize = (int)sliderBatchSize.Value;

        int totalFrames = int.Parse(txtFrames.Text);
        double imgIntensity = sliderIntensity.Value;
        double imgJitter = sliderJitter.Value;
        bool convertToVideo = chkConvertVideo.IsChecked == true;
        string fps = rbFps12.IsChecked == true ? "12" : rbFps24.IsChecked == true ? "24" : "30";

        double maxIntensityPercent = sliderVidIntensity.Value;
        double vidJitter = sliderVidJitter.Value;
        double vidBias = sliderVidBias.Value;
        int.TryParse(txtVidSeed.Text, out int vidSeed);

        double staticIntensity = sliderStaticVidIntensity.Value;
        double staticJitter = sliderStaticVidJitter.Value;
        double staticBias = sliderStaticBias.Value;
        int.TryParse(txtStaticSeed.Text, out int staticSeed);

        await Task.Run(() =>
        {
            Stopwatch masterTimer = Stopwatch.StartNew();
            try
            {
                if (selectedTab == 0) // Img2Vid
                {
                    using Mat input = Cv2.ImRead(targetPath, ImreadModes.Color);
                    using Mat scaled = ScaleSafely(input, scaleEnabled, maxDim);
                    RunImageToVideoEngineBatch(scaled, totalFrames, imgIntensity, imgJitter, batchSize);
                    if (convertToVideo) RunFFmpegFromDisk("output_frames", fps);
                }
                // FIX 2: Index 1 is now Uniform (Static)
                else if (selectedTab == 1)
                {
                    RunVideoToVideoEngineBatch(targetPath, staticIntensity, staticJitter, scaleEnabled, maxDim, limitFps, maxAllowedFps, true, staticSeed, staticBias, batchSize);
                }
                // FIX 3: Index 2 is now Progressive
                else if (selectedTab == 2)
                {
                    RunVideoToVideoEngineBatch(targetPath, maxIntensityPercent, vidJitter, scaleEnabled, maxDim, limitFps, maxAllowedFps, false, vidSeed, vidBias, batchSize);
                }

                masterTimer.Stop();
                Log($"\n========================================");
                Log($"TOTAL TIME: {masterTimer.Elapsed.TotalSeconds:F2} seconds");
                Log($"========================================");

                Dispatcher.Invoke(() => { lblStatus.Text = "Process Successfully Complete!"; lblStatus.Foreground = System.Windows.Media.Brushes.Green; });
            }
            catch (Exception ex)
            {
                masterTimer.Stop();
                Log($"CRITICAL RUNTIME ERROR: {ex.Message}");
                Dispatcher.Invoke(() => { lblStatus.Text = "Pipeline Failed!"; lblStatus.Foreground = System.Windows.Media.Brushes.Red; });
            }
        });

        isProcessing = false;
        btnStart.IsEnabled = true;
    }

    private Mat ScaleSafely(Mat input, bool enabled, int maxAllowed)
    {
        Mat output = new Mat();
        int longEdge = Math.Max(input.Width, input.Height);
        if (enabled && longEdge > maxAllowed)
        {
            double ratio = (double)maxAllowed / longEdge;
            Cv2.Resize(input, output, new OpenCvSharp.Size((int)(input.Width * ratio), (int)(input.Height * ratio)), 0, 0, InterpolationFlags.Area);
        }
        else
        {
            input.CopyTo(output);
        }
        return output;
    }

    // ====================================================================================
    // MULTI-THREADED RAM BUCKET: VIDEO PIPELINE
    // ====================================================================================
    private void RunVideoToVideoEngineBatch(string videoPath, double targetIntensity, double jitterStrength, bool scaleEnabled, int maxDim, bool limitFps, int maxAllowedFps, bool isStatic, int seed, double centerBias, int batchSize)
    {
        string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(ffmpegPath)) throw new Exception("ffmpeg.exe missing!");

        string outputFolder = "output_videos";
        if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
        string outputFile = Path.Combine(outputFolder, $"distorted_video_{DateTime.Now.Ticks}.mp4");

        string workingVideoPath = videoPath;
        if (limitFps)
        {
            using VideoCapture tempCap = new VideoCapture(videoPath);
            if (tempCap.Fps > maxAllowedFps)
            {
                Log($"Pre-processing: Decimating video from {Math.Round(tempCap.Fps)}fps down to {maxAllowedFps}fps...");
                string tempFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_decimated.mp4");

                Process preProc = new Process();
                preProc.StartInfo.FileName = ffmpegPath;
                preProc.StartInfo.Arguments = $"-y -i \"{videoPath}\" -r {maxAllowedFps} -c:v libx264 -preset ultrafast -crf 18 -an \"{tempFile}\"";
                preProc.StartInfo.UseShellExecute = false;
                preProc.StartInfo.CreateNoWindow = true;
                preProc.Start();
                preProc.WaitForExit();

                if (preProc.ExitCode == 0)
                {
                    workingVideoPath = tempFile;
                    Log("Pre-processing complete! Launching GPU Engine...");
                }
            }
        }

        using VideoCapture cap = new VideoCapture(workingVideoPath);
        if (!cap.IsOpened()) throw new Exception("Failed to open video file.");

        double sourceFps = cap.Fps;
        int totalSourceFrames = (int)cap.FrameCount;
        Log($"Video loaded for GPU: {totalSourceFrames} frames @ {Math.Round(sourceFps)}fps. Batch limit: {batchSize}");

        Process process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.Arguments = $"-y -f image2pipe -vcodec mjpeg -framerate {sourceFps} -i - -an -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:v libx264 -crf 18 -pix_fmt yuv420p \"{outputFile}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        process.ErrorDataReceived += (s, e) => { };
        process.Start();
        process.BeginErrorReadLine();

        using Context context = Context.Create(builder => builder.Cuda().OpenCL());
        var cudaDevice = context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda);
        if (cudaDevice == null) throw new Exception("No active NVIDIA CUDA device discovered!");

        using Accelerator gpu = cudaDevice.CreateAccelerator(context);

        string cascadePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "haarcascade_frontalface_default.xml");
        using CascadeClassifier faceDetector = File.Exists(cascadePath) ? new CascadeClassifier(cascadePath) : null;

        List<Mat> batchFrames = new List<Mat>();
        List<double> batchIntensities = new List<double>();
        int frameIndex = 0;

        while (frameIndex < totalSourceFrames)
        {
            batchFrames.Clear();
            batchIntensities.Clear();

            // 1. Fill the RAM Bucket
            for (int b = 0; b < batchSize && frameIndex < totalSourceFrames; b++)
            {
                Mat frame = new Mat();
                if (!cap.Read(frame) || frame.Empty()) break;
                batchFrames.Add(frame);

                double currentIntensity = isStatic ? targetIntensity : (targetIntensity * ((double)frameIndex / (totalSourceFrames - 1)));
                batchIntensities.Add(currentIntensity);
                frameIndex++;
            }

            if (batchFrames.Count == 0) break;

            Mat[] processedBatch = new Mat[batchFrames.Count];

            // 2. Multi-Threaded Math Execution (Maxes out CPU & GPU instantly)
            Parallel.For(0, batchFrames.Count, new ParallelOptions { MaxDegreeOfParallelism = batchSize }, j =>
            {
                // Each thread gets its own SeamCarver memory context to prevent data overlap crashes
                using var threadLocalCarver = new SeamCarver(gpu);
                using Mat scaledFrame = ScaleSafely(batchFrames[j], scaleEnabled, maxDim);
                processedBatch[j] = CarveSingleFrame(scaledFrame, batchIntensities[j], jitterStrength, seed, centerBias, gpu, threadLocalCarver, faceDetector);
                batchFrames[j].Dispose();
            });

            // 3. Write out sequentially to FFmpeg stream
            for (int j = 0; j < processedBatch.Length; j++)
            {
                Cv2.ImEncode(".jpg", processedBatch[j], out byte[] imgBuffer);
                process.StandardInput.BaseStream.Write(imgBuffer, 0, imgBuffer.Length);
                processedBatch[j].Dispose();
            }

            int progress = (int)(((float)frameIndex / totalSourceFrames) * 100);
            Dispatcher.Invoke(() => progressBar.Value = progress);
            Log($"Batch processed. Total Carved: {frameIndex}/{totalSourceFrames}");
        }

        process.StandardInput.Close();
        process.WaitForExit();

        if (workingVideoPath != videoPath && File.Exists(workingVideoPath))
        {
            try { File.Delete(workingVideoPath); } catch { }
        }

        Log(process.ExitCode == 0 ? $"SUCCESS: Saved silent video to {outputFile}" : $"ERROR: FFmpeg exited with code {process.ExitCode}");
    }


    // ====================================================================================
    // MULTI-THREADED RAM BUCKET: IMAGE PIPELINE
    // ====================================================================================
    private void RunImageToVideoEngineBatch(Mat nativeImage, int totalFrames, double targetIntensity, double jitterStrength, int batchSize)
    {
        string outputFolder = "output_frames";
        if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

        using Context context = Context.Create(builder => builder.Cuda().OpenCL());
        var cudaDevice = context.Devices.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda);
        if (cudaDevice == null) throw new Exception("No active NVIDIA CUDA device discovered!");

        using Accelerator gpu = cudaDevice.CreateAccelerator(context);
        Log($"GPU Acceleration Online: {gpu.Name}. Batch limit: {batchSize}");

        int frameIndex = 0;
        List<int> batchFrameIndices = new List<int>();

        while (frameIndex < totalFrames)
        {
            batchFrameIndices.Clear();
            for (int b = 0; b < batchSize && frameIndex < totalFrames; b++)
            {
                batchFrameIndices.Add(frameIndex);
                frameIndex++;
            }

            Mat[] processedBatch = new Mat[batchFrameIndices.Count];

            Parallel.For(0, batchFrameIndices.Count, new ParallelOptions { MaxDegreeOfParallelism = batchSize }, j =>
            {
                using var threadLocalCarver = new SeamCarver(gpu);
                double currentIntensity = targetIntensity * ((double)batchFrameIndices[j] / (totalFrames - 1));

                // Uses CarveSingleFrame for images too, treating the original image as the frame to squish!
                processedBatch[j] = CarveSingleFrame(nativeImage, currentIntensity, jitterStrength, 42, 0, gpu, threadLocalCarver, null);
            });

            for (int j = 0; j < processedBatch.Length; j++)
            {
                string framePath = Path.Combine(outputFolder, $"frame_{batchFrameIndices[j]:D4}.png");
                Cv2.ImWrite(framePath, processedBatch[j]);
                processedBatch[j].Dispose();
            }

            int progress = (int)(((float)frameIndex / totalFrames) * 100);
            Dispatcher.Invoke(() => progressBar.Value = progress);
            Log($"Batch processed. Total Rendered: {frameIndex}/{totalFrames}");
        }
    }


    // ====================================================================================
    // CORE GPU CARVING LOGIC (Thread Safe for Batching)
    // ====================================================================================
    private Mat CarveSingleFrame(Mat scaledFrame, double intensityPercent, double jitterStrength, int seed, double centerBias, Accelerator gpu, SeamCarver seamCarver, CascadeClassifier? faceDetector)
    {
        Random seededRandom = new Random(seed);

        using Mat bgraFrame = new Mat();
        Cv2.CvtColor(scaledFrame, bgraFrame, ColorConversionCodes.BGR2BGRA);

        int currentWidth = bgraFrame.Width;
        int currentHeight = bgraFrame.Height;

        float targetX = currentWidth / 2.0f;
        float targetY = currentHeight / 2.0f;

        if (faceDetector != null && centerBias != 0)
        {
            using Mat grayFrame = new Mat();
            Cv2.CvtColor(scaledFrame, grayFrame, ColorConversionCodes.BGR2GRAY);

            // THREAD LOCK: OpenCV face tracking can crash if 10 threads hit it at the exact same millisecond.
            // This safely queues them up for the microsecond it takes to scan the face.
            lock (faceDetector)
            {
                OpenCvSharp.Rect[] faces = faceDetector.DetectMultiScale(grayFrame, 1.1, 4);
                if (faces.Length > 0)
                {
                    targetX = faces[0].X + (faces[0].Width / 2.0f);
                    targetY = faces[0].Y + (faces[0].Height / 2.0f);
                }
            }
        }

        double scaleFactor = 1.0 - (intensityPercent / 100.0);
        int targetWidth = (int)(currentWidth * scaleFactor);
        int targetHeight = (int)(currentHeight * scaleFactor);

        int seamsToRemoveX = currentWidth - targetWidth;
        int seamsToRemoveY = currentHeight - targetHeight;

        if (seamsToRemoveX <= 0 && seamsToRemoveY <= 0)
        {
            Mat uncarved = new Mat();
            Cv2.CvtColor(bgraFrame, uncarved, ColorConversionCodes.BGRA2BGR);
            return uncarved;
        }

        var d_colorImage = gpu.Allocate2DDenseY<int>(new Index2D(currentWidth, currentHeight));
        int[,] cpuColorImage = new int[currentWidth, currentHeight];
        var indexer = bgraFrame.GetUnsafeGenericIndexer<Vec4b>();

        for (int y = 0; y < currentHeight; y++)
        {
            for (int x = 0; x < currentWidth; x++)
            {
                var vec = indexer[y, x];
                cpuColorImage[x, y] = (vec.Item3 << 24) | (vec.Item2 << 16) | (vec.Item1 << 8) | vec.Item0;
            }
        }
        d_colorImage.CopyFromCPU(cpuColorImage);

        // --- 1. CARVE X AXIS (WIDTH) ---
        for (int s = 0; s < seamsToRemoveX; s++)
        {
            using var d_energyMap = gpu.Allocate2DDenseY<float>(new Index2D(currentWidth, currentHeight));
            seamCarver.CalculateEnergy(d_colorImage, d_energyMap);
            gpu.Synchronize();

            float[,] localEnergy = d_energyMap.GetAsArray2D();
            float noiseBound = (float)(jitterStrength * 2.5);

            for (int y = 0; y < currentHeight; y++)
            {
                for (int x = 0; x < currentWidth; x++)
                {
                    if (jitterStrength > 0)
                    {
                        localEnergy[x, y] += (float)(seededRandom.NextDouble() * noiseBound);
                    }

                    if (centerBias != 0)
                    {
                        float distX = (x - targetX) / (currentWidth / 2.0f);
                        float distY = (y - targetY) / (currentHeight / 2.0f);
                        float distance = (float)Math.Sqrt((distX * distX) + (distY * distY));
                        float faceWeight = Math.Max(0, 1.0f - distance);

                        float multiplier = 1.0f;
                        if (centerBias > 0)
                        {
                            multiplier = 1.0f + (faceWeight * (float)(centerBias / 100.0f));
                        }
                        else
                        {
                            float reduction = faceWeight * (float)(-centerBias / 550.0f);
                            multiplier = Math.Max(0.1f, 1.0f - reduction);
                        }
                        localEnergy[x, y] *= multiplier;
                    }
                }
            }

            int[] seamPath = seamCarver.FindVerticalSeam(localEnergy, currentWidth, currentHeight);
            using var d_seamPath = gpu.Allocate1D<int>(currentHeight);
            d_seamPath.CopyFromCPU(seamPath);

            int newWidth = currentWidth - 1;
            var d_newImage = gpu.Allocate2DDenseY<int>(new Index2D(newWidth, currentHeight));
            seamCarver.RemoveVerticalSeam(d_colorImage, d_seamPath, d_newImage);
            gpu.Synchronize();

            d_colorImage.Dispose();
            d_colorImage = d_newImage;
            currentWidth = newWidth;
        }

        // --- 2. CARVE Y AXIS (HEIGHT) ---
        for (int s = 0; s < seamsToRemoveY; s++)
        {
            using var d_energyMap = gpu.Allocate2DDenseY<float>(new Index2D(currentWidth, currentHeight));
            seamCarver.CalculateEnergy(d_colorImage, d_energyMap);
            gpu.Synchronize();

            float[,] localEnergy = d_energyMap.GetAsArray2D();
            float noiseBound = (float)(jitterStrength * 2.5);

            for (int y = 0; y < currentHeight; y++)
            {
                for (int x = 0; x < currentWidth; x++)
                {
                    if (jitterStrength > 0)
                    {
                        localEnergy[x, y] += (float)(seededRandom.NextDouble() * noiseBound);
                    }

                    if (centerBias != 0)
                    {
                        float distX = (x - targetX) / (currentWidth / 2.0f);
                        float distY = (y - targetY) / (currentHeight / 2.0f);
                        float distance = (float)Math.Sqrt((distX * distX) + (distY * distY));
                        float faceWeight = Math.Max(0, 1.0f - distance);

                        float multiplier = 1.0f;
                        if (centerBias > 0)
                        {
                            multiplier = 1.0f + (faceWeight * (float)(centerBias / 100.0f));
                        }
                        else
                        {
                            float reduction = faceWeight * (float)(-centerBias / 550.0f);
                            multiplier = Math.Max(0.1f, 1.0f - reduction);
                        }
                        localEnergy[x, y] *= multiplier;
                    }
                }
            }

            int[] seamPath = seamCarver.FindHorizontalSeam(localEnergy, currentWidth, currentHeight);
            using var d_seamPath = gpu.Allocate1D<int>(currentWidth);
            d_seamPath.CopyFromCPU(seamPath);

            int newHeight = currentHeight - 1;
            var d_newImage = gpu.Allocate2DDenseY<int>(new Index2D(currentWidth, newHeight));
            seamCarver.RemoveHorizontalSeam(d_colorImage, d_seamPath, d_newImage);
            gpu.Synchronize();

            d_colorImage.Dispose();
            d_colorImage = d_newImage;
            currentHeight = newHeight;
        }

        int[,] frameData = d_colorImage.GetAsArray2D();
        using Mat outputMat = new Mat(currentHeight, currentWidth, MatType.CV_8UC4);
        var outIndexer = outputMat.GetUnsafeGenericIndexer<Vec4b>();

        for (int y = 0; y < currentHeight; y++)
        {
            for (int x = 0; x < currentWidth; x++)
            {
                int pixel = frameData[x, y];
                outIndexer[y, x] = new Vec4b((byte)(pixel & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)((pixel >> 16) & 0xFF), 255);
            }
        }
        d_colorImage.Dispose();

        Mat finalImage = new Mat();
        Cv2.CvtColor(outputMat, finalImage, ColorConversionCodes.BGRA2BGR);
        return finalImage;
    }

    private void RunFFmpegFromDisk(string inputFolder, string fps)
    {
        string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (!File.Exists(ffmpegPath)) return;

        string outputFolder = "output_videos";
        if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
        string outputFile = Path.Combine(outputFolder, $"output_video_{DateTime.Now.Ticks}.mp4");

        Log($"Encoding MP4 at {fps} fps...");
        Process process = new Process();
        process.StartInfo.FileName = ffmpegPath;
        process.StartInfo.Arguments = $"-y -framerate {fps} -i \"{inputFolder}\\frame_%04d.png\" -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:v libx264 -crf 18 -pix_fmt yuv420p \"{outputFile}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        process.ErrorDataReceived += (s, e) => { };
        process.Start();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode == 0) Log($"SUCCESS: Video saved to {outputFile}");
    }
}