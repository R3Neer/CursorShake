using System.Globalization;
using System.Text.Json;
using CursorShake.Core.Cursors;
using CursorShake.Core.Input;
using CursorShake.Core.Runtime;
using CursorShake.Core.Shake;
using CursorShake.Windows.Input;

namespace CursorShake.Windows.Tests;

public sealed class CalibrationReplayTests
{
    [Fact]
    public void GuidedCalibrationTracesDistinguishSlowFromIntendedShakeAcrossAssumedDpi()
    {
        string root = RepositoryRoot(); var results = new List<ReplayResult>();
        foreach (double dpi in new[] { 96d,120d,144d,192d })
        foreach (string stage in new[] { "medium1", "slow", "fast", "medium2" })
            results.Add(Replay(Path.Combine(root,"tests","Fixtures","Calibration",stage+".csv"),stage,dpi));
        string report = Path.Combine(root,"artifacts","tests","calibration-replay.json"); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report,JsonSerializer.Serialize(new
        {
            Note="Original received_ms and physical deltas; source monitor DPI was not recorded. These are DPI sensitivity scenarios, not measured DPI certification. No global Windows input or cursor mutation.",
            Profile=new GestureProfile(), Results=results
        },new JsonSerializerOptions{WriteIndented=true}));
        foreach(var result in results)
        {
            if(result.Stage=="slow") Assert.True(result.Activations==0,$"Slow activated at assumed {result.Dpi} DPI: {result.Activations}");
            else Assert.True(result.Activations>0,$"{result.Stage} never activated at assumed {result.Dpi} DPI");
        }
    }
    private static ReplayResult Replay(string path,string stage,double dpi)
    {
        var clock=new Clock(); var engine=new Recorder(); var input=new PointerInput(new GestureProfile(),()=>PointerButtons.None,(_,_)=>((nint)1,dpi));
        using var detector=new ShakeDetector(input,engine,clock); var strokes=new List<double>(); var epochs=new HashSet<long>();
        input.Stroke+=stroke=>strokes.Add(stroke.OccurredAt.TotalMilliseconds);
        void Advance(TimeSpan time){clock.Now=time;detector.Advance(time);if(detector.Phase==GesturePhase.Growing)epochs.Add(detector.GestureEpoch);}
        int movements=0;
        foreach(string line in File.ReadLines(path).Skip(1))
        {
            var fields=line.Split(','); var now=TimeSpan.FromMilliseconds(double.Parse(fields[0],CultureInfo.InvariantCulture));
            while(detector.NextDeadline is {} deadline && deadline<now)Advance(deadline);
            clock.Now=now;
            input.Process(new(RawPointerKind.Move,int.Parse(fields[1],CultureInfo.InvariantCulture),int.Parse(fields[2],CultureInfo.InvariantCulture)),now);
            Advance(now); // Input wins equal-timestamp priority over semantic deadlines.
            movements++;
        }
        var end=clock.Now+TimeSpan.FromSeconds(2);
        while(detector.NextDeadline is {} deadline && deadline<=end)Advance(deadline);
        Advance(end);
        double[] intervals=strokes.Zip(strokes.Skip(1),(a,b)=>b-a).ToArray();
        return new(stage,dpi,movements,strokes.Count,intervals,epochs.Count,engine.Peak);
    }
    private static string RepositoryRoot()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(Directory.Exists(Path.Combine(directory.FullName,"tests","Fixtures","Calibration")))return directory.FullName;
        throw new DirectoryNotFoundException("Calibration fixtures were not found.");
    }
    private sealed class Clock:IMonotonicClock {public TimeSpan Now{get;set;}}
    private sealed class Recorder:ICursorEngine {public double Peak{get;private set;}=1;public void SetScale(ScaleSample sample)=>Peak=Math.Max(Peak,sample.Scale);}
    private sealed record ReplayResult(string Stage,double Dpi,int Movements,int Strokes,double[] IntervalsMs,int Activations,double PeakScale);
}
