namespace Content.Shared._RMC14.MotionDetector;

public interface IDetectorComponent
{
    List<Blip> Blips { get; set; }

    TimeSpan LastScan { get; set; }

    TimeSpan ScanDuration { get; set; }
}
