using System.Text.Json.Serialization;

public class SeamConfig
{
    [JsonPropertyName("input_image")]
    public string InputImage { get; set; } = string.Empty;

    [JsonPropertyName("output_folder")]
    public string OutputFolder { get; set; } = "output_frames";

    [JsonPropertyName("total_frames")]
    public int TotalFrames { get; set; } = 60;

    [JsonPropertyName("max_squish_percent")]
    public double MaxSquishPercent { get; set; } = 0.6;

    [JsonPropertyName("frame_jitter")]
    public int FrameJitter { get; set; } = 2;

    [JsonPropertyName("energy_noise")]
    public int EnergyNoise { get; set; } = 50;

    [JsonPropertyName("use_forward_energy")]
    public bool UseForwardEnergy { get; set; } = false;
}